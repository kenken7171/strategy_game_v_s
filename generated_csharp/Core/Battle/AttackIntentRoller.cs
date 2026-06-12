// =============================================================================
//  ChronicleKnights — AttackIntentRoller.cs
// -----------------------------------------------------------------------------
//  敵の次ターン攻撃予告（AttackIntent）を、外部注入の System.Random と敵の現在
//  攻撃力から決定論的に抽選する純粋ファクトリ。
//
//  ★ 移植元（正本）:
//    TypeScript 版 BattleSimulator.generateAttackPattern() の思想を忠実に移植する。
//      r = rng()                              // パターン抽選
//        r < 1/3 → SingleStrike（1 行・倍率 2.0）
//        r < 2/3 → Pincer      （2 行・倍率 1.4）
//        else    → TotalAssault（3 行・倍率 0.9）
//      jitter = 0.85 + rng() * 0.30           // ±15% の個体差（敵ステ揺らぎと同式）
//      damage = max(1, round(baseAttack * multiplier * jitter))
//
//  ⚠ 決定論の絶対保証（設計憲法・要件②）:
//    確率要素に使う乱数は **必ず引数で注入された System.Random** から取り出す。
//    Random.Shared 等のグローバル乱数を内部で呼ぶことは一切しない。乱数の消費順序を
//    「パターン抽選 → （行抽選）→ ジッタ」に固定し、同一の (敵攻撃力, シード) からは
//    常に同一の AttackIntent が得られる。±15% の係数は敵ステータススケールと同一の
//    SoT（EnemyScaler.JitterFloor / JitterSpan）を再利用し、二重定義を避ける。
//
//  本ファイルは Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.Core.Battle;

/// <summary>
/// 敵の次ターン攻撃予告を世代・敵攻撃力・乱数から決定論的に抽選する純粋ファクトリ（静的）。
/// </summary>
public static class AttackIntentRoller
{
    // ─── 威力倍率（SoT・TS generateAttackPattern の移植） ─────────────────

    /// <summary>単一分隊強襲の威力倍率（1 行集中＝高ダメージ）。</summary>
    public const double SingleStrikeMultiplier = 2.0;

    /// <summary>複数分隊挟撃の威力倍率（2 行挟撃＝中ダメージ）。</summary>
    public const double PincerMultiplier = 1.4;

    /// <summary>全大隊総攻撃の威力倍率（3 行薙ぎ＝低ダメージ）。</summary>
    public const double TotalAssaultMultiplier = 0.9;

    /// <summary>各攻撃の最小威力（倍率・ジッタ・丸めで 0 以下に落ちない保証）。</summary>
    public const int MinimumDamagePerUnit = 1;

    /// <summary>
    /// 先読み（運命の帯）の最大ターン数の番兵。Forecast が受け取る lookaheadTurns は
    /// この上限でクランプされ、過大値による過剰確保・長時間ループを構造的に封じる。
    /// 数ターン先の戦術判断（回転作戦の読み合い）が目的のため、現実的な深さに留める。
    /// </summary>
    public const int MaxForecastTurns = 16;

    // ─── スキル名キー（ASCII・localization 解決用） ──────────────────────

    /// <summary>単一分隊強襲の表示名 localization キー。</summary>
    public const string SingleStrikeSkillNameKey = "enemy-skill-single-strike";

    /// <summary>複数分隊挟撃の表示名 localization キー。</summary>
    public const string PincerSkillNameKey = "enemy-skill-pincer";

    /// <summary>全大隊総攻撃の表示名 localization キー。</summary>
    public const string TotalAssaultSkillNameKey = "enemy-skill-total-assault";

    // ─── パターン抽選境界（[0,1) 一様乱数を 3 等分） ─────────────────────

    private const double SingleStrikeThreshold = 1.0 / 3.0;
    private const double PincerThreshold = 2.0 / 3.0;

    // ─── 挟撃の対象ペア（3C2 = 3 通り。いずれも正準順） ──────────────────

    private static readonly ImmutableArray<ImmutableArray<SquadRow>> PincerPairs =
        ImmutableArray.Create(
            ImmutableArray.Create(SquadRow.Front, SquadRow.RearLeft),
            ImmutableArray.Create(SquadRow.Front, SquadRow.RearRight),
            ImmutableArray.Create(SquadRow.RearLeft, SquadRow.RearRight));

    /// <summary>全行（正準順）。SingleStrike の抽選母集団・TotalAssault の対象。</summary>
    private static readonly ImmutableArray<SquadRow> AllRows = FormationBoard.RowOrder;

    // ─── 純粋ファクトリ ─────────────────────────────────────────────────

    /// <summary>
    /// 敵の現在攻撃力を基準に、次ターンの攻撃予告を決定論的に抽選する。
    /// 乱数消費順序は「パターン抽選 → （行抽選）→ ジッタ」に固定する。
    /// </summary>
    /// <param name="enemy">予告主の敵スナップショット（攻撃力を威力の基値に使う）。</param>
    /// <param name="rng">外部注入の乱数発生器（抽選・ジッタ用、null 不可）。</param>
    /// <exception cref="ArgumentNullException">enemy または rng が null の場合。</exception>
    public static AttackIntent Roll(EnemyState enemy, Random rng)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(rng);

        var baseAttack = enemy.Attack;
        var patternRoll = rng.NextDouble();

        if (patternRoll < SingleStrikeThreshold)
        {
            // ── 単一分隊強襲: 1 行を抽選し、倍率 2.0 で集中 ──────────────
            var target = PickRow(AllRows, rng);
            return new AttackIntent
            {
                Kind = AttackPatternKind.SingleStrike,
                SkillNameKey = SingleStrikeSkillNameKey,
                TargetRows = ImmutableArray.Create(target),
                DamagePerUnit = RollDamage(baseAttack, SingleStrikeMultiplier, rng),
            };
        }

        if (patternRoll < PincerThreshold)
        {
            // ── 複数分隊挟撃: 3 ペアから 1 つを抽選し、倍率 1.4 ────────────
            var pair = PickPair(rng);
            return new AttackIntent
            {
                Kind = AttackPatternKind.Pincer,
                SkillNameKey = PincerSkillNameKey,
                TargetRows = pair,
                DamagePerUnit = RollDamage(baseAttack, PincerMultiplier, rng),
            };
        }

        // ── 全大隊総攻撃: 全 3 行へ、倍率 0.9（行抽選なし） ───────────────
        return new AttackIntent
        {
            Kind = AttackPatternKind.TotalAssault,
            SkillNameKey = TotalAssaultSkillNameKey,
            TargetRows = AllRows,
            DamagePerUnit = RollDamage(baseAttack, TotalAssaultMultiplier, rng),
        };
    }

    // ─── N ターン先読み（運命の帯・横軸の予言エンジン） ──────────────────

    /// <summary>
    /// 敵がこの先 <paramref name="lookaheadTurns"/> ターンに放つ予定の攻撃予告を、
    /// 受け取ったシードから決定論的に先回りして「運命の帯」（ルックアヘッド配列）へ
    /// 組み上げて返す純粋関数。戻り値 index 0 が T+1（次ターン）、index 1 が T+2 … と続く。
    ///
    /// ★ SoT 不可侵（設計憲法 ②・③ / 副作用なく未来を覗き見る）:
    ///   <paramref name="currentSeed"/> の「コピー」をローカルの <see cref="Random"/> として
    ///   立て、それだけをターン数分だけ確定的に前進させる。外部状態（実戦の乱数器・敵・盤面）を
    ///   1 ミリも変更しない。<see cref="Roll"/> は <paramref name="enemy"/> を読むだけの純粋関数
    ///   ゆえ、何度呼んでも同じ局面からは同じ帯が返る（完全な冪等・決定論）。
    ///
    /// ★ 実戦との整合（faithful projection）:
    ///   実戦 <see cref="BattleResolver.ResolveTurn"/> は 1 本の乱数器を毎ターン Roll へ通して
    ///   次手を確定する。本関数も同型に、1 本のローカル乱数器を Roll へ連続通電して帯を組む。
    ///   各 Roll は局面に応じて乱数を可変個（強襲/挟撃=3・総攻撃=2）消費するため、帯の各要素は
    ///   前要素の消費量だけ前進した「次の予言」になる。
    ///
    /// ★ ガード番兵（要件③・暴走の構造的封じ込め）:
    ///   <paramref name="lookaheadTurns"/> が 0 以下なら空帯を返す。過大値は
    ///   <see cref="MaxForecastTurns"/> でクランプして過剰確保・長時間ループを防ぐ。ループは
    ///   固定回数の前進のみで添字アクセスを行わないため、無限ループ・範囲外参照は原理的に生じない。
    /// </summary>
    /// <param name="enemy">予告主の敵スナップショット（攻撃力を威力の基値に使う、null 不可）。</param>
    /// <param name="currentSeed">先読みの起点シード（このコピーをローカルで前進させる）。</param>
    /// <param name="lookaheadTurns">何ターン先まで覗くか（0 以下は空・上限はクランプ）。</param>
    /// <returns>T+1 から順に並ぶ攻撃予告の不変な帯（要素数は実効ターン数）。</returns>
    /// <exception cref="ArgumentNullException">enemy が null の場合。</exception>
    public static IReadOnlyList<AttackIntent> Forecast(
        EnemyState enemy, uint currentSeed, int lookaheadTurns)
    {
        ArgumentNullException.ThrowIfNull(enemy);

        // 番兵①: 非正のルックアヘッドは「覗く未来なし」として空帯を返す（例外を投げない）。
        if (lookaheadTurns <= 0)
        {
            return ImmutableArray<AttackIntent>.Empty;
        }

        // 番兵②: 過大値を上限でクランプ（過剰確保・長時間ループの構造的封じ込め）。
        var effectiveTurns = Math.Min(lookaheadTurns, MaxForecastTurns);

        // 受け取ったシードの「コピー」をローカル乱数器として確定的に前進させる。
        // uint シードを Random の種(int)へ unchecked で写す（情報を落とさない決定論的写像）。
        var forecastRng = new Random(unchecked((int)currentSeed));

        var builder = ImmutableArray.CreateBuilder<AttackIntent>(effectiveTurns);
        for (int turn = 0; turn < effectiveTurns; turn++)
        {
            // Roll は enemy を変更しない純粋関数。前進するのは forecastRng だけ（SoT は不可侵）。
            builder.Add(Roll(enemy, forecastRng));
        }
        return builder.ToImmutable();
    }

    // ─── 内部ヘルパー ───────────────────────────────────────────────────

    /// <summary>
    /// 基準攻撃力にパターン倍率と ±15% ジッタを掛けて整数化し、下限でクランプする。
    /// rng.NextDouble() をちょうど 1 回消費する（ジッタ用）。丸めは JS の Math.round
    /// （正の .5 は切り上げ）に合わせ AwayFromZero を用い、移植元と一致させる。
    /// </summary>
    private static int RollDamage(int baseAttack, double multiplier, Random rng)
    {
        var jitter = EnemyScaler.JitterFloor + rng.NextDouble() * EnemyScaler.JitterSpan;
        var rounded = (int)Math.Round(baseAttack * multiplier * jitter, MidpointRounding.AwayFromZero);
        return Math.Max(MinimumDamagePerUnit, rounded);
    }

    /// <summary>候補行から 1 つを一様抽選する。rng.NextDouble() を 1 回消費する。</summary>
    private static SquadRow PickRow(ImmutableArray<SquadRow> rows, Random rng)
    {
        var index = (int)(rng.NextDouble() * rows.Length);
        if (index >= rows.Length) index = rows.Length - 1; // NextDouble()==境界の保険
        return rows[index];
    }

    /// <summary>挟撃ペアを一様抽選する。rng.NextDouble() を 1 回消費する。</summary>
    private static ImmutableArray<SquadRow> PickPair(Random rng)
    {
        var index = (int)(rng.NextDouble() * PincerPairs.Length);
        if (index >= PincerPairs.Length) index = PincerPairs.Length - 1;
        return PincerPairs[index];
    }
}
