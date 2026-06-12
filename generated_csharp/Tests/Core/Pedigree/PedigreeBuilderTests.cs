// =============================================================================
//  ChronicleKnights.Tests — PedigreeBuilderTests.cs
// -----------------------------------------------------------------------------
//  家系図構築層 (PedigreeBuilder) の純粋ロジック単体テスト。
//
//  検証観点（A案 要件 #3「多世代の肥大・サイクル・参照欠落への堅牢性」に対応）:
//    1. 根欠落       : universe に根が居なければ Empty グラフを返す
//    2. 世代帯       : 祖父母(-2)/父母(-1)/本人・配偶者・兄弟(0)/子(+1)/孫(+2)
//    3. 関係解決     : Self / Spouse / Parent / Grandparent / Sibling / Child / Grandchild
//    4. 遺影判定     : currentMemberIds に居ない祖先は IsCurrentMember=false
//    5. エッジ       : 両端が共にノードのときだけ親→子コネクタを張る（宙吊り線ゼロ）
//    6. 堅牢性       : サイクル（自己親・相互親）・参照欠落・重複到達で例外も無限も起きない
//    7. ガード       : null 引数は ArgumentNullException
//
//  ★ テスト規約: 表示文字列は使わず、名前キーは ASCII プレースホルダ。Guid は固定値で
//    与え、世代・関係・エッジ本数を厳密にアサートする（SampleData と同方針）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Pedigree;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Pedigree;

public class PedigreeBuilderTests
{
    // ─── 固定 Guid（3 世代の血統ネット） ─────────────────────────────────────
    private static readonly Guid GrandfatherId = G(0x01);
    private static readonly Guid GrandmotherId = G(0x02);
    private static readonly Guid FatherId      = G(0x03);
    private static readonly Guid MotherId      = G(0x04);
    private static readonly Guid SelfId        = G(0x05);
    private static readonly Guid SpouseId      = G(0x06);
    private static readonly Guid SiblingId     = G(0x07);
    private static readonly Guid ChildAId      = G(0x08);
    private static readonly Guid ChildBId      = G(0x09);
    private static readonly Guid GrandchildId  = G(0x0A);
    private static readonly Guid AbsentCoParentId = G(0xFF);

    private static Guid G(byte tail) =>
        new(new byte[] { 0xAB, 0xCD, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, tail });

    // ─── Unit ファクトリ（血統リンクを注入可能な最小構築） ────────────────────
    private static Unit MakeUnit(
        Guid id,
        (Guid father, Guid mother)? parents = null,
        Guid? spouse = null)
    {
        var unit = new Unit
        {
            Id           = id,
            Job          = JobId.Sniper,
            Age          = 30,
            MaxAge       = 60,
            FirstNameKey = "name-key-" + id.ToString("N")[..4],
            LastNameKey  = "family-key-shared",
        };
        if (parents is { } p) unit = unit.WithParentage(p.father, p.mother);
        if (spouse is { } s) unit = unit.WithSpouse(s);
        return unit;
    }

    // ─── 3 世代フィクスチャ（本人を根に、祖父母〜孫まで） ────────────────────
    //   GP1+GP2 -> Father ;  (Mother は初代) ;  Father+Mother -> Self, Sibling ;
    //   Self<->Spouse ;  Self+Spouse -> ChildA, ChildB ;  ChildA+Absent -> Grandchild
    private static (ImmutableDictionary<Guid, Unit> Universe, ImmutableHashSet<Guid> Current)
        BuildFamily()
    {
        var grandfather = MakeUnit(GrandfatherId);
        var grandmother = MakeUnit(GrandmotherId);
        var father = MakeUnit(FatherId, parents: (GrandfatherId, GrandmotherId));
        var mother = MakeUnit(MotherId);
        var self = MakeUnit(SelfId, parents: (FatherId, MotherId), spouse: SpouseId);
        var spouse = MakeUnit(SpouseId, spouse: SelfId);
        var sibling = MakeUnit(SiblingId, parents: (FatherId, MotherId));
        var childA = MakeUnit(ChildAId, parents: (SelfId, SpouseId));
        var childB = MakeUnit(ChildBId, parents: (SelfId, SpouseId));
        // 孫の片親 (AbsentCoParentId) は universe に存在しない（宙吊り親の耐性検証用）。
        var grandchild = MakeUnit(GrandchildId, parents: (ChildAId, AbsentCoParentId));

        var universe = new[]
            {
                grandfather, grandmother, father, mother, self,
                spouse, sibling, childA, childB, grandchild,
            }
            .ToImmutableDictionary(u => u.Id, u => u);

        // 祖父母・父母は既に旅団を去った遺影。本人より下の世代は現役。
        var current = ImmutableHashSet.Create(
            SelfId, SpouseId, SiblingId, ChildAId, ChildBId, GrandchildId);

        return (universe, current);
    }

    private static PedigreeGraph BuildFamilyGraph()
    {
        var (universe, current) = BuildFamily();
        return PedigreeBuilder.Build(universe, current, SelfId);
    }

    private static PedigreeNode NodeOf(PedigreeGraph g, Guid id) =>
        g.Nodes.Single(n => n.UnitId == id);

    // ─── 1. 根欠落 ──────────────────────────────────────────────────────────

    [Fact]
    public void Build_RootNotInUniverse_ReturnsEmpty()
    {
        var (universe, current) = BuildFamily();
        var graph = PedigreeBuilder.Build(universe, current, AbsentCoParentId);

        Assert.False(graph.HasNodes);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Same(PedigreeGraph.Empty, graph);
    }

    [Fact]
    public void Build_SingletonRoot_NoLinks_YieldsLoneSelfNode()
    {
        var solo = MakeUnit(SelfId);
        var universe = ImmutableDictionary<Guid, Unit>.Empty.Add(SelfId, solo);
        var current = ImmutableHashSet.Create(SelfId);

        var graph = PedigreeBuilder.Build(universe, current, SelfId);

        var node = Assert.Single(graph.Nodes);
        Assert.Equal(SelfId, node.UnitId);
        Assert.Equal(PedigreeRelation.Self, node.Relation);
        Assert.Equal(0, node.Generation);
        Assert.Empty(graph.Edges);
    }

    // ─── 2 & 3. 世代帯と関係解決 ─────────────────────────────────────────────

    [Fact]
    public void Build_Family_CollectsAllTenNodesAcrossGenerations()
    {
        var graph = BuildFamilyGraph();

        Assert.Equal(SelfId, graph.RootId);
        Assert.Equal(10, graph.Nodes.Length);
    }

    [Theory]
    [InlineData(0x05, PedigreeRelation.Self, 0)]
    [InlineData(0x06, PedigreeRelation.Spouse, 0)]
    [InlineData(0x07, PedigreeRelation.Sibling, 0)]
    [InlineData(0x03, PedigreeRelation.Parent, -1)]
    [InlineData(0x04, PedigreeRelation.Parent, -1)]
    [InlineData(0x01, PedigreeRelation.Grandparent, -2)]
    [InlineData(0x02, PedigreeRelation.Grandparent, -2)]
    [InlineData(0x08, PedigreeRelation.Child, 1)]
    [InlineData(0x09, PedigreeRelation.Child, 1)]
    [InlineData(0x0A, PedigreeRelation.Grandchild, 2)]
    public void Build_Family_AssignsRelationAndGeneration(
        byte tail, PedigreeRelation expectedRelation, int expectedGeneration)
    {
        var graph = BuildFamilyGraph();
        var node = NodeOf(graph, G(tail));

        Assert.Equal(expectedRelation, node.Relation);
        Assert.Equal(expectedGeneration, node.Generation);
    }

    // ─── 4. 遺影判定（現役 / 去りし者） ──────────────────────────────────────

    [Fact]
    public void Build_Family_MarksDepartedAncestorsAsNonMembers()
    {
        var graph = BuildFamilyGraph();

        // 祖父母・父母は遺影（非現役）。
        Assert.False(NodeOf(graph, GrandfatherId).IsCurrentMember);
        Assert.False(NodeOf(graph, FatherId).IsCurrentMember);
        // 本人・子・孫は現役。
        Assert.True(NodeOf(graph, SelfId).IsCurrentMember);
        Assert.True(NodeOf(graph, ChildAId).IsCurrentMember);
        Assert.True(NodeOf(graph, GrandchildId).IsCurrentMember);
    }

    // ─── 5. エッジ（親→子コネクタ、宙吊り線ゼロ） ───────────────────────────

    [Fact]
    public void Build_Family_DrawsParentChildEdgesForPresentPairsOnly()
    {
        var graph = BuildFamilyGraph();
        var edges = graph.Edges.Select(e => (e.ParentId, e.ChildId)).ToHashSet();

        // 祖父母→父
        Assert.Contains((GrandfatherId, FatherId), edges);
        Assert.Contains((GrandmotherId, FatherId), edges);
        // 父母→本人 / 父母→兄弟
        Assert.Contains((FatherId, SelfId), edges);
        Assert.Contains((MotherId, SelfId), edges);
        Assert.Contains((FatherId, SiblingId), edges);
        Assert.Contains((MotherId, SiblingId), edges);
        // 本人・配偶者→子
        Assert.Contains((SelfId, ChildAId), edges);
        Assert.Contains((SpouseId, ChildAId), edges);
        Assert.Contains((SelfId, ChildBId), edges);
        Assert.Contains((SpouseId, ChildBId), edges);
        // 子→孫（実在側の親のみ）
        Assert.Contains((ChildAId, GrandchildId), edges);

        // 宙吊り親（universe に居ない孫の片親）からのエッジは張られない。
        Assert.DoesNotContain((AbsentCoParentId, GrandchildId), edges);

        // 合計 11 本ちょうど（重複なし）。
        Assert.Equal(11, graph.Edges.Length);
    }

    // ─── 6. 堅牢性（サイクル・参照欠落・重複） ───────────────────────────────

    [Fact]
    public void Build_SelfParentCycle_DoesNotLoopAndKeepsSelfAsRoot()
    {
        // 自分自身を親に持つ不正データ（養子データ崩れの想定）。
        var self = MakeUnit(SelfId, parents: (SelfId, MotherId));
        var mother = MakeUnit(MotherId);
        var universe = new[] { self, mother }.ToImmutableDictionary(u => u.Id, u => u);
        var current = ImmutableHashSet.Create(SelfId, MotherId);

        var graph = PedigreeBuilder.Build(universe, current, SelfId);

        // 本人は Self のまま 1 度だけ採録（Parent へ降格しない）。
        Assert.Equal(PedigreeRelation.Self, NodeOf(graph, SelfId).Relation);
        Assert.Single(graph.Nodes, n => n.UnitId == SelfId);
        // 母は父母世代として採録される。
        Assert.Equal(PedigreeRelation.Parent, NodeOf(graph, MotherId).Relation);
        // 自己ループのエッジ (Self->Self) は張られない。
        Assert.DoesNotContain(graph.Edges, e => e.ParentId == SelfId && e.ChildId == SelfId);
    }

    [Fact]
    public void Build_MutualParentCycle_TerminatesWithoutStackOverflow()
    {
        // A の親が B、B の親が A という相互サイクル。
        var a = MakeUnit(SelfId, parents: (SpouseId, MotherId));
        var b = MakeUnit(SpouseId, parents: (SelfId, FatherId));
        var mother = MakeUnit(MotherId);
        var father = MakeUnit(FatherId);
        var universe = new[] { a, b, mother, father }
            .ToImmutableDictionary(u => u.Id, u => u);
        var current = ImmutableHashSet<Guid>.Empty;

        // 例外も無限再帰も起きずに完了すること自体が検証。
        var graph = PedigreeBuilder.Build(universe, current, SelfId);

        Assert.Equal(SelfId, graph.RootId);
        Assert.True(graph.HasNodes);
        // 各ユニットはちょうど 1 ノード（重複採録なし）。
        Assert.Equal(graph.Nodes.Length, graph.Nodes.Select(n => n.UnitId).Distinct().Count());
    }

    [Fact]
    public void Build_DanglingParentReferences_AreSkippedSafely()
    {
        // 両親とも universe に存在しない初代（参照欠落）。
        var self = MakeUnit(SelfId, parents: (AbsentCoParentId, G(0xFE)));
        var universe = ImmutableDictionary<Guid, Unit>.Empty.Add(SelfId, self);
        var current = ImmutableHashSet.Create(SelfId);

        var graph = PedigreeBuilder.Build(universe, current, SelfId);

        // 本人ノードだけが残り、欠落親はノード化もエッジ化もされない。
        Assert.Single(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    // ─── 7. ガード句 ────────────────────────────────────────────────────────

    [Fact]
    public void Build_NullUniverse_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PedigreeBuilder.Build(null!, ImmutableHashSet<Guid>.Empty, SelfId));
    }

    [Fact]
    public void Build_NullCurrentMembers_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PedigreeBuilder.Build(ImmutableDictionary<Guid, Unit>.Empty, null!, SelfId));
    }
}
