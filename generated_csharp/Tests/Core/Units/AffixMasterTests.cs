// =============================================================================
//  ChronicleKnights.Tests — AffixMasterTests.cs
// -----------------------------------------------------------------------------
//  Affix（装備の付加効果）の数値 SoT ＋ 決定論的生成・解決マスターの単体テスト。
//
//  検証観点:
//    - 定義表 All / 逆引き ByKey の整合（3 種別・キー一致）
//    - Resolve（未知キー・null は null・落ちない）
//    - 効果合算（ATK/DEF/SPD の総和・未知キー無視・空は 0）
//    - レベル別の生成個数 AffixCountForLevel
//    - RollAffixKeys の決定論（同一シード → 同一列）・個数・相異性・解決可能性
//    - Equipment 側の派生プロパティ（AffixKeys → Affix{Attack,Defense,Speed}Bonus）
// =============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Units;

public class AffixMasterTests
{
    // ─── 定義表の整合 ─────────────────────────────────────────────────────

    [Fact]
    public void All_HasThreeCombatAffixes_WithMatchingKeys()
    {
        Assert.Equal(3, AffixMaster.All.Count);

        Assert.Equal("affix-sharp", AffixMaster.All[AffixKind.Sharp].Key);
        Assert.Equal("affix-sturdy", AffixMaster.All[AffixKind.Sturdy].Key);
        Assert.Equal("affix-swift", AffixMaster.All[AffixKind.Swift].Key);

        // 各種別は単一の戦闘軸だけを底上げする（MVP の純粋な味付け）。
        Assert.Equal(3, AffixMaster.All[AffixKind.Sharp].AttackBonus);
        Assert.Equal(2, AffixMaster.All[AffixKind.Sturdy].DefenseBonus);
        Assert.Equal(2, AffixMaster.All[AffixKind.Swift].SpeedBonus);
    }

    [Fact]
    public void ByKey_ContainsEveryDefinition()
    {
        foreach (var def in AffixMaster.All.Values)
        {
            Assert.True(AffixMaster.ByKey.ContainsKey(def.Key));
            Assert.Same(def, AffixMaster.ByKey[def.Key]);
        }
    }

    // ─── 解決（キー → 定義） ───────────────────────────────────────────────

    [Theory]
    [InlineData("affix-sharp", AffixKind.Sharp)]
    [InlineData("affix-sturdy", AffixKind.Sturdy)]
    [InlineData("affix-swift", AffixKind.Swift)]
    public void Resolve_KnownKey_ReturnsDefinition(string key, AffixKind expected)
    {
        var def = AffixMaster.Resolve(key);
        Assert.NotNull(def);
        Assert.Equal(expected, def!.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("affix-unknown")]
    public void Resolve_UnknownOrNull_ReturnsNull(string? key)
    {
        Assert.Null(AffixMaster.Resolve(key));
    }

    // ─── 効果合算 ─────────────────────────────────────────────────────────

    [Fact]
    public void Sum_AggregatesEachAxisIndependently()
    {
        var keys = new[] { "affix-sharp", "affix-sturdy", "affix-swift" };

        Assert.Equal(3, AffixMaster.SumAttackBonus(keys));
        Assert.Equal(2, AffixMaster.SumDefenseBonus(keys));
        Assert.Equal(2, AffixMaster.SumSpeedBonus(keys));
    }

    [Fact]
    public void Sum_IgnoresUnknownKeys()
    {
        var keys = new[] { "affix-sharp", "affix-unknown", "affix-sharp" };

        // 既知の affix-sharp が 2 つ → ATK 6。未知キーは 0 として無視。
        Assert.Equal(6, AffixMaster.SumAttackBonus(keys));
        Assert.Equal(0, AffixMaster.SumDefenseBonus(keys));
    }

    [Fact]
    public void Sum_EmptyKeys_IsZero()
    {
        var empty = Array.Empty<string>();
        Assert.Equal(0, AffixMaster.SumAttackBonus(empty));
        Assert.Equal(0, AffixMaster.SumDefenseBonus(empty));
        Assert.Equal(0, AffixMaster.SumSpeedBonus(empty));
    }

    // ─── レベル別生成個数 ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void AffixCountForLevel_IsOneBelowLv3_ThenTwo(int level, int expectedCount)
    {
        Assert.Equal(expectedCount, AffixMaster.AffixCountForLevel(level));
    }

    // ─── 決定論的生成 ─────────────────────────────────────────────────────

    [Fact]
    public void RollAffixKeys_SameSeed_ProducesIdenticalSequence()
    {
        var a = AffixMaster.RollAffixKeys(5, new Random(12345));
        var b = AffixMaster.RollAffixKeys(5, new Random(12345));

        // ImmutableArray の IEquatable は内部配列の参照比較のため、要素列として突合する。
        Assert.Equal(a.ToArray(), b.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void RollAffixKeys_HasLevelCount_DistinctAndResolvable(int level)
    {
        var keys = AffixMaster.RollAffixKeys(level, new Random(7));

        Assert.Equal(AffixMaster.AffixCountForLevel(level), keys.Length);

        // 相異なる（種別が重複しない）。
        Assert.Equal(keys.Length, keys.Distinct().Count());

        // すべて解決可能な既知キー。
        Assert.All(keys, k => Assert.NotNull(AffixMaster.Resolve(k)));
    }

    // ─── Equipment 側の派生（AffixKeys → 戦闘ステ補正） ───────────────────

    [Fact]
    public void Equipment_AffixBonuses_ReflectAffixKeys()
    {
        var equip = new Equipment
        {
            Id = Guid.NewGuid(),
            ItemId = ItemId.SwordKnight,
            Level = 1,
            AffixKeys = ImmutableArray.Create("affix-sharp", "affix-sturdy"),
        };

        Assert.Equal(3, equip.AffixAttackBonus);
        Assert.Equal(2, equip.AffixDefenseBonus);
        Assert.Equal(0, equip.AffixSpeedBonus);
        Assert.True(equip.HasAnyAffix);
    }

    [Fact]
    public void Equipment_NoAffix_AllBonusesZero()
    {
        var equip = new Equipment
        {
            Id = Guid.NewGuid(),
            ItemId = ItemId.BowSniper,
            Level = 3,
        };

        Assert.Equal(0, equip.AffixAttackBonus);
        Assert.Equal(0, equip.AffixDefenseBonus);
        Assert.Equal(0, equip.AffixSpeedBonus);
        Assert.False(equip.HasAnyAffix);
    }
}
