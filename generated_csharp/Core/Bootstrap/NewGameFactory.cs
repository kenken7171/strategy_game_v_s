// =============================================================================
//  ChronicleKnights — NewGameFactory.cs
// -----------------------------------------------------------------------------
//  新規ゲームの初期状態（初期ロスター・初期資金）を組み立てる Godot 非依存の
//  純粋ファクトリ。ゲーム起動時のブートストラップで唯一の「最初の世界」を作る。
//
//  ★ 設計判断 — 脳（純粋ロジック）と身体（Godot 殻）の分離:
//    起動エントリ（ChronicleGlobal.Initialize の呼び出し）は UI/GameDirector が
//    担うが、「新規ゲームがどんな初期状態で始まるか」という仕様そのものは Godot に
//    一切依存しない純粋関数として本クラスへ切り出す。これにより:
//      - xUnit から「初期ロスター人数・初期残高・名前の一意性」を Godot なしで検証可能
//      - 乱数発生器 (Random) は引数注入なので seeded Random で完全再現できる
//      - GameDirector は「ファクトリを呼んで Initialize へ流す」配線だけに専念する
//
//  ★ 初期タイムライン（ターン 1 の予言 3 つ）は本ファクトリでは作らない。
//    ChronicleGlobal.Initialize に initialTimeline=null を渡すと、Initialize 自身が
//    同一の Random を用いて TimelineEngine.CreateInitial で生成する。乱数列を 1 本に
//    統一するため、タイムライン生成は Initialize 側へ委ね、本ファクトリは
//    「ロスターと財布」だけを確定する責務に絞る。
//
//  本クラスは状態を持たない static。入力（乱数）から不変スナップショット
//  (NewGameSeed) を返すだけで、副作用は一切ない。略称は完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Bootstrap;

// ─── 新規ゲーム初期状態（不変スナップショット） ─────────────────────────────

/// <summary>
/// 新規ゲーム開始時に確定する初期状態の束。
/// 呼び出し側（GameDirector）はこれを ChronicleGlobal.Initialize へそのまま流す。
/// </summary>
public sealed record NewGameSeed
{
    /// <summary>初期旅団員リスト（血縁なしの外様で構成された創設メンバー）。</summary>
    public required ImmutableList<Unit> Roster { get; init; }

    /// <summary>初期ポイント経済（創設時の元手を与えた財布）。</summary>
    public required PointsEconomy Economy { get; init; }
}

// ─── ファクトリ本体（純粋・静的・乱数外部注入） ──────────────────────────────

/// <summary>
/// 新規ゲームの初期ロスターと初期資金を組み立てる Godot 非依存の純粋ファクトリ。
/// </summary>
public static class NewGameFactory
{
    // ─── 初期状態パラメータ定数 ───────────────────────────────────────────
    //  ハードコード禁止の憲法に従い、調整可能な数値は名前付き定数として一箇所に集約。

    /// <summary>
    /// 創設時の初期旅団員数。大隊規模（3×3 = 9 名）に合わせ、起動直後から
    /// 編成盤を満たせる人数を創設メンバーとして用意する。
    /// </summary>
    public const int InitialRosterSize = 9;

    /// <summary>
    /// 創設時に財布へ与える初期ポイント残高（元手）。起動直後からスカウト・婚姻が
    /// 試せるよう、いくらかの種銭を持たせる。SoT の収入式の外側にある創設時の付与で
    /// あるため、PointsEconomy.EarnDirect（特殊効果経路）で加算する。
    /// </summary>
    public const int InitialPointsBalance = 20;

    // ─── 初期パーティーの役割保証（リセマラ抑止のバランス制約） ─────────────────
    //  完全一様ランダム 9 名は「前衛ゼロ」「回復ゼロ」「同職 3 ダブり」を高頻度で生み、
    //  詰み開幕＝引き直しを誘発する。そこで詰み盤面だけを構造的に排除する最低保証を置く。
    //  ★ 役割の判定は職名ハードコードではなく JobMaster のデータ（推奨 row / パッシブ）から
    //    導出する（職定義が変わっても保証ロジックが追従する・憲法①/データ駆動）。

    /// <summary>必ず確保する前衛（最前列専任）の最低人数。</summary>
    public const int GuaranteedFrontLine = 2;

    /// <summary>必ず確保する回復役（ターン末分隊治癒）の最低人数。</summary>
    public const int GuaranteedHealer = 1;

    /// <summary>必ず確保する支援役（突撃号令＝InitiativeBuff）の最低人数。</summary>
    public const int GuaranteedSupport = 1;

    /// <summary>同一ジョブの上限（偏り＝同職ダブり過多を防ぐ）。</summary>
    public const int MaxSameJob = 2;

    // ─── 初期状態の生成 ───────────────────────────────────────────────────

    /// <summary>
    /// 新規ゲームの初期ロスター（外様 <see cref="InitialRosterSize"/> 名）と
    /// 初期資金（<see cref="InitialPointsBalance"/> pt）を生成する純粋関数。
    ///
    /// ロスター生成:
    ///   血縁を持たない創設メンバーを ScoutService.CreateOutsiderUnit で人数ぶん作る。
    ///   各員の年齢・寿命・ジョブ・文化圏・性別・名前はすべて乱数で個体差が付く。
    ///   ファーストネームキーは生成のたびに使用済み集合へ追加し、創設メンバー間で
    ///   名前が重複しないことを保証する（重複時は称号付き複合キーへ自動退避）。
    ///
    /// 入力の rng は本メソッド内で消費される（呼び出し側はこの後 Initialize に同じ
    /// rng を渡し、続けてタイムライン生成へ乱数列を引き継ぐ想定）。
    /// </summary>
    /// <param name="rng">初期状態生成に使う乱数発生器（seeded で完全再現可能）。</param>
    /// <returns>初期ロスターと初期資金を束ねた不変スナップショット。</returns>
    public static NewGameSeed Create(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var usedFirstNameKeys = new HashSet<string>(StringComparer.Ordinal);
        var jobCounts = new Dictionary<JobId, int>();
        var roster = ImmutableList.CreateBuilder<Unit>();

        // 1. 役割の背骨を保証する（職の中身・性別・名前・年齢は引き続き乱数。同職は MaxSameJob まで）。
        AddGuaranteed(roster, jobCounts, usedFirstNameKeys, rng, IsFrontLineJob, GuaranteedFrontLine);
        AddGuaranteed(roster, jobCounts, usedFirstNameKeys, rng, IsSupportJob,   GuaranteedSupport);
        AddGuaranteed(roster, jobCounts, usedFirstNameKeys, rng, IsHealerJob,    GuaranteedHealer);

        // 2. 残りスロットは全職からランダムに埋める（同職上限のみ尊重 = 偏り過多を防ぐ）。
        while (roster.Count < InitialRosterSize)
        {
            var job = PickJob(rng, jobCounts, static _ => true) ?? JobMaster.DisplayOrder[0];
            AddRecruit(roster, jobCounts, usedFirstNameKeys, rng, job);
        }

        // 創設時の元手を与える。残高 0 の初期状態へ EarnDirect で加算する
        // （SoT の年次収入・撃破報酬とは別経路の「創設付与」）。
        var economy = PointsEconomy.CreateInitial().EarnDirect(InitialPointsBalance);

        return new NewGameSeed
        {
            Roster  = roster.ToImmutable(),
            Economy = economy,
        };
    }

    // ─── 役割判定（JobMaster のデータから導出・職名ハードコードなし） ─────────────

    /// <summary>最前列専任の前衛か（推奨 row が Front ただ 1 つ＝鉄壁騎士・重装歩兵）。</summary>
    public static bool IsFrontLineJob(JobId job)
    {
        var rows = JobMaster.All[job].FormationGuide.RecommendedRows;
        return rows.Length == 1 && rows[0] == SquadRow.Front;
    }

    /// <summary>回復役か（ターン末分隊治癒パッシブを持つ＝衛生兵）。</summary>
    public static bool IsHealerJob(JobId job) => JobMaster.HasPassive(job, PassiveKind.TurnEndSquadHeal);

    /// <summary>支援役か（突撃号令＝InitiativeBuff パッシブを持つ＝旗手・戦術官）。</summary>
    public static bool IsSupportJob(JobId job) => JobMaster.HasPassive(job, PassiveKind.InitiativeBuff);

    // ─── 生成補助 ──────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="predicate"/> を満たす職を <paramref name="count"/> 名、同職上限を守りつつ
    /// ロスターへ加える。役割枠が同職上限で埋まり切った異常時は「上限内の任意職」へ退避する
    /// （保証は満たせなくとも頭数だけは必ず確保し、無限ループを防ぐ）。
    /// </summary>
    private static void AddGuaranteed(
        ImmutableList<Unit>.Builder roster,
        Dictionary<JobId, int> jobCounts,
        HashSet<string> usedFirstNameKeys,
        Random rng,
        Func<JobId, bool> predicate,
        int count)
    {
        for (var i = 0; i < count && roster.Count < InitialRosterSize; i++)
        {
            var job = PickJob(rng, jobCounts, predicate)
                   ?? PickJob(rng, jobCounts, static _ => true)
                   ?? JobMaster.DisplayOrder[0];
            AddRecruit(roster, jobCounts, usedFirstNameKeys, rng, job);
        }
    }

    /// <summary>指定職の外様 1 名を生成してロスターへ加え、使用済み名・同職カウントを更新する。</summary>
    private static void AddRecruit(
        ImmutableList<Unit>.Builder roster,
        Dictionary<JobId, int> jobCounts,
        HashSet<string> usedFirstNameKeys,
        Random rng,
        JobId job)
    {
        var recruit = ScoutService.CreateOutsiderUnit(rng, usedFirstNameKeys, job);
        usedFirstNameKeys.Add(recruit.FirstNameKey);
        jobCounts[job] = jobCounts.GetValueOrDefault(job) + 1;
        roster.Add(recruit);
    }

    /// <summary>
    /// <paramref name="predicate"/> を満たし、かつ同職上限 <see cref="MaxSameJob"/> 未満の職から
    /// 1 つを均等乱択する。候補が無ければ null（呼び出し側が退避する）。
    /// </summary>
    private static JobId? PickJob(
        Random rng, IReadOnlyDictionary<JobId, int> jobCounts, Func<JobId, bool> predicate)
    {
        var eligible = new List<JobId>();
        foreach (var job in JobMaster.DisplayOrder)
        {
            if (predicate(job) && jobCounts.GetValueOrDefault(job) < MaxSameJob)
            {
                eligible.Add(job);
            }
        }
        if (eligible.Count == 0) return null;
        return eligible[rng.Next(eligible.Count)];
    }
}
