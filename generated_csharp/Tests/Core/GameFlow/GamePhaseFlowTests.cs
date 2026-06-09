// =============================================================================
//  ChronicleKnights.Tests — GamePhaseFlowTests.cs
// -----------------------------------------------------------------------------
//  ゲームフェーズ状態マシン (GamePhaseFlow) の純粋ロジック単体テスト。
//
//  検証観点（ユーザー要件 #4-b に対応）:
//    1. 正規順序   : Chronicle → Guild → Formation → Battle → Chronicle と循環する
//    2. 一方通行   : 合法なのは「ちょうど次のフェーズ」へ進む場合のみ
//    3. 飛び越し禁止: Chronicle から直接 Battle 等のジャンプはガードされる
//    4. 後退禁止   : Guild → Chronicle 等の巻き戻しはガードされる
//    5. 自己遷移禁止: from == to は不正
//    6. 網羅性     : 全 (from, to) 組で CanTransition == (Next(from) == to)
//
//  ChronicleGlobal は Godot.Node 継承のため本テストプロジェクトには取り込まれない。
//  状態マシンの判断ロジックは純粋な GamePhaseFlow に集約されており、ここを検証すれば
//  常駐ノードの遷移可否（不正な飛び越しの拒否）も論理的に保証される。
// =============================================================================

using System.Linq;
using ChronicleKnights.Core.GameFlow;
using Xunit;

namespace ChronicleKnights.Tests.Core.GameFlow;

public class GamePhaseFlowTests
{
    // ─── 1. 正規順序（循環） ───────────────────────────────────────────────

    [Fact]
    public void InitialPhase_IsChronicle()
    {
        Assert.Equal(GamePhase.Chronicle, GamePhaseFlow.InitialPhase);
    }

    [Fact]
    public void Cycle_HasFourPhasesInCanonicalOrder()
    {
        Assert.Equal(4, GamePhaseFlow.Cycle.Length);
        Assert.Equal(
            new[] { GamePhase.Chronicle, GamePhase.Guild, GamePhase.Formation, GamePhase.Battle },
            GamePhaseFlow.Cycle.ToArray());
    }

    [Theory]
    [InlineData(GamePhase.Chronicle, GamePhase.Guild)]
    [InlineData(GamePhase.Guild, GamePhase.Formation)]
    [InlineData(GamePhase.Formation, GamePhase.Battle)]
    [InlineData(GamePhase.Battle, GamePhase.Chronicle)] // Battle の次は次世代の Chronicle
    public void Next_FollowsCanonicalCycle(GamePhase current, GamePhase expectedNext)
    {
        Assert.Equal(expectedNext, GamePhaseFlow.Next(current));
    }

    [Fact]
    public void Next_AppliedFourTimes_ReturnsToStart()
    {
        var phase = GamePhaseFlow.InitialPhase;
        for (var i = 0; i < 4; i++)
        {
            phase = GamePhaseFlow.Next(phase);
        }

        // 4 回進めば 1 周して開始フェーズへ戻る。
        Assert.Equal(GamePhaseFlow.InitialPhase, phase);
    }

    // ─── 2. 一方通行（合法遷移） ───────────────────────────────────────────

    [Theory]
    [InlineData(GamePhase.Chronicle, GamePhase.Guild)]
    [InlineData(GamePhase.Guild, GamePhase.Formation)]
    [InlineData(GamePhase.Formation, GamePhase.Battle)]
    [InlineData(GamePhase.Battle, GamePhase.Chronicle)]
    public void CanTransition_AllowsExactNextPhase(GamePhase from, GamePhase to)
    {
        Assert.True(GamePhaseFlow.CanTransition(from, to));
    }

    // ─── 3. 飛び越し禁止（不正ジャンプのガード） ────────────────────────────

    [Theory]
    [InlineData(GamePhase.Chronicle, GamePhase.Formation)]
    [InlineData(GamePhase.Chronicle, GamePhase.Battle)]   // ★ 代表的な不正ジャンプ
    [InlineData(GamePhase.Guild, GamePhase.Battle)]
    [InlineData(GamePhase.Guild, GamePhase.Chronicle)]    // ★ 後退も不正
    public void CanTransition_BlocksIllegalJumps(GamePhase from, GamePhase to)
    {
        Assert.False(GamePhaseFlow.CanTransition(from, to));
    }

    // ─── 4. 後退禁止 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(GamePhase.Formation, GamePhase.Guild)]
    [InlineData(GamePhase.Battle, GamePhase.Formation)]
    [InlineData(GamePhase.Battle, GamePhase.Guild)]
    public void CanTransition_BlocksBackwardMoves(GamePhase from, GamePhase to)
    {
        Assert.False(GamePhaseFlow.CanTransition(from, to));
    }

    // ─── 5. 自己遷移禁止 ───────────────────────────────────────────────────

    [Theory]
    [InlineData(GamePhase.Chronicle)]
    [InlineData(GamePhase.Guild)]
    [InlineData(GamePhase.Formation)]
    [InlineData(GamePhase.Battle)]
    public void CanTransition_BlocksSelfTransition(GamePhase phase)
    {
        Assert.False(GamePhaseFlow.CanTransition(phase, phase));
    }

    // ─── 6. 網羅性（全組合せで規約と一致） ─────────────────────────────────

    [Fact]
    public void CanTransition_IsConsistentWithNext_ForAllPairs()
    {
        foreach (var from in GamePhaseFlow.Cycle)
        {
            foreach (var to in GamePhaseFlow.Cycle)
            {
                var expected = GamePhaseFlow.Next(from) == to;
                Assert.Equal(expected, GamePhaseFlow.CanTransition(from, to));
            }
        }
    }

    // 各フェーズから合法に進める先はちょうど 1 つだけ（一方通行の本質）。
    [Theory]
    [InlineData(GamePhase.Chronicle)]
    [InlineData(GamePhase.Guild)]
    [InlineData(GamePhase.Formation)]
    [InlineData(GamePhase.Battle)]
    public void EachPhase_HasExactlyOneLegalSuccessor(GamePhase from)
    {
        var legalTargets = GamePhaseFlow.Cycle.Count(to => GamePhaseFlow.CanTransition(from, to));
        Assert.Equal(1, legalTargets);
    }

    // ─── スラッグ（ローカライズキー組み立て用） ────────────────────────────

    [Theory]
    [InlineData(GamePhase.Chronicle, "chronicle")]
    [InlineData(GamePhase.Guild, "guild")]
    [InlineData(GamePhase.Formation, "formation")]
    [InlineData(GamePhase.Battle, "battle")]
    public void Slug_ReturnsAsciiKey(GamePhase phase, string expected)
    {
        Assert.Equal(expected, phase.Slug());
    }
}
