// =============================================================================
//  ChronicleKnights.Tests — MetricsLogFormatterTests.cs
// -----------------------------------------------------------------------------
//  実走ログ整形層 (MetricsLogFormatter) の純粋ロジックを固定する。
//
//  検証観点:
//    1. 戦闘開始行 : 年・敵原型(kebab スラッグ)・HP/ATK/SPD が JSON 風 ASCII で厳密一致。
//    2. 決算行     : 勝利の内訳（基礎/昇級/進化/ロスト/合計）と適用後残高が厳密一致。
//    3. 非勝利     : victory:false で全要素 0 を映す。
//    4. ASCII 限定 : 出力に非 ASCII 文字（日本語・絵文字）が 1 文字も混じらない（憲法①）。
//
//  ★ テスト規約: 文字列リテラルは ASCII のみ。MarriagePointsBreakdown は公開不変 record struct
//    ゆえテストから直接組み立て、整形の決定論を 1 ビット単位で固定する。
// =============================================================================

using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class MetricsLogFormatterTests
{
    private static void AssertAsciiOnly(string text)
    {
        Assert.All(text, c => Assert.True(c < 128, $"non-ASCII char detected: {(int)c}"));
    }

    // ─── 1. 戦闘開始行（敵原型は testid と同じ kebab スラッグ） ─────────────

    [Fact]
    public void FormatBattleStart_ProducesExactStructuredAsciiLine()
    {
        var line = MetricsLogFormatter.FormatBattleStart(
            50, EnemyArchetype.UpheavalConqueror, hp: 1000, attack: 50, speed: 30);

        Assert.Equal(
            "[metrics] {\"event\":\"battle-start\",\"year\":50,\"enemy\":\"upheaval-conqueror\","
            + "\"hp\":1000,\"atk\":50,\"spd\":30}",
            line);
        AssertAsciiOnly(line);
    }

    [Theory]
    [InlineData(EnemyArchetype.TrialGuardian, "trial-guardian")]
    [InlineData(EnemyArchetype.DawnWarden, "dawn-warden")]
    [InlineData(EnemyArchetype.EternalSovereign, "eternal-sovereign")]
    public void FormatBattleStart_UsesKebabArchetypeSlug(EnemyArchetype archetype, string slug)
    {
        var line = MetricsLogFormatter.FormatBattleStart(1, archetype, 1, 1, 1);
        Assert.Contains("\"enemy\":\"" + slug + "\"", line);
    }

    // ─── 2. 決算行（勝利の内訳と適用後残高） ───────────────────────────────

    [Fact]
    public void FormatSettlement_Victory_ProducesExactBreakdownLine()
    {
        // 5 + 2*2 + 1*1 - 1*3 = 7。
        var breakdown = new MarriagePointsBreakdown
        {
            IsVictory      = true,
            VictoryBase    = 5,
            LevelGainCount = 2,
            LevelGainRate  = 2,
            EvolutionCount = 1,
            EvolutionRate  = 1,
            LossCount      = 1,
            LossRate       = 3,
        };
        Assert.Equal(7, breakdown.Total); // 整形対象の前提を固定。

        var line = MetricsLogFormatter.FormatSettlement(25, breakdown, balanceAfter: 42);

        Assert.Equal(
            "[metrics] {\"event\":\"battle-settlement\",\"year\":25,\"victory\":true,"
            + "\"base\":5,\"levelgain_bonus\":4,\"evolution_bonus\":1,\"loss_penalty\":3,"
            + "\"earned_total\":7,\"balance_after\":42}",
            line);
        AssertAsciiOnly(line);
    }

    // ─── 3. 非勝利（victory:false・全要素 0） ──────────────────────────────

    [Fact]
    public void FormatSettlement_NonVictory_ReportsZeroEarned()
    {
        var breakdown = new MarriagePointsBreakdown
        {
            IsVictory      = false,
            VictoryBase    = 0,
            LevelGainCount = 0,
            LevelGainRate  = 2,
            EvolutionCount = 0,
            EvolutionRate  = 1,
            LossCount      = 0,
            LossRate       = 3,
        };

        var line = MetricsLogFormatter.FormatSettlement(80, breakdown, balanceAfter: 12);

        Assert.Contains("\"victory\":false", line);
        Assert.Contains("\"earned_total\":0", line);
        Assert.Contains("\"balance_after\":12", line);
        AssertAsciiOnly(line);
    }
}
