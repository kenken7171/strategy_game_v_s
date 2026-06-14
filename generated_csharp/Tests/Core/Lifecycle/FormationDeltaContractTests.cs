// =============================================================================
//  ChronicleKnights.Tests — FormationDeltaContractTests.cs
// -----------------------------------------------------------------------------
//  HubView の ▲（デルタ）陣形（前衛1 / 中衛3 / 後衛5）が 3x3 盤面の 9 座標へ過不足なく写る契約を
//  純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    HubView / FormationSlotControl は Godot.Control ゆえテストプロジェクト（Core/** のみコンパイル）から
//    直接は叩けない。ドラッグ＆ドロップのライフサイクル（_GetDragData/_CanDropData/_DropData → SoT 書込み →
//    FormationChanged → RenderFormation の台帳更地化）は論理検証で担保し、本テストは「▲ 9 スロットが
//    盤面 9 座標の完全な置換であること」と「▲ 行 → SquadRow（0:前衛=Front / 1:中衛=RearLeft / 2:後衛=RearRight）」を
//    包囲する（HubView の不変マップと同一定義）。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class FormationDeltaContractTests
{
    // HubView の VanguardCoordinates / CenterCoordinates / RearCoordinates と同一の不変マップ。
    private static readonly SlotCoordinate[] Vanguard =
    {
        new(SquadRow.Front, 0),
    };

    private static readonly SlotCoordinate[] Center =
    {
        new(SquadRow.RearLeft, 0), new(SquadRow.RearLeft, 1), new(SquadRow.RearLeft, 2),
    };

    private static readonly SlotCoordinate[] Rear =
    {
        new(SquadRow.RearRight, 0), new(SquadRow.RearRight, 1), new(SquadRow.RearRight, 2),
        new(SquadRow.Front, 1), new(SquadRow.Front, 2),
    };

    [Fact]
    public void DeltaRows_AreOneThreeFive_SummingToBoardCapacity()
    {
        Assert.Single(Vanguard);
        Assert.Equal(3, Center.Length);
        Assert.Equal(5, Rear.Length);
        Assert.Equal(FormationBoard.SlotCount, Vanguard.Length + Center.Length + Rear.Length); // 9
    }

    [Fact]
    public void DeltaSlots_CoverEveryBoardCoordinate_WithoutDuplication()
    {
        var all = Vanguard.Concat(Center).Concat(Rear).ToList();

        Assert.Equal(FormationBoard.SlotCount, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count()); // 重複なし

        var board = new HashSet<SlotCoordinate>();
        foreach (var row in FormationBoard.RowOrder)
        {
            for (var column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                board.Add(new SlotCoordinate(row, column));
            }
        }

        Assert.True(board.SetEquals(all)); // 盤面 9 座標を過不足なく覆う完全な置換。
    }

    [Fact]
    public void DeltaRowToSquadRow_MapsVanguardFront_CenterRearLeft_RearRearRight()
    {
        Assert.All(Vanguard, coord => Assert.Equal(SquadRow.Front, coord.Row));   // 前衛 = Front (row 0)
        Assert.All(Center, coord => Assert.Equal(SquadRow.RearLeft, coord.Row));  // 中衛 = RearLeft (row 1)

        // 後衛 = RearRight (row 2) を主体（3 枠）とし、5 を 3 列盤面へ収めるための溢れ 2 枠のみ Front 余席。
        Assert.Equal(3, Rear.Count(coord => coord.Row == SquadRow.RearRight));
        Assert.Equal(2, Rear.Count(coord => coord.Row == SquadRow.Front));
    }
}
