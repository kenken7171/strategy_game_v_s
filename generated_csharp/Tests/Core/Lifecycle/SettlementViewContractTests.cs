// =============================================================================
//  ChronicleKnights.Tests — SettlementViewContractTests.cs
// -----------------------------------------------------------------------------
//  決算画面（SettlementView）が SoT から Push バインドする値の源を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    SettlementView は Godot.Control、LastBattleSpoils / GetAliveUnits / ResolveLastHit /
//    FinalizeBattleSpoils / ApplyBattleSpoils は Godot.Node（ChronicleGlobal）ゆえテスト
//    プロジェクト（Core/** のみコンパイル）から直接は叩けない。決算ライフサイクル
//    （とどめ選択 → 統合台帳確定 → 経済還流、_ExitTree で台帳更地化、SettlementAccepted 購読解除）は
//    論理検証で担保し、本テストは「戦果ラベルの源」と「とどめ候補の母集合」を包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class SettlementViewContractTests
{
    private static Unit MakeUnit(Guid id, JobId job = JobId.IronWallKnight) => new()
    {
        Id           = id,
        Job          = job,
        Age          = 20,
        MaxAge       = 60,
        FirstNameKey = "first-settle",
        LastNameKey  = "last-settle",
    };

    private static ImmutableDictionary<Guid, Unit> Roster(params Unit[] units)
        => units.ToImmutableDictionary(unit => unit.Id);

    [Fact]
    public void SpoilsValue_EmptySpoils_IsZero()
    {
        // 敗北・戦果なし → settlement-view-spoils-value は 0（ApplyBattleSpoils も no-op で経済不変）。
        Assert.False(BattleSpoils.Empty.HasAnySpoils);
        Assert.Equal(0, BattleSpoils.Empty.CalculateEarnedMarriagePoints());
    }

    [Fact]
    public void SpoilsValue_VictoryWithLastHitLevelGain_IsPositive()
    {
        var id     = Guid.NewGuid();
        var before = MakeUnit(id);             // Lv1（開戦時）
        var after  = before.WithLevelUp(out _); // Lv2（とどめ成長後）

        var spoils = BattleSpoils.FromBattle(Roster(before), Roster(after), BattleOutcome.BattalionVictory);

        // 勝利 + とどめ昇級 1 件 → Gross = VictoryBase(5) + LevelGainBonus(2) = 7、Total = 7（正）。
        Assert.True(spoils.HasAnySpoils);
        Assert.Equal(7, spoils.CalculateEarnedMarriagePoints());
    }

    [Fact]
    public void LastHitCandidates_AreAliveSurvivorsOnly()
    {
        var alive = MakeUnit(Guid.NewGuid());
        var dead  = MakeUnit(Guid.NewGuid()).MarkDeadInBattle();
        var roster = new List<Unit> { alive, dead };

        // とどめカード（settlement-view-lasthit-card-{id}）の母集合 = GetAliveUnits = IsAlive 生存者。
        var candidates = roster.Where(unit => unit.IsAlive).ToList();

        Assert.Single(candidates);
        Assert.Equal(alive.Id, candidates[0].Id);
    }
}
