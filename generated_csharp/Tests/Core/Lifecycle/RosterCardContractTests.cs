// =============================================================================
//  ChronicleKnights.Tests — RosterCardContractTests.cs
// -----------------------------------------------------------------------------
//  拠点の現役旅団員カード（HubView ロスター）が GetAliveUnits から動的生成する際の
//  「カードの母集合」と「カードに載る ASCII ラベルの源」を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    HubView は Godot.Control、GetAliveUnits / RosterChanged は Godot.Node（ChronicleGlobal）
//    ゆえテストプロジェクト（Core/** のみコンパイル）から直接は叩けない。ロスター再描画の
//    ライフサイクル（RosterChanged 購読 → 台帳更地化して張り替え、_ExitTree で完全解除、
//    _rosterNodes の全 QueueFree）は論理検証で担保し、本テストは「どの旅団員がカード化されるか」
//    （= GetAliveUnits と同じ IsAlive 述語）と「カードに載るラベルが ASCII か」を包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class RosterCardContractTests
{
    private static Unit MakeUnit(bool isDead = false, int age = 20, int maxAge = 60, JobId job = JobId.Scout)
    {
        var unit = new Unit
        {
            Id           = Guid.NewGuid(),
            Job          = job,
            Age          = age,
            MaxAge       = maxAge,
            FirstNameKey = "name-sample-roster",
            LastNameKey  = "name-family-sample",
        };
        return isDead ? unit.MarkDeadInBattle() : unit;
    }

    [Fact]
    public void RosterCards_RenderOnlyAliveUnits_ExcludingDeadAndElders()
    {
        var alive = MakeUnit();
        var dead  = MakeUnit(isDead: true);            // IsDead → IsAlive false
        var elder = MakeUnit(age: 60, maxAge: 60);     // HasReachedMaxAge → IsAlive false

        var roster = new List<Unit> { alive, dead, elder };

        // 旅団員カードの母集合 = GetAliveUnits と同じ生存述語（IsAlive）でフィルタした集合。
        var carded = roster.Where(unit => unit.IsAlive).ToList();

        Assert.Single(carded);
        Assert.Equal(alive.Id, carded[0].Id);
    }

    [Fact]
    public void RosterCard_LabelSources_AreAscii()
    {
        var unit = MakeUnit(job: JobId.IronWallKnight);

        // カードに載るテキストの源（憲法①: ASCII）。ジョブは enum 名、レベルは既定 LV1。
        Assert.Equal("IronWallKnight", unit.Job.ToString());
        Assert.Equal(Unit.InitialLevel, unit.Level);
        Assert.True(unit.IsAlive);
    }

    [Fact]
    public void EmptyRoster_ProducesNoCards()
    {
        var roster = new List<Unit>();

        Assert.Empty(roster.Where(unit => unit.IsAlive));
    }
}
