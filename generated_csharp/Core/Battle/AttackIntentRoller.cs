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
