// =============================================================================
//  ChronicleKnights — EnemyScalingResolver.cs
// -----------------------------------------------------------------------------
//  「その年の敵はどれほど強いか」を、年数と章（Epoch）の難易度・環境補正だけから
//  整数演算で決定論的に弾き出す純粋ファクトリ。100 年史の難易度曲線（骨格）を司る数理層。
//
//  ★ EnemyScaler との関心の分離（重複ではなく相補）:
//    - EnemyScaler（Core.Battle）= 「実戦 1 体」の個体差。±15% の乱数ジッタと HP×10 集約を
//      担う。戦闘インスタンスごとに揺らぐ（要 System.Random）。
//    - EnemyScalingResolver（本ファイル）= 「その時代の基準強度」。乱数を 1 ミリも使わず、
//      章の難易度・環境補正を整数で重ねた決定論的ベースラインを弾く。100 年史の難易度曲線そのもの。
//    両者は将来「resolver で時代基準を出す → scaler で個体差を乗せる」と合成できるが、本ファイルは
//    決定論的な骨格だけに徹し、乱数・SoT には一切触れない（要件②・忠実な投影）。
//
//  ★ 整数演算のみ（要件②・Math.Floor 相当）:
//    倍率はすべて整数パーセントで保持し、`値 × パーセント / 100`（正の整数除算 = floor）だけで
//    スケールする。浮動小数・Math.Round・乱数を介在させない。よって同じ (year, template) からは
//    どの環境でも 100% 同一の ResolvedEnemyStats が返る。
//
//  ★ クランプ番兵（要件④）:
//    年は ChronicleTimelineConfig と同じく [FirstYear, TotalYears] にクランプしてから章を引き、
//    成長計算にもクランプ後の年を使う。よって 101 年目以降は終焉（最後の章）の値で頭打ちになり、
//    例外を投げない。
//
//  ★ 完全不変（設計憲法 ②）・日本語ハードコード禁止（①）。Godot 非依存の純粋 C#。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;

namespace ChronicleKnights.Core.Chronicle;

// ─── 敵テンプレート（スケール前の素の基準ステータス） ────────────────────────

/// <summary>
/// スケール前の敵の素の基準ステータスを写し取った完全不変テンプレート。年成長・章補正を
/// 掛ける前の「世代 0・等倍」の値で、EnemyScalingResolver がこれを起点に時代強度を弾く。
/// 表示名は NameKey（ASCII）で運び、日本語テキストは持たない（設計憲法 ①）。
/// </summary>
public sealed record EnemyTemplate
{
    /// <summary>この敵の原型。</summary>
    public required EnemyArchetype Archetype { get; init; }

    /// <summary>基準 HP（世代 0・等倍、1 以上）。</summary>
    public required int BaseHp { get; init; }

    /// <summary>基準攻撃力（世代 0・等倍、0 以上）。</summary>
    public required int BaseAttack { get; init; }

    /// <summary>基準防御力（世代 0・等倍、0 以上）。</summary>
    public required int BaseDefense { get; init; }

    /// <summary>基準速度（世代 0・等倍、0 以上）。</summary>
    public required int BaseSpeed { get; init; }

    /// <summary>表示名の localization キー（ASCII・例: "enemy-eternal-sovereign"）。</summary>
    public required string NameKey { get; init; }
}

// ─── スケール後の敵ステータス（その年・その章での確定強度） ──────────────────

/// <summary>
/// 年数・章補正を掛けて確定した敵ステータスを写し取る完全不変レコード。どの年・どの章で
/// 弾かれたかの文脈（Year/Epoch）も併せ持ち、難易度曲線の検証・UI 表示の根拠になる。
/// </summary>
public sealed record ResolvedEnemyStats
{
    /// <summary>この敵の原型。</summary>
    public required EnemyArchetype Archetype { get; init; }

    /// <summary>スケールに用いた（クランプ後の）年。</summary>
    public required int Year { get; init; }

    /// <summary>その年が属する章。</summary>
    public required EpochId Epoch { get; init; }

    /// <summary>スケール後 HP（1 以上）。</summary>
    public required int Hp { get; init; }

    /// <summary>スケール後 攻撃力（1 以上）。</summary>
    public required int Attack { get; init; }

    /// <summary>スケール後 防御力（1 以上）。</summary>
    public required int Defense { get; init; }

    /// <summary>スケール後 速度（1 以上）。</summary>
    public required int Speed { get; init; }
}

// ─── 決定論的スケーリング純粋ファクトリ ──────────────────────────────────────

/// <summary>
/// 年数と敵テンプレートから、章の難易度・環境補正を整数で重ねた敵ステータスを決定論的に
/// 弾く純粋ファクトリ（静的）。乱数・SoT 非依存。
/// </summary>
public static class EnemyScalingResolver
{
    // ─── 年成長率（1 年あたりの素の増分・整数のみ） ──────────────────────

    /// <summary>HP の年成長率（1 年あたりの基準 HP 増分）。</summary>
    public const int HpGainPerYear = 5;

    /// <summary>攻撃力の年成長率（1 年あたりの基準攻撃力増分）。</summary>
    public const int AttackGainPerYear = 1;

    /// <summary>防御力の年成長率（1 年あたりの基準防御力増分）。</summary>
    public const int DefenseGainPerYear = 1;

    /// <summary>速度の年成長率（1 年あたりの基準速度増分）。</summary>
    public const int SpeedGainPerYear = 1;

    /// <summary>各ステータスの下限（補正・整数除算で 0 以下に落ちないよう保証）。</summary>
    public const int MinimumStatValue = 1;

    /// <summary>整数パーセント除算の分母（100% = 等倍）。</summary>
    private const int PercentDenominator = 100;

    // ─── 純粋ファクトリ ─────────────────────────────────────────────────

    /// <summary>
    /// 年数と敵テンプレートから、その年の章の難易度・環境補正を整数で重ねた敵ステータスを
    /// 決定論的に弾く。年は [FirstYear, TotalYears] にクランプされ、範囲外でも例外を投げない。
    ///
    /// 各ステータスの算出（整数のみ・floor 相当）:
    ///   grown          = base + clampedYear × gainPerYear
    ///   afterDifficulty = grown × 章.DifficultyScalePercent / 100
    ///   afterEnvironment = afterDifficulty × 章.EnvironmentModifierPercent / 100
    ///   結果            = Max(MinimumStatValue, afterEnvironment)
    /// </summary>
    /// <param name="year">現在年（範囲外でもクランプされ安全）。</param>
    /// <param name="template">スケール前の素の基準ステータス（null 不可）。</param>
    /// <returns>その年・その章での確定ステータス。</returns>
    /// <exception cref="ArgumentNullException">template が null の場合。</exception>
    public static ResolvedEnemyStats ResolveEnemyStats(int year, EnemyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var clampedYear = Math.Clamp(
            year, ChronicleTimelineConfig.FirstYear, ChronicleTimelineConfig.TotalYears);
        var epoch = ChronicleTimelineConfig.EpochForYear(clampedYear);

        return new ResolvedEnemyStats
        {
            Archetype = template.Archetype,
            Year      = clampedYear,
            Epoch     = epoch.Id,
            Hp        = ScaleStat(template.BaseHp,      HpGainPerYear,      clampedYear, epoch),
            Attack    = ScaleStat(template.BaseAttack,  AttackGainPerYear,  clampedYear, epoch),
            Defense   = ScaleStat(template.BaseDefense, DefenseGainPerYear, clampedYear, epoch),
            Speed     = ScaleStat(template.BaseSpeed,   SpeedGainPerYear,   clampedYear, epoch),
        };
    }

    /// <summary>
    /// 1 ステータスを「年成長 → 難易度倍率 → 環境補正」の順で整数スケールし、下限でクランプする。
    /// すべて整数演算（`× パーセント / 100` は正の整数除算 = floor）で乱数を介在させない。
    /// </summary>
    private static int ScaleStat(int baseStat, int gainPerYear, int clampedYear, Epoch epoch)
    {
        var grown = baseStat + clampedYear * gainPerYear;
        var afterDifficulty = grown * epoch.DifficultyScalePercent / PercentDenominator;
        var afterEnvironment = afterDifficulty * epoch.EnvironmentModifierPercent / PercentDenominator;
        return Math.Max(MinimumStatValue, afterEnvironment);
    }

    // ─── 実戦合成（時代基準 × 個体差ジッタ → 戦闘に立つ 1 体） ────────────────

    /// <summary>
    /// 時代基準ステータス（<see cref="ResolveEnemyStats"/>）へ実戦の個体差ジッタ（±15%）と HP 集約を
    /// 乗せ、「その年・その章で戦闘に立つ 1 体」を合成する純粋ファクトリ。乱数は外部注入の
    /// <see cref="Random"/> から取り出すため、同一 (year, template, シード) からは 100% 同一の
    /// <see cref="EnemyState"/> が返る（決定論）。個体差の SoT は <see cref="EnemyScaler.ApplyJitter"/>
    /// を再利用し二重定義しない（Chronicle → Battle の一方向結合）。
    ///
    /// 合成順序（乱数消費を HP → 攻撃 → 速度 に固定＝EnemyScaler.ScaleTrialGuardian と同型）:
    ///   era    = ResolveEnemyStats(year, template)                       // 章の難易度・環境を畳んだ時代基準
    ///   hp     = ApplyJitter(era.Hp × HpAggregationFactor)               // 旧 10 体合算の集約＋個体差
    ///   attack = ApplyJitter(era.Attack)
    ///   speed  = ApplyJitter(era.Speed)
    ///
    /// 防御（era.Defense）は <see cref="EnemyState"/> が戦闘通貨として持たないため本合成では畳まず、
    /// 時代基準の DEF はマスターデータとして resolver 側に留める（将来の戦闘式拡張に備える設計余地）。
    /// </summary>
    /// <param name="year">現在年（範囲外でもクランプされ安全）。</param>
    /// <param name="template">スケール前の素テンプレート（null 不可）。</param>
    /// <param name="rng">個体差ジッタ用の外部注入乱数（null 不可）。</param>
    /// <returns>満タンの実戦用敵スナップショット。</returns>
    /// <exception cref="ArgumentNullException">template または rng が null の場合。</exception>
    public static EnemyState ComposeBattleEnemy(int year, EnemyTemplate template, Random rng)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rng);

        var era = ResolveEnemyStats(year, template);

        // 乱数消費順序を HP → 攻撃 → 速度 に固定（決定論の核心・既存スケーラと同型）。
        var hp = EnemyScaler.ApplyJitter(
            (double)era.Hp * EnemyScaler.HpAggregationFactor, rng);
        var attack = EnemyScaler.ApplyJitter(era.Attack, rng);
        var speed = EnemyScaler.ApplyJitter(era.Speed, rng);

        return EnemyState.CreateAtFullHealth(era.Archetype, hp, attack, speed);
    }

    /// <summary>
    /// 原型から素テンプレートを引いて <see cref="ComposeBattleEnemy(int, EnemyTemplate, Random)"/> する
    /// 簡易経路。未登録の原型は通常敵テンプレートへ安全にフォールバックする。
    /// </summary>
    public static EnemyState ComposeBattleEnemy(int year, EnemyArchetype archetype, Random rng)
        => ComposeBattleEnemy(year, TemplateFor(archetype), rng);

    // ─── 敵テンプレート・カタログ（マスターデータ・スケール前の素値） ─────

    /// <summary>通常戦の標準敵（試練の門の守護者）の素テンプレート。</summary>
    public static readonly EnemyTemplate TrialGuardianTemplate = new()
    {
        Archetype   = EnemyArchetype.TrialGuardian,
        BaseHp      = 150,
        BaseAttack  = 30,
        BaseDefense = 10,
        BaseSpeed   = 100,
        NameKey     = "enemy-trial-guardian",
    };

    /// <summary>黎明の章ボス（DawnWarden）の素テンプレート。</summary>
    public static readonly EnemyTemplate DawnWardenTemplate = new()
    {
        Archetype   = EnemyArchetype.DawnWarden,
        BaseHp      = 200,
        BaseAttack  = 40,
        BaseDefense = 15,
        BaseSpeed   = 90,
        NameKey     = "enemy-dawn-warden",
    };

    /// <summary>激動の章ボス（UpheavalConqueror）の素テンプレート。</summary>
    public static readonly EnemyTemplate UpheavalConquerorTemplate = new()
    {
        Archetype   = EnemyArchetype.UpheavalConqueror,
        BaseHp      = 260,
        BaseAttack  = 55,
        BaseDefense = 20,
        BaseSpeed   = 110,
        NameKey     = "enemy-upheaval-conqueror",
    };

    /// <summary>斜陽の章ボス（DeclineTyrant）の素テンプレート。</summary>
    public static readonly EnemyTemplate DeclineTyrantTemplate = new()
    {
        Archetype   = EnemyArchetype.DeclineTyrant,
        BaseHp      = 320,
        BaseAttack  = 70,
        BaseDefense = 28,
        BaseSpeed   = 120,
        NameKey     = "enemy-decline-tyrant",
    };

    /// <summary>終焉の章ボス（EternalSovereign）の素テンプレート。</summary>
    public static readonly EnemyTemplate EternalSovereignTemplate = new()
    {
        Archetype   = EnemyArchetype.EternalSovereign,
        BaseHp      = 420,
        BaseAttack  = 90,
        BaseDefense = 36,
        BaseSpeed   = 140,
        NameKey     = "enemy-eternal-sovereign",
    };

    /// <summary>原型 → 素テンプレートの引き当て表（章ボスの素値を年から解決する補助）。</summary>
    public static readonly ImmutableDictionary<EnemyArchetype, EnemyTemplate> TemplatesByArchetype =
        ImmutableDictionary.CreateRange(new[]
        {
            new System.Collections.Generic.KeyValuePair<EnemyArchetype, EnemyTemplate>(
                EnemyArchetype.TrialGuardian, TrialGuardianTemplate),
            new System.Collections.Generic.KeyValuePair<EnemyArchetype, EnemyTemplate>(
                EnemyArchetype.DawnWarden, DawnWardenTemplate),
            new System.Collections.Generic.KeyValuePair<EnemyArchetype, EnemyTemplate>(
                EnemyArchetype.UpheavalConqueror, UpheavalConquerorTemplate),
            new System.Collections.Generic.KeyValuePair<EnemyArchetype, EnemyTemplate>(
                EnemyArchetype.DeclineTyrant, DeclineTyrantTemplate),
            new System.Collections.Generic.KeyValuePair<EnemyArchetype, EnemyTemplate>(
                EnemyArchetype.EternalSovereign, EternalSovereignTemplate),
        });

    /// <summary>
    /// 原型に対応する素テンプレートを返す。未登録の原型は通常敵（試練の門の守護者）の
    /// テンプレートへ安全にフォールバックする（例外を投げない番兵）。
    /// </summary>
    public static EnemyTemplate TemplateFor(EnemyArchetype archetype)
        => TemplatesByArchetype.TryGetValue(archetype, out var template)
            ? template
            : TrialGuardianTemplate;
}
