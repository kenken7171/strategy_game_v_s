// =============================================================================
//  ChronicleKnights — EnemyScaler.cs
// -----------------------------------------------------------------------------
//  敵ステータスを「世代（年数）」と「レベル」から決定論的に算出する純粋ファクトリ。
//
//  ★ 移植元（正本）:
//    TypeScript 版 apps/api/src/routes/battle.ts の makeTrialEnemy(year, rng) と
//    config の ENEMY_SCALING。基準値・年成長率・HP 集約倍率・±15% 揺らぎを忠実に移植する。
//      baseHp    = BASE_HP(150)    + year * HP_GAIN_PER_YEAR(5)
//      baseAtk   = BASE_ATTACK(30) + year * ATTACK_GAIN_PER_YEAR(0.6)
//      baseSpeed = BASE_SPEED(100) + year * SPEED_GAIN_PER_YEAR(0.6)
//      jitter()  = 0.85 + rng() * 0.30                    // ±15% の個体差
//      hp = round(baseHp * 10 * jitter())                 // 旧10体合算HPを単体へ集約
//      atk / speed = round(base * jitter())
//
//  ★ レベル軸（C# 版で追加した決定論的拡張）:
//    上記の年成長に加え、敵レベルで全ステータスを単調に増幅する。
//      levelMultiplier = 1.0 + (level - 1) * PER_LEVEL_GAIN(0.5)
//        Lv1 → ×1.0（年成長のみ・移植元と完全一致）
//        Lv2 → ×1.5 / Lv3 → ×2.0 ...
//    既定 level=1 では移植元の式にそのまま縮退する。
//
//  ⚠ 決定論の絶対保証（設計憲法・要件②）:
//    揺らぎの乱数は **必ず引数で注入された System.Random** から取り出す。
//    Random.Shared やグローバル乱数を内部で呼ぶことを一切しない。よって同一の
//    (year, level, シード) を渡せば、どの環境・xUnit 上でも 100% 同一の結果になる。
//    乱数消費順序も HP → 攻撃 → 速度 に固定し、移植元と一致させる。
//
//  本ファイルは Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;

namespace ChronicleKnights.Core.Battle;

/// <summary>
/// 敵ステータスを世代・レベルから決定論的にスケールする純粋ファクトリ（静的）。
/// </summary>
public static class EnemyScaler
{
    // ─── スケーリング定数（SoT・TS ENEMY_SCALING の移植） ─────────────────

    /// <summary>基準 HP（世代 0 の素の値、集約・揺らぎ前）。</summary>
    public const int BaseHp = 150;

    /// <summary>基準攻撃力（世代 0 の素の値、揺らぎ前）。</summary>
    public const int BaseAttack = 30;

    /// <summary>基準速度（世代 0 の素の値、揺らぎ前）。</summary>
    public const int BaseSpeed = 100;

    /// <summary>HP の年成長率（1 世代あたりの基準 HP 増分）。</summary>
    public const double HpGainPerYear = 5.0;

    /// <summary>攻撃力の年成長率（1 世代あたりの基準攻撃力増分）。</summary>
    public const double AttackGainPerYear = 0.6;

    /// <summary>
    /// 速度の年成長率（1 世代あたりの基準速度増分）。
    /// instructions.md の絶対ルールにより 1.5 → 0.6 に緩和済み（移植元と一致）。
    /// </summary>
    public const double SpeedGainPerYear = 0.6;

    /// <summary>HP 集約倍率。旧 10 体集団の合算 HP を単体ボスへ集約する係数。</summary>
    public const int HpAggregationFactor = 10;

    /// <summary>個体差（±15%）の下限係数。jitter = JitterFloor + rng * JitterSpan。</summary>
    public const double JitterFloor = 0.85;

    /// <summary>個体差（±15%）の振れ幅。0.85〜1.15 の一様乱数になる。</summary>
    public const double JitterSpan = 0.30;

    /// <summary>レベル 1 段あたりの増幅率。Lv1=×1.0 / Lv2=×1.5 / Lv3=×2.0。</summary>
    public const double PerLevelGain = 0.5;

    /// <summary>最小レベル（これ未満は不正）。この値で levelMultiplier=1.0 になる基準。</summary>
    public const int BaseLevel = 1;

    /// <summary>各ステータスの下限（揺らぎ・丸めで 0 以下に落ちないよう保証）。</summary>
    public const int MinimumStatValue = 1;

    // ─── 純粋ファクトリ ─────────────────────────────────────────────────

    /// <summary>
    /// 既定レベル（<see cref="BaseLevel"/>）で試練の門の守護者をスケール生成する。
    /// この経路は移植元 makeTrialEnemy(year, rng) と完全に等価（年成長のみ）。
    /// </summary>
    /// <param name="generationYear">世代（年数、0 以上）。</param>
    /// <param name="rng">外部注入の乱数発生器（揺らぎ用、null 不可）。</param>
    public static EnemyState ScaleTrialGuardian(int generationYear, Random rng)
        => ScaleTrialGuardian(generationYear, BaseLevel, rng);

    /// <summary>
    /// 試練の門の守護者を「世代 × レベル」でスケール生成する。年成長で算出した基準値に
    /// レベル増幅を乗じ、最後に各ステータスへ ±15% の個体差を掛けて確定する。
    ///
    /// 乱数消費順序は HP → 攻撃 → 速度 に固定（決定論の保証）。同一の (year, level, シード)
    /// からは常に同一の <see cref="EnemyState"/> が得られる。
    /// </summary>
    /// <param name="generationYear">世代（年数、0 以上）。</param>
    /// <param name="level">敵レベル（<see cref="BaseLevel"/> 以上）。</param>
    /// <param name="rng">外部注入の乱数発生器（揺らぎ用、null 不可）。</param>
    /// <exception cref="ArgumentNullException">rng が null の場合。</exception>
    /// <exception cref="ArgumentOutOfRangeException">year が負、または level が BaseLevel 未満の場合。</exception>
    public static EnemyState ScaleTrialGuardian(int generationYear, int level, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (generationYear < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generationYear), generationYear, "generation year must be non-negative");
        }
        if (level < BaseLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level), level, $"level must be at least {BaseLevel}");
        }

        var levelMultiplier = 1.0 + (level - BaseLevel) * PerLevelGain;

        var scaledHp = (BaseHp + generationYear * HpGainPerYear)
                       * HpAggregationFactor * levelMultiplier;
        var scaledAttack = (BaseAttack + generationYear * AttackGainPerYear) * levelMultiplier;
        var scaledSpeed = (BaseSpeed + generationYear * SpeedGainPerYear) * levelMultiplier;

        // 乱数消費順序を HP → 攻撃 → 速度 に固定（移植元と一致・決定論の核心）。
        var hp = RollStat(scaledHp, rng);
        var attack = RollStat(scaledAttack, rng);
        var speed = RollStat(scaledSpeed, rng);

        return EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, hp, attack, speed);
    }

    // ─── 内部ヘルパー ───────────────────────────────────────────────────

    /// <summary>
    /// 基準値へ ±15% の個体差（jitter）を 1 回掛けて整数化し、下限でクランプする純粋プリミティブ。
    /// rng.NextDouble() をちょうど 1 回消費する（呼び出し順が決定論を決める核心）。丸めは JS の
    /// Math.round（正の .5 は切り上げ）に合わせ AwayFromZero を用いる。
    ///
    /// ★ 時代基準（EnemyScalingResolver.ResolveEnemyStats）へ実戦の個体差を乗せる合成でも、
    ///   個体差の SoT を二重定義しないよう本プリミティブを再利用する（Chronicle → Battle の一方向）。
    /// </summary>
    /// <param name="baseValue">個体差を乗せる前の基準値（時代基準ステータス等）。</param>
    /// <param name="rng">外部注入の乱数発生器（null 不可）。</param>
    /// <exception cref="ArgumentNullException">rng が null の場合。</exception>
    public static int ApplyJitter(double baseValue, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var jitter = JitterFloor + rng.NextDouble() * JitterSpan;
        var rounded = (int)Math.Round(baseValue * jitter, MidpointRounding.AwayFromZero);
        return Math.Max(MinimumStatValue, rounded);
    }

    /// <summary>±15% 個体差を 1 回掛けて整数化する内部経路（<see cref="ApplyJitter"/> へ委譲）。</summary>
    private static int RollStat(double baseValue, Random rng) => ApplyJitter(baseValue, rng);
}
