// =============================================================================
//  ChronicleKnights.Tests — EquipmentDropServiceTests.cs
// -----------------------------------------------------------------------------
//  装備ドロップの 3 択候補を決定論的に生成する純粋層 EquipmentDropService の単体テスト。
//
//  検証観点:
//    - 候補数（count 個）と全候補のレベルが指定レベル（クランプ後）で揃う。
//    - 各候補の Affix がすべて解決可能（AffixMaster の既知キー）。
//    - 決定論: 同一シード＋同一 idFactory なら候補列が完全一致。
//    - 境界: count<=0 は空、レベルは Equipment 下限〜上限へクランプ。
// =============================================================================

using System;
using System.Linq;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Units;

public class EquipmentDropServiceTests
{
    // 決定論テスト用の連番 Guid 採番器。
    private static Func<Guid> SequentialIds()
    {
        var n = 0;
        return () => new Guid(++n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void RollCandidates_ReturnsRequestedCount_AllAtClampedLevel()
    {
        var candidates = EquipmentDropService.RollCandidates(3, new Random(1));

        Assert.Equal(EquipmentDropService.DropCandidateCount, candidates.Length);
        Assert.All(candidates, c => Assert.Equal(3, c.Level));
    }

    [Fact]
    public void RollCandidates_EveryAffixIsResolvable()
    {
        var candidates = EquipmentDropService.RollCandidates(5, new Random(99));

        foreach (var c in candidates)
        {
            Assert.All(c.AffixKeys, k => Assert.NotNull(AffixMaster.Resolve(k)));
        }
    }

    [Fact]
    public void RollCandidates_SameSeedAndIdFactory_AreIdentical()
    {
        var a = EquipmentDropService.RollCandidates(4, new Random(42), 3, SequentialIds());
        var b = EquipmentDropService.RollCandidates(4, new Random(42), 3, SequentialIds());

        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Id, b[i].Id);
            Assert.Equal(a[i].ItemId, b[i].ItemId);
            Assert.Equal(a[i].Level, b[i].Level);
            Assert.Equal(a[i].AffixKeys.ToArray(), b[i].AffixKeys.ToArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RollCandidates_NonPositiveCount_IsEmpty(int count)
    {
        Assert.Empty(EquipmentDropService.RollCandidates(3, new Random(1), count));
    }

    [Theory]
    [InlineData(0, Equipment.MinEquipmentLevel)]
    [InlineData(99, Equipment.MaxEquipmentLevel)]
    public void RollCandidates_LevelIsClamped(int requested, int expected)
    {
        var candidates = EquipmentDropService.RollCandidates(requested, new Random(7));
        Assert.All(candidates, c => Assert.Equal(expected, c.Level));
    }
}
