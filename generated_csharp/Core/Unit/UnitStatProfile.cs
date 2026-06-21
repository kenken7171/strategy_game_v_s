// =============================================================================
//  ChronicleKnights — Core/Unit/UnitStatProfile.cs
// -----------------------------------------------------------------------------
//  ユニットの「実効戦闘ステータス」を解決する純粋関数。素のジョブ値（JobMaster の
//  JobStats）へ ① レベル成長 ② 三段階加齢（修業期→全盛期。衰退なし）の係数を掛けて返す。
//
//  ★ なぜ必要か（仕様 instructions.md・旧 TS 版に在った成長が C# 移植で欠落していた）:
//    instructions.md は「騎士は生まれ・育ち・全盛期を迎え…」と成長曲線を謳い、旧 TS 版 Unit は
//    growthFactor（修業期=age/peakStart の線形成長 / 全盛期=1.0 / 衰退期=0.97^…）を持っていた。
//    C# 版は Unit をステータスレス化した際にこれを落とし、Level も年齢も戦闘力に乗らなかった。
//    本クラスはレベル成長と「全盛期までの成長」を復活させる（衰退は当面入れない＝旅団長決定）。
//
//  ★ 係数（旅団長決定 2026-06-21）:
//    レベル成長 : Lv ごと +25%（Lv1=×1.0 / Lv2=×1.25 / Lv3=×1.5）。
//    加齢（三段階・旧 TS 版と同式）:
//      - 修業期 (age < MaturityAge)         : ageFactor = age / MaturityAge（全盛期へ向かい線形成長）
//      - 全盛期 (MaturityAge..DeclineAge)   : ageFactor = 1.0
//      - 衰退期 (age > DeclineAge)           : ageFactor = DeclineRetentionPerYear^(age - DeclineAge)（年 3% 減）
//    実効       = round(base × levelMult × ageFactor)。base が 0 の項は 0 のまま（治癒/防御の 0 を 1 に
//                 しないための保護）。base>0 は最低 1 を保証する。
//
//  ★ 完全純粋・Godot 非依存（xUnit 検証可）。BattleManager / BattleResolver はユニットの戦闘ステを
//    本クラス経由で解決する（素の JobStats を直接読まない）。enum・識別子は ASCII（開発憲法①）。
// =============================================================================

using System;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.Core.Units;

/// <summary>素のジョブ値へレベル成長＋加齢成長を掛けて実効戦闘ステを解決する純粋関数。</summary>
public static class UnitStatProfile
{
    /// <summary>全盛期に到達する年齢。これ未満は修業期で線形に伸び、以降は全盛期で 1.0。</summary>
    public const int MaturityAge = 25;

    /// <summary>衰退期に入る年齢。これを超えると毎年 <see cref="DeclineRetentionPerYear"/> ずつステが落ちる。</summary>
    public const int DeclineAge = 45;

    /// <summary>衰退期の年あたり残存率（0.97＝年 3% 減・旧 TS 版と同式）。</summary>
    public const double DeclineRetentionPerYear = 0.97;

    /// <summary>レベル 1 段あたりの戦闘ステ上昇率（+25% / Lv）。</summary>
    public const double LevelGrowthPerLevel = 0.25;

    /// <summary>レベル係数（Lv1=1.0 / Lv2=1.25 / Lv3=1.5）。Level は Unit の上限でクランプ。</summary>
    public static double LevelMultiplier(int level)
        => 1.0 + (Math.Clamp(level, Unit.InitialLevel, Unit.MaxUnitLevel) - Unit.InitialLevel) * LevelGrowthPerLevel;

    /// <summary>
    /// 三段階の加齢係数。修業期(age&lt;MaturityAge)=age/MaturityAge の線形成長 / 全盛期(..DeclineAge)=1.0 /
    /// 衰退期(&gt;DeclineAge)=DeclineRetentionPerYear^(age-DeclineAge) の複利減（年 3%）。
    /// </summary>
    public static double AgeFactor(int age)
    {
        if (age <= 0) return 0.0;
        if (age < MaturityAge) return (double)age / MaturityAge;          // 修業期: 全盛期へ向かい成長
        if (age <= DeclineAge) return 1.0;                               // 全盛期
        return Math.Pow(DeclineRetentionPerYear, age - DeclineAge);     // 衰退期: 年 3% 減
    }

    /// <summary>レベル × 加齢の合成成長係数。</summary>
    public static double GrowthFactor(int level, int age) => LevelMultiplier(level) * AgeFactor(age);

    /// <summary>1 ステを成長係数でスケール。base が 0 の項は 0 のまま、base&gt;0 は最低 1 を保証。</summary>
    private static int Scale(int baseStat, double factor)
        => baseStat <= 0 ? baseStat : Math.Max(1, (int)Math.Round(baseStat * factor, MidpointRounding.AwayFromZero));

    /// <summary>素の JobStats へ (level, age) の成長係数を掛けた実効 JobStats を返す。</summary>
    public static JobStats EffectiveStats(JobStats baseStats, int level, int age)
    {
        ArgumentNullException.ThrowIfNull(baseStats);
        var f = GrowthFactor(level, age);
        return baseStats with
        {
            MaxHp            = Scale(baseStats.MaxHp, f),
            Speed            = Scale(baseStats.Speed, f),
            FrontAttack      = Scale(baseStats.FrontAttack, f),
            RearAttack       = Scale(baseStats.RearAttack, f),
            BattalionDefense = Scale(baseStats.BattalionDefense, f),
            SquadDefense     = Scale(baseStats.SquadDefense, f),
            InitiativeBuff   = Scale(baseStats.InitiativeBuff, f),
            TurnEndSquadHeal = Scale(baseStats.TurnEndSquadHeal, f),
        };
    }

    /// <summary>ユニットの実効戦闘ステを解決する（ジョブ未定義なら null）。</summary>
    public static JobStats? Resolve(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var def = JobMaster.Find(unit.Job);
        return def is null ? null : EffectiveStats(def.Stats, unit.Level, unit.Age);
    }
}
