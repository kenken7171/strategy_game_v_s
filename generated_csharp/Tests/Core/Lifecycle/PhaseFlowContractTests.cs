// =============================================================================
//  ChronicleKnights.Tests — PhaseFlowContractTests.cs
// -----------------------------------------------------------------------------
//  新 UI フロー（Hub MARCH → Battle、Settlement ACCEPT → 年送り）が依存する GamePhase 機械の
//  契約を純粋層で固定する。
//
//  ★ なぜ重要か:
//    HubView.AdvancePhaseToBattle は TryAdvanceTo(Next(...)) を辿って Battle まで前進し、
//    SettlementView.OnAcceptPressed は AdvancePhase（Battle→Chronicle）で年送りを成立させる。
//    その正当性は「Next の巡回順」「隣接前進のみ許す CanTransition」「Battle→Chronicle が年送りの辺」に
//    かかっている。ChronicleGlobal（Godot.Node）の駆動は論理検証で担保し、本テストは純粋な
//    GamePhaseFlow の契約を包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class PhaseFlowContractTests
{
    [Fact]
    public void InitialPhase_IsChronicle()
    {
        Assert.Equal(GamePhase.Chronicle, GamePhaseFlow.InitialPhase);
    }

    [Fact]
    public void Next_CyclesChronicleGuildFormationBattleChronicle()
    {
        Assert.Equal(GamePhase.Guild, GamePhaseFlow.Next(GamePhase.Chronicle));
        Assert.Equal(GamePhase.Formation, GamePhaseFlow.Next(GamePhase.Guild));
        Assert.Equal(GamePhase.Battle, GamePhaseFlow.Next(GamePhase.Formation));
        Assert.Equal(GamePhase.Chronicle, GamePhaseFlow.Next(GamePhase.Battle)); // 年送りの辺
    }

    [Fact]
    public void MarchStepping_ReachesBattleFromChronicleInThreeSteps()
    {
        // HubView.AdvancePhaseToBattle と同式: Next を辿り Battle で停止する。
        var phase = GamePhaseFlow.InitialPhase; // Chronicle
        var steps = 0;
        while (phase != GamePhase.Battle && steps < 4)
        {
            phase = GamePhaseFlow.Next(phase);
            steps++;
        }

        Assert.Equal(GamePhase.Battle, phase);
        Assert.Equal(3, steps);
    }

    [Fact]
    public void CanTransition_IsAdjacentForwardOnly()
    {
        Assert.True(GamePhaseFlow.CanTransition(GamePhase.Chronicle, GamePhase.Guild));   // 隣接前進 OK
        Assert.False(GamePhaseFlow.CanTransition(GamePhase.Chronicle, GamePhase.Battle)); // 飛び越し NG
        Assert.False(GamePhaseFlow.CanTransition(GamePhase.Battle, GamePhase.Formation)); // 後退 NG
        Assert.True(GamePhaseFlow.CanTransition(GamePhase.Battle, GamePhase.Chronicle));  // 年送りの辺 OK
    }
}
