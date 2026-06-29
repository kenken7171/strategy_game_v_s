// =============================================================================
//  ChronicleKnights — ScoutService.cs
// -----------------------------------------------------------------------------
//  外様スカウト（血縁関係のないユニットの有償採用）の純粋ロジック。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  セクション 4「ポイント一元経済」) より:
//    「ポイントを消費して雇うのは血縁なしの外様のみ」。
//    子供（婚姻で生まれたユニット）は 0 pt でロスタに既に含まれるのに対し、
//    外様はポイントを支払って即戦力（成人）を 1 名加える。
//
//  ★ 設計判断 — Godot 非依存の純粋関数として分離:
//    元々 ChronicleGlobal (Godot.Node) のインスタンスメソッドだった生成ロジックを
//    本クラスへ抽出した。これにより:
//      - Godot ランタイム無しで xUnit から残高検証・ロスター増加を完全に単体テスト可能
//      - ChronicleGlobal は「状態の差し替えとシグナル発火」だけに専念（責務分離）
//      - 乱数発生器は引数で注入するため、seeded Random で再現性のあるテストが書ける
//
//  本クラスは状態を持たない static。入力（経済・ロスタ・コスト・乱数）から
//  新しい不変スナップショット (ScoutResult) を返すだけで、副作用は一切ない。
//
//  略称 (BDF/SDF/AB/HL) は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

// ─── スカウト結果（不変スナップショット） ───────────────────────────────────

/// <summary>
/// スカウト成功時に確定する 3 つの不変な結果の束。
/// 呼び出し側（ChronicleGlobal.ExecuteScout）はこの結果で状態を丸ごと差し替える。
/// </summary>
public sealed record ScoutResult
{
    /// <summary>コスト消費後の新しいポイント経済。</summary>
    public required PointsEconomy NewEconomy { get; init; }

    /// <summary>採用ユニットを末尾に追加した新しい旅団員リスト。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>新規採用された外様ユニット本体（UI の「○○が加入！」演出に使う）。</summary>
    public required Unit Recruit { get; init; }
}

// ─── スカウト候補（人事フェーズのスカウトタブに並ぶ 1 名） ───────────────────

/// <summary>
/// スカウト候補プールの 1 件＝生成済みの外様ユニットと、その採用コスト（TargetRating 連動）の束。
/// プールは ChronicleGlobal が SoT として保持し、UI はこれを並べて 1 名を選んで採用する。
/// </summary>
public sealed record ScoutCandidate
{
    /// <summary>生成済みの外様候補ユニット（採用するとこの実体がそのままロスタへ入る）。</summary>
    public required Unit Unit { get; init; }

    /// <summary>採用に必要なポイント（<see cref="ScoutService.ComputeScoutCost"/> で算出）。</summary>
    public required int Cost { get; init; }
}

// ─── スカウト本体（純粋・静的） ─────────────────────────────────────────────

/// <summary>
/// 外様スカウトの残高検証・ポイント消費・ユニット生成・ロスター追加を行う
/// Godot 非依存の純粋ユーティリティ。
/// </summary>
public static class ScoutService
{
    // ─── 生成パラメータ定数 ───────────────────────────────────────────────

    /// <summary>外様スカウトで生成する新ユニットの初期年齢の下限（成人・即戦力）。</summary>
    public const int ScoutMinInitialAge = 16;

    /// <summary>外様スカウトで生成する新ユニットの初期年齢の上限。</summary>
    public const int ScoutMaxInitialAge = 28;

    /// <summary>外様スカウトで生成する新ユニットの寿命の下限。</summary>
    public const int ScoutMinLifespan = 55;

    /// <summary>外様スカウトで生成する新ユニットの寿命の上限。</summary>
    public const int ScoutMaxLifespan = 75;

    // ─── 候補プール・コストのパラメータ ───────────────────────────────────

    /// <summary>スカウトタブに並べる候補の既定数。最小 <see cref="MinScoutCandidateCount"/> を下回らない。</summary>
    public const int DefaultScoutCandidateCount = 3;

    /// <summary>候補数の下限（仕様: 3 名以上は並べられるようにする）。</summary>
    public const int MinScoutCandidateCount = 3;

    /// <summary>採用コスト算出の除数（婚姻の CostDivisor と揃える）。式: ceil(TargetRating / 本値)。</summary>
    public const int ScoutCostDivisor = 20;

    /// <summary>採用コストの下限（安すぎる即戦力を防ぐ床）。</summary>
    public const int MinScoutCost = 3;

    // ─── 採用コスト（ジョブの TargetRating 連動） ─────────────────────────

    /// <summary>
    /// 候補ジョブの強さ（<see cref="JobMaster.TargetRating"/>）に連動した採用コストを算出する。
    /// 式: max(<see cref="MinScoutCost"/>, ceil(TargetRating / <see cref="ScoutCostDivisor"/>))。
    /// 強い職ほど高コストになり、床で下限を保証する（数値は SoT 定数のみ・ハードコード回避）。
    /// </summary>
    public static int ComputeScoutCost(JobId job)
    {
        var rating = JobMaster.TargetRating[job];
        var scaled = (int)Math.Ceiling(rating / (double)ScoutCostDivisor);
        return Math.Max(MinScoutCost, scaled);
    }

    // ─── 候補プール生成 ───────────────────────────────────────────────────

    /// <summary>
    /// 血縁なしの外様候補を <paramref name="count"/> 名（<see cref="MinScoutCandidateCount"/> を下回らない）
    /// 生成し、各々に TargetRating 連動コストを付けた不変プールを返す純粋関数。候補同士でも名前が
    /// 衝突しないよう、払い出すたびに使用済みキー集合へ加えながら生成する。副作用なし（rng のみ消費）。
    /// </summary>
    /// <param name="count">生成する候補数（最小 3 にクランプ）。</param>
    /// <param name="rng">候補生成に使う乱数発生器（seeded で再現可能）。</param>
    /// <param name="usedFirstNameKeys">名前重複回避用の既使用キー集合（通常は現ロスタ由来）。</param>
    public static ImmutableList<ScoutCandidate> CreateCandidatePool(
        int count,
        Random rng,
        IReadOnlySet<string> usedFirstNameKeys)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(usedFirstNameKeys);

        var n = Math.Max(MinScoutCandidateCount, count);
        var used = new HashSet<string>(usedFirstNameKeys, StringComparer.Ordinal);

        var builder = ImmutableList.CreateBuilder<ScoutCandidate>();
        for (int i = 0; i < n; i++)
        {
            var unit = CreateOutsiderUnit(rng, used);
            used.Add(unit.FirstNameKey); // 次の候補と名前が衝突しないように
            builder.Add(new ScoutCandidate { Unit = unit, Cost = ComputeScoutCost(unit.Job) });
        }
        return builder.ToImmutable();
    }

    // ─── 候補の採用（プールから 1 名を雇う） ───────────────────────────────

    /// <summary>
    /// 既に生成済みの候補 1 名を、その候補固有のコストで採用する純粋関数。残高検証 → 消費 →
    /// 候補ユニットをロスタへ追加した <see cref="ScoutResult"/> を返す。失敗（負コスト・残高不足）は null。
    /// 候補ユニットは新規生成せず、プールに並んでいた実体をそのまま使う（表示と採用の一致を保証）。
    /// </summary>
    /// <param name="economy">現在のポイント経済。</param>
    /// <param name="roster">現在の旅団員リスト。</param>
    /// <param name="candidate">採用する候補（コストを内包）。</param>
    public static ScoutResult? TryRecruit(
        PointsEconomy economy,
        ImmutableList<Unit> roster,
        ScoutCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Cost < 0) return null;
        if (!economy.CanAfford(candidate.Cost)) return null;

        PointsEconomy nextEconomy;
        try
        {
            nextEconomy = economy.SpendPoints(candidate.Cost);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return new ScoutResult
        {
            NewEconomy = nextEconomy,
            NewRoster  = roster.Add(candidate.Unit),
            Recruit    = candidate.Unit,
        };
    }

    // ─── スカウト試行 ─────────────────────────────────────────────────────

    /// <summary>
    /// 指定コストを共通サイフから消費して、血縁関係のない外様ユニットを 1 名生成し、
    /// ロスターへ追加した結果を返す純粋関数。
    ///
    /// 判定順:
    ///   1. cost が負 → null（不正入力）
    ///   2. 残高不足 (CanAfford(cost) == false) → null（例外は投げない）
    ///   3. SpendPoints が万一例外を投げた（並列で残高が割れた等）→ null
    ///   4. 成功 → CreateOutsiderUnit で生成し ScoutResult を返す
    ///
    /// 入力の economy / roster は一切変更しない（不変・副作用なし）。
    /// </summary>
    /// <param name="economy">現在のポイント経済。</param>
    /// <param name="roster">現在の旅団員リスト。</param>
    /// <param name="cost">スカウトに支払うポイント数（非負）。</param>
    /// <param name="rng">外様生成に使う乱数発生器（テストでは seeded を渡せる）。</param>
    /// <param name="usedFirstNameKeys">
    /// 名前重複回避のための使用済みファーストネームキー集合。null の場合は
    /// 現在の roster から自動導出する（呼び出し側の利便性のため）。
    /// </param>
    /// <returns>成功時は ScoutResult、失敗（残高不足・不正入力）時は null。</returns>
    public static ScoutResult? TryScout(
        PointsEconomy economy,
        ImmutableList<Unit> roster,
        int cost,
        Random rng,
        IReadOnlySet<string>? usedFirstNameKeys = null)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(rng);

        if (cost < 0) return null;
        if (!economy.CanAfford(cost)) return null;

        PointsEconomy nextEconomy;
        try
        {
            nextEconomy = economy.SpendPoints(cost);
        }
        catch (InvalidOperationException)
        {
            // 残高検証を通った直後でも、念のため SpendPoints の例外を握り潰して
            // 「失敗 = null」に正規化する（呼び出し側の null 安全契約を守る）。
            return null;
        }

        // 使用済みキー集合が渡されなければ、現在の roster の実キーから導出する。
        var used = usedFirstNameKeys
            ?? roster.Select(u => u.FirstNameKey).ToHashSet(StringComparer.Ordinal);

        var recruit = CreateOutsiderUnit(rng, used);

        return new ScoutResult
        {
            NewEconomy = nextEconomy,
            NewRoster  = roster.Add(recruit),
            Recruit    = recruit,
        };
    }

    // ─── 外様ユニット生成 ─────────────────────────────────────────────────

    /// <summary>
    /// 血縁関係のない外様ユニット 1 名を乱数生成する純粋ヘルパー。
    ///
    /// - ジョブ     : JobMaster.DisplayOrder から均等乱択
    /// - 年齢       : 成人かつ即戦力 (ScoutMinInitialAge〜ScoutMaxInitialAge)
    /// - 寿命       : 個体差 (ScoutMinLifespan〜ScoutMaxLifespan)
    /// - レベル     : Unit.InitialLevel (=1)
    /// - 親 / 好感度 / 装備 : 一切なし（外様 = 完全に独立した新規加入）
    /// - 名前・文化圏 : NameGenerator.DrawRandom で文化圏・性別を乱択し、
    ///                重複しないローカライズキーを払い出す。払い出された
    ///                Origin はユニットの血統属性として永続保持する。
    ///
    /// テスト容易性のため public。seeded Random を渡せば年齢・ジョブ・名前が再現する。
    /// </summary>
    /// <param name="rng">外様生成に使う乱数発生器。</param>
    /// <param name="usedFirstNameKeys">名前重複回避用の使用済みキー集合。</param>
    public static Unit CreateOutsiderUnit(Random rng, IReadOnlySet<string> usedFirstNameKeys)
    {
        ArgumentNullException.ThrowIfNull(rng);

        // ジョブを先に均等乱択してからジョブ指定版へ委譲する（乱数消費順は従来どおり：
        // job → age → lifespan → name。seeded Random の再現性を壊さない）。
        var jobs = JobMaster.DisplayOrder;
        var job = jobs[rng.Next(jobs.Length)];
        return CreateOutsiderUnit(rng, usedFirstNameKeys, job);
    }

    /// <summary>
    /// ジョブを指定して外様ユニット 1 名を生成する純粋ヘルパー。年齢・寿命・性別・名前・
    /// 文化圏は乱数のまま、ジョブだけを呼び出し側が固定する。初期パーティーの役割保証
    /// （<see cref="ChronicleKnights.Core.Bootstrap.NewGameFactory"/>）のように「職だけ制御し
    /// たい」用途のための入口。職一様乱択版（2 引数）も内部で本メソッドへ委譲する。
    /// </summary>
    /// <param name="rng">外様生成に使う乱数発生器。</param>
    /// <param name="usedFirstNameKeys">名前重複回避用の使用済みキー集合。</param>
    /// <param name="job">付与するジョブ（呼び出し側が決定）。</param>
    public static Unit CreateOutsiderUnit(Random rng, IReadOnlySet<string> usedFirstNameKeys, JobId job)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(usedFirstNameKeys);

        var initialAge = rng.Next(ScoutMinInitialAge, ScoutMaxInitialAge + 1);
        var lifespan = rng.Next(ScoutMinLifespan, ScoutMaxLifespan + 1);

        // 文化圏・性別を乱択し、重複しない名前キーを払い出す（外様 = 出自も乱数）。
        var name = NameGenerator.DrawRandom(usedFirstNameKeys, rng);

        return new Unit
        {
            Id             = Guid.NewGuid(),
            Job            = job,
            Age            = initialAge,
            MaxAge         = lifespan,
            FirstNameKey   = name.FirstNameKey,
            LastNameKey    = name.LastNameKey,
            Origin         = name.Origin,
            Gender         = name.Gender,
            Level          = Unit.InitialLevel,
            MainEquipment  = null,
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty,
            IsDead         = false,
        };
    }
}
