// =============================================================================
//  ChronicleKnights — ChronicleTimelineConfig.cs
// -----------------------------------------------------------------------------
//  100 年の叙事詩を貫く「歴史の骨格」を写し取った完全不変のマスターデータ。
//  100 年を 4 つの章（Epoch）に分割し、各章の難易度倍率・出現敵種別・環境補正・
//  章ボス（EpochBoss）の出現年を静的に保持する単一の真実（SoT）である。
//
//  ★ 4 つの章（黎明 → 激動 → 斜陽 → 終焉）:
//      黎明 Dawn       1- 25 年   難易度 100% / 環境 100%   章ボス: DawnWarden
//      激動 Upheaval  26- 50 年   難易度 140% / 環境 110%   章ボス: UpheavalConqueror
//      斜陽 Decline   51- 75 年   難易度 150% / 環境 110%   章ボス: DeclineTyrant（黄金均衡へ調律済）
//      終焉 Twilight  76-100 年   難易度 170% / 環境 110%   章ボス: EternalSovereign（黄金均衡へ調律済）
//    各章は最終年（25/50/75/100）に章ボスが君臨する。難易度・環境はいずれも整数パーセントで
//    保持し、敵スケーリング（EnemyScalingResolver）が整数演算だけで忠実に投影できるようにする。
//
//  ★ クランプ番兵（要件④・例外を投げない歴史の縁）:
//    EpochForYear は年を [FirstYear, TotalYears] に必ずクランプしてから章を引く。よって
//    101 年目以降や 0 年以下を渡しても、最後（または最初）の章へ安全に丸められ、
//    IndexOutOfRangeException を構造的に投げない。添字アクセスは一切しない。
//
//  ★ 章ボス前兆の窓口（要件③）:
//    BuildOmenScheduleForYear が「今が何年か」だけから、次に到来する章ボスまでの残り年数を
//    決定論的に弾き、Core.Battle の EpochBossOmenSchedule（運命の帯へ重ねる前兆の窓口仕様）を
//    組み立てる。乱数・SoT には一切依存しない（同じ年からは 100% 同じスケジュール）。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）・完全不変（②）:
//    章名は NameKey（ASCII・localization 解決キー）として保持し、日本語テキストは持たない。
//    Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;

namespace ChronicleKnights.Core.Chronicle;

// ─── 章の識別子 ──────────────────────────────────────────────────────────────

/// <summary>
/// 100 年史を分割する 4 つの章（Epoch）。表示名は localization キー経由で解決する想定で、
/// 列挙体には日本語・絵文字を一切含めない（設計憲法 ①）。
/// </summary>
public enum EpochId
{
    /// <summary>黎明（1-25 年）。最初の試練の時代。</summary>
    Dawn,

    /// <summary>激動（26-50 年）。乱世の時代。</summary>
    Upheaval,

    /// <summary>斜陽（51-75 年）。衰亡の時代。</summary>
    Decline,

    /// <summary>終焉（76-100 年）。100 年史を締めくくる時代。</summary>
    Twilight,
}

// ─── 1 つの章（難易度・出現敵・環境補正・章ボス） ────────────────────────────

/// <summary>
/// 100 年史の 1 つの章を写し取った完全不変レコード。年範囲・難易度倍率（整数パーセント）・
/// 環境補正（整数パーセント）・通常出現敵原型・章ボス原型を保持する。表示名は NameKey
/// （ASCII）で運び、日本語テキストは持たない（設計憲法 ①）。
/// </summary>
public sealed record Epoch
{
    /// <summary>章の識別子。</summary>
    public required EpochId Id { get; init; }

    /// <summary>章の開始年（含む・1 始まり）。</summary>
    public required int StartYear { get; init; }

    /// <summary>章の終了年（含む）。章ボスはこの年に君臨する。</summary>
    public required int EndYear { get; init; }

    /// <summary>難易度倍率（整数パーセント。100 = 等倍、140 = 1.4 倍）。</summary>
    public required int DifficultyScalePercent { get; init; }

    /// <summary>環境補正倍率（整数パーセント。難易度に重ねて掛かる時代の過酷さ）。</summary>
    public required int EnvironmentModifierPercent { get; init; }

    /// <summary>この章で通常出現する敵の原型。</summary>
    public required EnemyArchetype RegularArchetype { get; init; }

    /// <summary>この章の最終年に君臨する章ボスの原型。</summary>
    public required EnemyArchetype BossArchetype { get; init; }

    /// <summary>章名の表示 localization キー（ASCII・例: "epoch-dawn"）。</summary>
    public required string NameKey { get; init; }

    // ─── 派生クエリ（読み取り専用） ──────────────────────────────────────

    /// <summary>章ボスが君臨する年（= 章の終了年）。</summary>
    public int BossYear => EndYear;

    /// <summary>指定年がこの章の範囲（StartYear..EndYear）に含まれるか。</summary>
    public bool ContainsYear(int year) => year >= StartYear && year <= EndYear;

    /// <summary>指定年がこの章の章ボス出現年か。</summary>
    public bool IsBossYear(int year) => year == BossYear;
}

// ─── 100 年史マスターデータ（静的 SoT） ───────────────────────────────────────

/// <summary>
/// 100 年史のタイムライン骨格を保持する静的マスターデータ。4 章の不変配列と、年から章を
/// 安全に引く（クランプ番兵つき）純粋クエリ、章ボス接近前兆スケジュールの決定論的ビルダを提供する。
/// </summary>
public static class ChronicleTimelineConfig
{
    // ─── 歴史の縁（定数） ────────────────────────────────────────────────

    /// <summary>叙事詩の開始年（1 始まり）。</summary>
    public const int FirstYear = 1;

    /// <summary>叙事詩の総年数（100 年史）。最終年でもある。</summary>
    public const int TotalYears = 100;

    /// <summary>1 つの章の年数（100 / 4 = 25 年）。</summary>
    public const int YearsPerEpoch = 25;

    /// <summary>
    /// 章ボス到来の何ターン前から前兆（時間の矢）を出し始めるか（先行告知の長さ）。
    /// BuildOmenScheduleForYear がこの値で EpochBossOmenSchedule.ForewarnLeadTurns を満たす。
    /// </summary>
    public const int EpochBossForewarnLeadTurns = 3;

    /// <summary>章ボス前兆スキル名の表示 localization キー（ASCII）。</summary>
    public const string EpochBossOmenSkillNameKey = "epoch-boss-omen-approach";

    // ─── 4 章の不変マスターデータ ────────────────────────────────────────

    /// <summary>
    /// 100 年史を構成する 4 つの章（黎明 → 激動 → 斜陽 → 終焉）。年範囲は [1,100] を
    /// 隙間なく分割し、BossYear（25/50/75/100）昇順に並ぶ（前兆ビルダがこの順序に依存する）。
    /// </summary>
    public static readonly ImmutableArray<Epoch> Epochs = ImmutableArray.Create(
        new Epoch
        {
            Id                         = EpochId.Dawn,
            StartYear                  = 1,
            EndYear                    = 25,
            DifficultyScalePercent     = 100,
            EnvironmentModifierPercent = 100,
            RegularArchetype           = EnemyArchetype.TrialGuardian,
            BossArchetype              = EnemyArchetype.DawnWarden,
            NameKey                    = "epoch-dawn",
        },
        new Epoch
        {
            Id                         = EpochId.Upheaval,
            StartYear                  = 26,
            EndYear                    = 50,
            DifficultyScalePercent     = 140,
            EnvironmentModifierPercent = 110,
            RegularArchetype           = EnemyArchetype.TrialGuardian,
            BossArchetype              = EnemyArchetype.UpheavalConqueror,
            NameKey                    = "epoch-upheaval",
        },
        new Epoch
        {
            Id                         = EpochId.Decline,
            StartYear                  = 51,
            EndYear                    = 75,
            DifficultyScalePercent     = 150,
            EnvironmentModifierPercent = 110,
            RegularArchetype           = EnemyArchetype.TrialGuardian,
            BossArchetype              = EnemyArchetype.DeclineTyrant,
            NameKey                    = "epoch-decline",
        },
        new Epoch
        {
            Id                         = EpochId.Twilight,
            StartYear                  = 76,
            EndYear                    = 100,
            DifficultyScalePercent     = 170,
            EnvironmentModifierPercent = 110,
            RegularArchetype           = EnemyArchetype.TrialGuardian,
            BossArchetype              = EnemyArchetype.EternalSovereign,
            NameKey                    = "epoch-twilight",
        });

    // ─── 年 → 章（クランプ番兵つきの安全な引き当て） ─────────────────────

    /// <summary>
    /// 指定年が属する章を返す。年は必ず [<see cref="FirstYear"/>, <see cref="TotalYears"/>] に
    /// クランプしてから引くため、101 年目以降は終焉（最後の章）へ、0 年以下は黎明（最初の章）へ
    /// 安全に丸められ、例外を投げない（要件④）。
    /// </summary>
    /// <param name="year">問い合わせ年（範囲外でも安全にクランプされる）。</param>
    public static Epoch EpochForYear(int year)
    {
        var clampedYear = Math.Clamp(year, FirstYear, TotalYears);

        foreach (var epoch in Epochs)
        {
            if (epoch.ContainsYear(clampedYear))
            {
                return epoch;
            }
        }

        // 章は [1,100] を隙間なく覆うため通常ここへは来ないが、万一の保険として最後の章を返す
        // （添字アクセスを避け IndexOutOfRange を構造的に封じる二重の番兵）。
        return Epochs[Epochs.Length - 1];
    }

    /// <summary>指定年が、いずれかの章の章ボス出現年（25/50/75/100）か。</summary>
    public static bool IsEpochBossYear(int year)
    {
        foreach (var epoch in Epochs)
        {
            if (epoch.IsBossYear(year))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// その年の戦闘で出現すべき敵の原型を決定論的に選ぶ。章ボス出現年（25/50/75/100）はその章の
    /// 章ボス、それ以外の年は章の通常敵（試練の門の守護者）を返す。年は [FirstYear, TotalYears] に
    /// クランプされるため、101 年目以降は終焉の最終年（章ボス出現年）へ丸められ EternalSovereign を、
    /// 0 年以下は黎明の通常年へ丸められ TrialGuardian を返す（例外を投げない）。
    ///
    /// 戦闘開始ファクトリ（ChronicleGlobal.CreateCurrentYearEnemy）が「今年は誰と戦うか」を引く正本。
    /// </summary>
    /// <param name="year">現在年（範囲外でもクランプされ安全）。</param>
    public static EnemyArchetype BattleArchetypeForYear(int year)
    {
        var clampedYear = Math.Clamp(year, FirstYear, TotalYears);
        var epoch = EpochForYear(clampedYear);
        return epoch.IsBossYear(clampedYear) ? epoch.BossArchetype : epoch.RegularArchetype;
    }

    // ─── 章ボス接近前兆スケジュールの決定論的ビルダ（要件③の窓口） ───────

    /// <summary>
    /// 「今が <paramref name="currentYear"/> 年」という暦だけから、次に到来する章ボスまでの残り年数を
    /// 決定論的に弾き、運命の帯へ重ねる前兆スケジュール（<see cref="EpochBossOmenSchedule"/>）を組み立てる。
    /// 乱数・SoT には一切依存しない（同じ年からは 100% 同じスケジュール）。
    ///
    /// 規律:
    ///   - 次の章ボス = BossYear が currentYear より大きい最初の章（昇順に走査）。
    ///   - TurnsUntilArrival = nextBossYear − currentYear（1 年 ≒ 帯 1 ターンの決定論的写像）。
    ///   - これ以上将来の章ボスが無い（currentYear が最終ボス年 100 以上）場合は
    ///     <see cref="EpochBossOmenSchedule.None"/>（前兆なし）を返す。
    ///   - 0 年以下など範囲外でも例外を投げず、最初の章ボスまでの残り年数として安全に弾く。
    /// </summary>
    /// <param name="currentYear">現在年（範囲外でも安全に扱う）。</param>
    /// <returns>次の章ボスへの前兆スケジュール（接近する章ボスが無ければ None）。</returns>
    public static EpochBossOmenSchedule BuildOmenScheduleForYear(int currentYear)
    {
        foreach (var epoch in Epochs)
        {
            if (epoch.BossYear > currentYear)
            {
                return new EpochBossOmenSchedule
                {
                    BossApproaching   = true,
                    TurnsUntilArrival = epoch.BossYear - currentYear,
                    ForewarnLeadTurns = EpochBossForewarnLeadTurns,
                    BossArchetype     = epoch.BossArchetype,
                    EpochNameKey      = epoch.NameKey,
                    OmenSkillNameKey  = EpochBossOmenSkillNameKey,
                };
            }
        }

        // 最終ボス年（100）以降は、これ以上将来の章ボスは存在しない。
        return EpochBossOmenSchedule.None;
    }
}
