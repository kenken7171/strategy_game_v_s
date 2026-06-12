// =============================================================================
//  ChronicleKnights — PedigreeGraph.cs
// -----------------------------------------------------------------------------
//  家系図（血統ネット）の純粋データモデルと、その純粋な構築ファクトリ。
//
//  ★ 何を組み立てるのか:
//    1 名のユニット（TargetUnitId）を根として、SoT に刻まれた血統リンク
//    （<see cref="ChronicleKnights.Core.Units.Unit.Parentage"/> = 父母 Id）と
//    婚姻リンク（<see cref="ChronicleKnights.Core.Units.Unit.SpouseId"/> = 配偶者 Id）を
//    手繰り、4 つの世代帯へまたがる縦系の家系ネットを 1 枚のグラフへ写し取る:
//      - 祖父母世代 (Generation -2)
//      - 父母世代   (Generation -1)
//      - 本人・配偶者・兄弟世代 (Generation 0)
//      - 子孫世代   (Generation +1 = 子, +2 = 孫)
//
//  ★ 設計憲法の遵守:
//    ① 日本語ハードコード禁止: 本グラフは Guid・列挙・整数だけを運び、表示用テキスト
//       （ユニット名・ジョブ名）を一切持たない。名前解決は UI 層が localization 経由で行う。
//    ② 完全不変: PedigreeNode / PedigreeEdge / PedigreeGraph は全て sealed record・
//       init 専用・ImmutableArray。ファクトリは入力から新規生成するだけの副作用ゼロな
//       純粋関数で、Godot に 1 ミリも依存しない（xUnit で完全検証可能）。
//    ③ 単一 SoT・単方向: 入力 universe（Id→Unit の血統宇宙）と currentMemberIds（現役 Id 集合）は
//       ChronicleGlobal.GetPedigreeUniverse() が「英霊アーカイブ ∪ 現役ロスタ」を合成して渡す。
//       本ファイルは状態を一切持たず、与えられたスナップショットを読むだけ。
//
//  ★ 堅牢性（多世代の肥大・サイクル・参照欠落への耐性）:
//    - 世代は -2〜+2 に構造的に限定されるため、養子縁組や不正データで循環が生じても
//      探索は有限深度で必ず停止する（スタックオーバーフローを構造的に排除）。
//    - 既に採録した Id は二度採らない（visited 集合による重複排除）。これにより
//      同一ユニットが複数経路で到達してもノードは 1 個に収束する（サイクルでも無限化しない）。
//    - 全ての血統リンク解決は TryGetValue + null 番兵で行い、参照先がアーカイブにも
//      ロスタにも存在しない（既にロストした祖先のさらに祖先など）場合は黙ってスキップする。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Pedigree;

/// <summary>
/// 家系図上での、根（本人）から見た血縁関係の種別。世代帯の割り当てと
/// UI のカード装飾（遺影 sepia / 現役強調など）の判断材料になる。
/// </summary>
public enum PedigreeRelation
{
    /// <summary>祖父母（父母の親。Generation -2）。</summary>
    Grandparent,

    /// <summary>父母（本人の親。Generation -1）。</summary>
    Parent,

    /// <summary>本人（家系図の根。Generation 0）。</summary>
    Self,

    /// <summary>配偶者（本人と婚姻リンクされた相手。Generation 0）。</summary>
    Spouse,

    /// <summary>兄弟姉妹（本人と父か母を 1 人以上共有する者。Generation 0）。</summary>
    Sibling,

    /// <summary>子（本人または配偶者を親に持つ者。Generation +1）。</summary>
    Child,

    /// <summary>孫（子を親に持つ者。Generation +2）。</summary>
    Grandchild,
}

/// <summary>
/// 家系図の 1 ノード（= 1 ユニット）を表す完全不変レコード。
/// </summary>
public sealed record PedigreeNode
{
    /// <summary>このノードのユニット Id（family net 上の一意キー）。</summary>
    public required Guid UnitId { get; init; }

    /// <summary>このノードのユニット静止画（現役なら最新、去った者なら遺影スナップショット）。</summary>
    public required Unit Unit { get; init; }

    /// <summary>根（本人）から見た血縁関係の種別。</summary>
    public required PedigreeRelation Relation { get; init; }

    /// <summary>
    /// 本人を 0 とした世代オフセット（祖先は負、子孫は正）。
    /// -2=祖父母 / -1=父母 / 0=本人・配偶者・兄弟 / +1=子 / +2=孫。
    /// </summary>
    public required int Generation { get; init; }

    /// <summary>
    /// 現役ロスタに在籍中か。false の場合は既に旅団を去った祖先（遺影）として
    /// UI が sepia / 半透明で描く判断に使う。
    /// </summary>
    public required bool IsCurrentMember { get; init; }
}

/// <summary>
/// 親 → 子の血統エッジ（コネクタ線の 1 本分）を表す完全不変レコード。
/// 両端のユニットが共にグラフのノードとして存在する場合にのみ生成される。
/// </summary>
public sealed record PedigreeEdge
{
    /// <summary>親側ノードの Id（線の始点）。</summary>
    public required Guid ParentId { get; init; }

    /// <summary>子側ノードの Id（線の終点）。</summary>
    public required Guid ChildId { get; init; }
}

/// <summary>
/// 家系図（血統ネット）の完全不変スナップショット。ノード集合とエッジ集合を運ぶ。
/// </summary>
public sealed record PedigreeGraph
{
    /// <summary>家系図の根（本人）のユニット Id。ノードが空の場合は <see cref="Guid.Empty"/>。</summary>
    public required Guid RootId { get; init; }

    /// <summary>全ノード（祖父母〜孫）。採録順（本人→配偶者→父母→祖父母→兄弟→子→孫）。</summary>
    public required ImmutableArray<PedigreeNode> Nodes { get; init; }

    /// <summary>全エッジ（親→子のコネクタ）。両端が共にノードとして存在するものだけ。</summary>
    public required ImmutableArray<PedigreeEdge> Edges { get; init; }

    /// <summary>ノードを 1 つも持たない空グラフ。根が見つからない場合の安全な戻り値。</summary>
    public static PedigreeGraph Empty { get; } = new()
    {
        RootId = Guid.Empty,
        Nodes = ImmutableArray<PedigreeNode>.Empty,
        Edges = ImmutableArray<PedigreeEdge>.Empty,
    };

    /// <summary>このグラフが描画すべきノードを 1 つでも持つか。</summary>
    public bool HasNodes => !Nodes.IsDefaultOrEmpty;
}

/// <summary>
/// 血統宇宙（Id→Unit）と現役 Id 集合から、指定ユニットを根とする家系図を組み立てる
/// 静的な純粋ファクトリ。状態を一切持たず、Godot に依存しない。
/// </summary>
public static class PedigreeBuilder
{
    /// <summary>子孫方向に降る最大世代（孫まで）。これにより探索は構造的に有限深度で停止する。</summary>
    private const int MaxDescendantGeneration = 2;

    /// <summary>
    /// <paramref name="rootId"/> を根として家系図を構築する純粋関数。
    ///
    /// 採録順序（各ユニットは重複排除され、最初に到達した関係で 1 度だけ採られる）:
    ///   1. 本人 (Self, 0)
    ///   2. 配偶者 (Spouse, 0)
    ///   3. 父母 (Parent, -1)
    ///   4. 祖父母 (Grandparent, -2)
    ///   5. 兄弟姉妹 (Sibling, 0)
    ///   6. 子 (Child, +1)
    ///   7. 孫 (Grandchild, +2)
    ///
    /// エッジは最後に、採録済み全ノードの Parentage を走査して「親・子が共に
    /// ノードとして存在する」ペアにだけ張る（祖父母→父母→本人/兄弟→子→孫が一気通貫で繋がる）。
    /// </summary>
    /// <param name="universe">血統宇宙（Id→Unit）。英霊アーカイブ ∪ 現役ロスタ。null 不可。</param>
    /// <param name="currentMemberIds">現役ロスタの Id 集合（遺影判定に使う）。null 不可。</param>
    /// <param name="rootId">家系図の根とするユニット Id。universe に不在なら空グラフを返す。</param>
    /// <returns>構築された家系図。根が見つからない場合は <see cref="PedigreeGraph.Empty"/>。</returns>
    public static PedigreeGraph Build(
        IReadOnlyDictionary<Guid, Unit> universe,
        IReadOnlySet<Guid> currentMemberIds,
        Guid rootId)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(currentMemberIds);

        // 根が血統宇宙に存在しなければ何も描けない（ヌル安全に空グラフを返す）。
        if (!universe.TryGetValue(rootId, out var rootUnit))
        {
            return PedigreeGraph.Empty;
        }

        // 採録済みノードを Id で重複排除しながら順序付きで蓄える。
        var nodeOrder = new List<PedigreeNode>();
        var visited = new HashSet<Guid>();

        // ローカル採録ヘルパ: 未採録の Id のみノード化する（重複・サイクルを構造的に弾く）。
        bool TryAdd(Guid id, PedigreeRelation relation, int generation)
        {
            if (id == Guid.Empty) return false;
            if (visited.Contains(id)) return false;
            if (!universe.TryGetValue(id, out var unit)) return false;

            visited.Add(id);
            nodeOrder.Add(new PedigreeNode
            {
                UnitId = id,
                Unit = unit,
                Relation = relation,
                Generation = generation,
                IsCurrentMember = currentMemberIds.Contains(id),
            });
            return true;
        }

        // ── 1. 本人 ──────────────────────────────────────────────────────────
        TryAdd(rootId, PedigreeRelation.Self, 0);

        // ── 2. 配偶者 ────────────────────────────────────────────────────────
        if (rootUnit.SpouseId is { } spouseId)
        {
            TryAdd(spouseId, PedigreeRelation.Spouse, 0);
        }

        // ── 3. 父母 ──────────────────────────────────────────────────────────
        //   後段の祖父母探索のため、採録できた親の Id を控える。
        var parentIds = new List<Guid>(2);
        if (rootUnit.Parentage is { } rootParentage)
        {
            if (TryAdd(rootParentage.FatherId, PedigreeRelation.Parent, -1))
                parentIds.Add(rootParentage.FatherId);
            if (TryAdd(rootParentage.MotherId, PedigreeRelation.Parent, -1))
                parentIds.Add(rootParentage.MotherId);
        }

        // ── 4. 祖父母 ────────────────────────────────────────────────────────
        foreach (var parentId in parentIds)
        {
            if (!universe.TryGetValue(parentId, out var parentUnit)) continue;
            if (parentUnit.Parentage is not { } grandParentage) continue;
            TryAdd(grandParentage.FatherId, PedigreeRelation.Grandparent, -2);
            TryAdd(grandParentage.MotherId, PedigreeRelation.Grandparent, -2);
        }

        // ── 5. 兄弟姉妹 ──────────────────────────────────────────────────────
        //   本人が父母を持つ場合のみ。父か母を 1 人以上共有する者を兄弟とみなす。
        if (rootUnit.Parentage is { } selfParentage)
        {
            foreach (var kv in universe)
            {
                var candidate = kv.Value;
                if (candidate.Id == rootId) continue;
                if (candidate.Parentage is not { } cParentage) continue;
                var sharesParent =
                    cParentage.FatherId == selfParentage.FatherId ||
                    cParentage.MotherId == selfParentage.MotherId;
                if (sharesParent)
                {
                    TryAdd(candidate.Id, PedigreeRelation.Sibling, 0);
                }
            }
        }

        // ── 6. 子 ────────────────────────────────────────────────────────────
        //   本人または配偶者を親に持つ者。後段の孫探索のため Id を控える。
        var parentPool = new HashSet<Guid> { rootId };
        if (rootUnit.SpouseId is { } sId) parentPool.Add(sId);

        var childIds = new List<Guid>();
        foreach (var kv in universe)
        {
            var candidate = kv.Value;
            if (candidate.Parentage is not { } cParentage) continue;
            if (parentPool.Contains(cParentage.FatherId) || parentPool.Contains(cParentage.MotherId))
            {
                if (TryAdd(candidate.Id, PedigreeRelation.Child, 1))
                    childIds.Add(candidate.Id);
            }
        }

        // ── 7. 孫 ────────────────────────────────────────────────────────────
        //   子のいずれかを親に持つ者。世代は MaxDescendantGeneration で頭打ち。
        if (childIds.Count > 0)
        {
            var childSet = new HashSet<Guid>(childIds);
            foreach (var kv in universe)
            {
                var candidate = kv.Value;
                if (candidate.Parentage is not { } cParentage) continue;
                if (childSet.Contains(cParentage.FatherId) || childSet.Contains(cParentage.MotherId))
                {
                    TryAdd(candidate.Id, PedigreeRelation.Grandchild, MaxDescendantGeneration);
                }
            }
        }

        // ── エッジ生成 ───────────────────────────────────────────────────────
        //   採録済み全ノードの Parentage を走査し、親・子が共にノードであるペアに線を張る。
        //   両端ノードの存在を保証してから張るため、宙に浮く線は構造的に生じない。
        var presentIds = visited; // visited は採録済みノードの Id 集合そのもの。
        var edgeSeen = new HashSet<(Guid, Guid)>();
        var edges = ImmutableArray.CreateBuilder<PedigreeEdge>();

        foreach (var node in nodeOrder)
        {
            if (node.Unit.Parentage is not { } parentage) continue;
            AddEdgeIfPresent(parentage.FatherId, node.UnitId, presentIds, edgeSeen, edges);
            AddEdgeIfPresent(parentage.MotherId, node.UnitId, presentIds, edgeSeen, edges);
        }

        return new PedigreeGraph
        {
            RootId = rootId,
            Nodes = nodeOrder.ToImmutableArray(),
            Edges = edges.ToImmutable(),
        };
    }

    /// <summary>
    /// 親 Id・子 Id が共に採録済みノードであり、かつ未登録のエッジである場合にのみ
    /// コネクタを 1 本追加する内部ヘルパ（宙吊り線・重複線の二重防止）。
    /// </summary>
    private static void AddEdgeIfPresent(
        Guid parentId,
        Guid childId,
        HashSet<Guid> presentIds,
        HashSet<(Guid, Guid)> edgeSeen,
        ImmutableArray<PedigreeEdge>.Builder edges)
    {
        if (parentId == Guid.Empty) return;
        if (!presentIds.Contains(parentId)) return;
        if (!presentIds.Contains(childId)) return;
        if (!edgeSeen.Add((parentId, childId))) return;
        edges.Add(new PedigreeEdge { ParentId = parentId, ChildId = childId });
    }
}
