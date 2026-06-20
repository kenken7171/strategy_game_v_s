// =============================================================================
//  ChronicleKnights.Tests — ProphecyMasterTests.cs
// -----------------------------------------------------------------------------
//  予言生成の本実装（ProphecyMaster）の純粋ロジック単体テスト。固定するのは:
//    1. 構造     : 必ず 3 枚、かつ Kind は相異なる（「3 枚同じ」を構造排除）。
//    2. レア度   : 基本ブロンズ・たまにシルバー・稀にゴールド。戦闘は常に Bronze。
//    3. 効果量   : Kind × Rarity の Value テーブルが単調（上位ほど効率が良い）。
//    4. 暦連動   : 戦乱期（Twilight）は黎明期（Dawn）より Battle が出やすい。
//    5. 表示キー : DescriptionKey は "Kind.Rarity" 形式。
//    6. 決定論   : 同一 turn ＋ 同一 rng 状態なら（Id を除き）同一の 3 枚。
//
//  Godot 非依存の純粋層テスト（開発憲法④：Random は引数注入）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Timeline;
using Xunit;

namespace ChronicleKnights.Tests.Core.Timeline;

public class ProphecyMasterTests
{
    // ─── 1. 構造（3 枚・Kind は相異なる） ─────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    [InlineData(50)]
    [InlineData(99)]
    public void Generate_AlwaysReturnsThreeDistinctKinds(int turn)
    {
        var rng = new Random(turn * 31 + 7);
        for (var trial = 0; trial < 200; trial++)
        {
            var options = ProphecyMaster.Generate(turn, rng);

            Assert.Equal(3, options.Length);
            var kinds = options.Select(o => o.Kind).ToList();
            Assert.Equal(3, kinds.Distinct().Count()); // 相異なる Kind
        }
    }

    // ─── 2. レア度（戦闘は常に Bronze） ───────────────────────────────────────

    [Fact]
    public void Generate_BattleProphecy_IsAlwaysBronze()
    {
        var rng = new Random(2024);
        var battleSeen = 0;
        for (var turn = 1; turn <= 100; turn++)
        {
            foreach (var p in ProphecyMaster.Generate(turn, rng))
            {
                if (p.Kind != ProphecyKind.Battle) continue;
                battleSeen++;
                Assert.Equal(ProphecyRarity.Bronze, p.Rarity);
            }
        }
        Assert.True(battleSeen > 0, "Battle prophecies should appear over 100 years");
    }

    [Fact]
    public void RollRarity_IsBronzeDominant_GoldRarest()
    {
        var rng = new Random(12345);
        var counts = new Dictionary<ProphecyRarity, int>
        {
            [ProphecyRarity.Bronze] = 0,
            [ProphecyRarity.Silver] = 0,
            [ProphecyRarity.Gold] = 0,
        };
        const int n = 20000;
        for (var i = 0; i < n; i++) counts[ProphecyMaster.RollRarity(rng)]++;

        // 基本ブロンズ > たまにシルバー > 稀にゴールド の単調順序。
        Assert.True(counts[ProphecyRarity.Bronze] > counts[ProphecyRarity.Silver]);
        Assert.True(counts[ProphecyRarity.Silver] > counts[ProphecyRarity.Gold]);
        // 設計値（Gold 6% / Silver 22%）の近傍（広めの許容）に収まる。
        Assert.InRange(counts[ProphecyRarity.Gold] / (double)n, 0.03, 0.09);
        Assert.InRange(counts[ProphecyRarity.Silver] / (double)n, 0.17, 0.27);
    }

    // ─── 3. 効果量テーブル（上位ほど効率が良い＝単調増加） ─────────────────────

    [Fact]
    public void ValueTables_AreMonotonicAcrossRarity()
    {
        Assert.True(ProphecyMaster.RewardPointsValue(ProphecyRarity.Bronze)
                  < ProphecyMaster.RewardPointsValue(ProphecyRarity.Silver));
        Assert.True(ProphecyMaster.RewardPointsValue(ProphecyRarity.Silver)
                  < ProphecyMaster.RewardPointsValue(ProphecyRarity.Gold));

        Assert.True(ProphecyMaster.ScoutRewardValue(ProphecyRarity.Bronze)
                  < ProphecyMaster.ScoutRewardValue(ProphecyRarity.Gold));

        Assert.True(ProphecyMaster.RestValue(ProphecyRarity.Bronze)
                  < ProphecyMaster.RestValue(ProphecyRarity.Gold));
    }

    [Theory]
    [InlineData(ProphecyRarity.Bronze, 1, 2)]
    [InlineData(ProphecyRarity.Silver, 3, 3)]
    [InlineData(ProphecyRarity.Gold, 4, 5)]
    public void EquipmentDropLevel_StaysWithinRarityBand(ProphecyRarity rarity, int min, int max)
    {
        var rng = new Random(777);
        for (var i = 0; i < 500; i++)
        {
            var lvl = ProphecyMaster.EquipmentDropLevel(rarity, rng);
            Assert.InRange(lvl, min, max);
        }
    }

    // ─── 4. 暦連動（戦乱期は黎明期より Battle が出やすい） ──────────────────────

    [Fact]
    public void Generate_BattleAppearsMoreInTwilightThanDawn()
    {
        const int samples = 4000;

        var dawnBattleDecks = 0;
        var rngDawn = new Random(99);
        for (var i = 0; i < samples; i++)
        {
            if (ProphecyMaster.Generate(5, rngDawn).Any(p => p.Kind == ProphecyKind.Battle))
                dawnBattleDecks++;
        }

        var twilightBattleDecks = 0;
        var rngTwilight = new Random(99);
        for (var i = 0; i < samples; i++)
        {
            if (ProphecyMaster.Generate(88, rngTwilight).Any(p => p.Kind == ProphecyKind.Battle))
                twilightBattleDecks++;
        }

        Assert.True(twilightBattleDecks > dawnBattleDecks,
            $"twilight={twilightBattleDecks} should exceed dawn={dawnBattleDecks}");
    }

    // ─── 5. 表示キー（DescriptionKey = "Kind.Rarity"） ───────────────────────

    [Fact]
    public void Generate_DescriptionKey_IsKindDotRarity()
    {
        var rng = new Random(31337);
        for (var turn = 1; turn <= 40; turn++)
        {
            foreach (var p in ProphecyMaster.Generate(turn, rng))
            {
                Assert.Equal($"{p.Kind}.{p.Rarity}", p.DescriptionKey);
            }
        }
    }

    // ─── 6. 決定論（同一 turn ＋ 同一 rng 状態なら Id を除き同一） ─────────────

    [Fact]
    public void Generate_IsDeterministic_ExceptForIds()
    {
        var a = ProphecyMaster.Generate(42, new Random(2024));
        var b = ProphecyMaster.Generate(42, new Random(2024));

        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Kind, b[i].Kind);
            Assert.Equal(a[i].Rarity, b[i].Rarity);
            Assert.Equal(a[i].Value, b[i].Value);
            Assert.Equal(a[i].SkipYears, b[i].SkipYears);
            Assert.Equal(a[i].DescriptionKey, b[i].DescriptionKey);
        }
    }

    // ─── SkipYears は既定範囲（2〜4 年）に収まる ──────────────────────────────

    [Fact]
    public void Generate_SkipYears_StayWithinDefaultBand()
    {
        var rng = new Random(555);
        for (var turn = 1; turn <= 100; turn++)
        {
            foreach (var p in ProphecyMaster.Generate(turn, rng))
            {
                Assert.InRange(p.SkipYears, ProphecyMaster.MinSkipYears, ProphecyMaster.MaxSkipYears);
            }
        }
    }
}
