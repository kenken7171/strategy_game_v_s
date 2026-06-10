// =============================================================================
//  ChronicleKnights.Tests — FormationBoardTests.cs
// -----------------------------------------------------------------------------
//  V字 3×3 編成盤面（FormationBoard）の純粋論理単体テスト。
//
//  検証観点（ユーザー要件 #3 に対応）:
//    1. 初期化     : Empty() が 9 マスを正準座標で空席初期化する
//    2. 配置/除去  : WithUnitAt / ClearedAt が不変式で意図通り動く
//    3. 重複退去   : 同一 ID の再配置で元マスが自動的に空席化する
//    4. 交換       : SwapSlots が 2 マスの占有者を入れ替える
//    5. 回転       : Rotated が分隊単位で列順を崩さず正確に回す（CW/CCW/恒等）
//    6. 集計/堅牢  : PlacedUnitIds / EmptyCoordinates / IsFull、範囲外列の例外
//
//  ★ テスト規約: 日本語リテラルは用いず、識別子も ASCII プレースホルダ（固定
//    Guid）で与える（PhaseNameResolverTests / SampleData と同じ方針）。盤面比較は
//    レコード等価ではなくクエリ API（OccupantAt 等）で行う。
// =============================================================================

using System;
using System.Linq;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;   // SquadRow（V 字分隊行）の正本はジョブ定義側にある
using Xunit;

namespace ChronicleKnights.Tests.Core.Formation;

public class FormationBoardTests
{
    // ─── 固定 Guid（アサート用の既知の値・ASCII のみ） ───────────────────

    private static readonly Guid UnitA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid UnitB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid UnitC = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid UnitD = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static SlotCoordinate At(SquadRow row, int column) => new(row, column);

    // ─── 1. 初期化 ─────────────────────────────────────────────────────────

    [Fact]
    public void Empty_CreatesNineEmptySlots()
    {
        var board = FormationBoard.Empty();

        Assert.Equal(FormationBoard.SlotCount, board.Slots.Length);
        Assert.Equal(9, board.Slots.Length);
        Assert.True(board.IsEmpty);
        Assert.False(board.IsFull);
        Assert.Equal(0, board.OccupiedCount);
        Assert.Empty(board.PlacedUnitIds);
        Assert.Equal(9, board.EmptyCoordinates.Length);
        Assert.All(board.Slots, slot => Assert.True(slot.IsEmpty));
    }

    [Fact]
    public void Empty_ContainsEveryCanonicalCoordinateExactlyOnce()
    {
        var board = FormationBoard.Empty();

        foreach (var row in FormationBoard.RowOrder)
        {
            for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                var matches = board.Slots.Count(s => s.Coordinate == At(row, column));
                Assert.Equal(1, matches);
            }
        }
    }

    // ─── 2. 配置 / 除去 ────────────────────────────────────────────────────

    [Fact]
    public void WithUnitAt_PlacesUnitAtCoordinate()
    {
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 1), UnitA);

        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.Front, 1)));
        Assert.Equal(1, board.OccupiedCount);
        Assert.True(board.Contains(UnitA));
        Assert.Equal(At(SquadRow.Front, 1), board.CoordinateOf(UnitA));
    }

    [Fact]
    public void WithUnitAt_IsImmutable_OriginalBoardUnchanged()
    {
        var original = FormationBoard.Empty();
        var placed = original.WithUnitAt(At(SquadRow.Front, 0), UnitA);

        // 元の盤面は空のまま、新しい盤面だけが配置を持つ。
        Assert.True(original.IsEmpty);
        Assert.Null(original.OccupantAt(At(SquadRow.Front, 0)));
        Assert.Equal(UnitA, placed.OccupantAt(At(SquadRow.Front, 0)));
    }

    [Fact]
    public void ClearedAt_RemovesOccupant()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.RearLeft, 2), UnitA)
            .ClearedAt(At(SquadRow.RearLeft, 2));

        Assert.Null(board.OccupantAt(At(SquadRow.RearLeft, 2)));
        Assert.False(board.Contains(UnitA));
        Assert.True(board.IsEmpty);
    }

    [Fact]
    public void ClearedAt_EmptySlot_ReturnsSameInstance()
    {
        var board = FormationBoard.Empty();

        // 空席に対する除去は無変化（同一参照を返す）。
        Assert.Same(board, board.ClearedAt(At(SquadRow.Front, 0)));
    }

    // ─── 3. 重複配置の自動退去 ─────────────────────────────────────────────

    [Fact]
    public void WithUnitAt_SameUnitToNewCoordinate_AutoEvictsFromOldCoordinate()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .WithUnitAt(At(SquadRow.RearRight, 2), UnitA);

        Assert.Null(board.OccupantAt(At(SquadRow.Front, 0)));      // 元マスは空席化
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearRight, 2)));
        Assert.Equal(1, board.OccupiedCount);                     // 二重に現れない
        Assert.Equal(At(SquadRow.RearRight, 2), board.CoordinateOf(UnitA));
    }

    [Fact]
    public void WithUnitAt_OverwritesDifferentOccupant_PreviousUnitLeavesBoard()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .WithUnitAt(At(SquadRow.Front, 0), UnitB);

        Assert.Equal(UnitB, board.OccupantAt(At(SquadRow.Front, 0)));
        Assert.False(board.Contains(UnitA));                      // 上書きされたユニットは退去
        Assert.Equal(1, board.OccupiedCount);
    }

    // ─── 4. 交換 ───────────────────────────────────────────────────────────

    [Fact]
    public void SwapSlots_ExchangesOccupants()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .WithUnitAt(At(SquadRow.RearLeft, 1), UnitB)
            .SwapSlots(At(SquadRow.Front, 0), At(SquadRow.RearLeft, 1));

        Assert.Equal(UnitB, board.OccupantAt(At(SquadRow.Front, 0)));
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearLeft, 1)));
    }

    [Fact]
    public void SwapSlots_WithEmptyTarget_MovesOccupant()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .SwapSlots(At(SquadRow.Front, 0), At(SquadRow.RearRight, 2));

        Assert.Null(board.OccupantAt(At(SquadRow.Front, 0)));
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearRight, 2)));
        Assert.Equal(1, board.OccupiedCount);
    }

    [Fact]
    public void SwapSlots_SameCoordinate_ReturnsSameInstance()
    {
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), UnitA);

        Assert.Same(board, board.SwapSlots(At(SquadRow.Front, 0), At(SquadRow.Front, 0)));
    }

    // ─── 5. 回転 ───────────────────────────────────────────────────────────

    // 各行に 1 体ずつ置いた基準盤面（列 0 のみ使用）。
    private static FormationBoard ThreeRowSample() => FormationBoard.Empty()
        .WithUnitAt(At(SquadRow.Front, 0), UnitA)
        .WithUnitAt(At(SquadRow.RearLeft, 0), UnitB)
        .WithUnitAt(At(SquadRow.RearRight, 0), UnitC);

    [Fact]
    public void Rotated_Clockwise_MapsRowsBySquadSwap()
    {
        var board = ThreeRowSample().Rotated(RotationDirection.Clockwise);

        // CW: Front←RearLeft, RearRight←Front, RearLeft←RearRight
        Assert.Equal(UnitB, board.OccupantAt(At(SquadRow.Front, 0)));      // was RearLeft
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearRight, 0)));  // was Front
        Assert.Equal(UnitC, board.OccupantAt(At(SquadRow.RearLeft, 0)));   // was RearRight
    }

    [Fact]
    public void Rotated_CounterClockwise_MapsRowsBySquadSwap()
    {
        var board = ThreeRowSample().Rotated(RotationDirection.CounterClockwise);

        // CCW: Front←RearRight, RearLeft←Front, RearRight←RearLeft
        Assert.Equal(UnitC, board.OccupantAt(At(SquadRow.Front, 0)));      // was RearRight
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearLeft, 0)));   // was Front
        Assert.Equal(UnitB, board.OccupantAt(At(SquadRow.RearRight, 0)));  // was RearLeft
    }

    [Fact]
    public void Rotated_PreservesColumnOrderWithinRow()
    {
        // Front 行に列順 0/1/2 で A/B/C を配置。
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .WithUnitAt(At(SquadRow.Front, 1), UnitB)
            .WithUnitAt(At(SquadRow.Front, 2), UnitC)
            .Rotated(RotationDirection.Clockwise);

        // CW で Front → RearRight へ移動。列順は完全保持される。
        Assert.Equal(UnitA, board.OccupantAt(At(SquadRow.RearRight, 0)));
        Assert.Equal(UnitB, board.OccupantAt(At(SquadRow.RearRight, 1)));
        Assert.Equal(UnitC, board.OccupantAt(At(SquadRow.RearRight, 2)));
    }

    [Fact]
    public void Rotated_Clockwise_ThreeTimes_IsIdentity()
    {
        var original = ThreeRowSample();
        var rotated = original
            .Rotated(RotationDirection.Clockwise)
            .Rotated(RotationDirection.Clockwise)
            .Rotated(RotationDirection.Clockwise);

        foreach (var row in FormationBoard.RowOrder)
        {
            Assert.Equal(
                original.OccupantAt(At(row, 0)),
                rotated.OccupantAt(At(row, 0)));
        }
    }

    [Fact]
    public void Rotated_ClockwiseThenCounterClockwise_IsIdentity()
    {
        var original = ThreeRowSample();
        var roundTrip = original
            .Rotated(RotationDirection.Clockwise)
            .Rotated(RotationDirection.CounterClockwise);

        foreach (var row in FormationBoard.RowOrder)
        {
            Assert.Equal(
                original.OccupantAt(At(row, 0)),
                roundTrip.OccupantAt(At(row, 0)));
        }
    }

    [Fact]
    public void Rotated_PreservesOccupantCount()
    {
        var board = ThreeRowSample().Rotated(RotationDirection.Clockwise);
        Assert.Equal(3, board.OccupiedCount);
    }

    // ─── 6. 集計 / 堅牢性 ──────────────────────────────────────────────────

    [Fact]
    public void IsFull_WhenAllNineSlotsOccupied()
    {
        var board = FormationBoard.Empty();
        var unit = 0;
        foreach (var row in FormationBoard.RowOrder)
        {
            for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                // 9 個の相異なる Guid を割り当てる。
                board = board.WithUnitAt(At(row, column), DeterministicId(unit++));
            }
        }

        Assert.True(board.IsFull);
        Assert.False(board.IsEmpty);
        Assert.Equal(9, board.OccupiedCount);
        Assert.Equal(9, board.PlacedUnitIds.Length);
        Assert.Empty(board.EmptyCoordinates);
    }

    [Fact]
    public void PlacedUnitIds_AreReturnedInCanonicalOrder()
    {
        var board = FormationBoard.Empty()
            .WithUnitAt(At(SquadRow.RearRight, 0), UnitC)
            .WithUnitAt(At(SquadRow.Front, 0), UnitA)
            .WithUnitAt(At(SquadRow.RearLeft, 0), UnitB);

        // 正準順は Front → RearLeft → RearRight なので、配置順に依らず A,B,C。
        Assert.Equal(new[] { UnitA, UnitB, UnitC }, board.PlacedUnitIds.ToArray());
    }

    [Fact]
    public void EmptyCoordinates_ExcludeOccupiedSlots()
    {
        var board = FormationBoard.Empty().WithUnitAt(At(SquadRow.Front, 0), UnitA);

        Assert.Equal(8, board.EmptyCoordinates.Length);
        Assert.DoesNotContain(At(SquadRow.Front, 0), board.EmptyCoordinates);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void WithUnitAt_ColumnOutOfRange_Throws(int badColumn)
    {
        var board = FormationBoard.Empty();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => board.WithUnitAt(At(SquadRow.Front, badColumn), UnitA));
    }

    [Fact]
    public void OccupantAt_ColumnOutOfRange_Throws()
    {
        var board = FormationBoard.Empty();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => board.OccupantAt(At(SquadRow.Front, 5)));
    }

    // 9 個の相異なる決定的 Guid を払い出す（テスト内専用ヘルパー）。
    private static Guid DeterministicId(int seed)
    {
        var bytes = new byte[16];
        bytes[0] = (byte)(seed + 1);
        return new Guid(bytes);
    }
}
