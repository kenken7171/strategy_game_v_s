// =============================================================================
//  ChronicleKnights.Tests — SaveSerializerTests.cs
// -----------------------------------------------------------------------------
//  セーブ純粋変換層 (SaveSerializer) の Serialize → Deserialize ラウンドトリップ
//  等価性テスト。「保存して読み戻しても情報が一切欠落しない」ことを証明する。
//
//  ★ 比較方針 — フィールド単位の照合:
//    Unit / Equipment / TimelineEngine は ImmutableArray / ImmutableDictionary 等の
//    コレクションメンバーを含む。C# の record 自動生成 Equals はコレクションを
//    参照等価で比較するため、レコード全体の値等価は信頼できない。よって本テストは
//    スカラーは値で、コレクションは要素単位で 1 つずつ突き合わせる。
//    （例外: PointsEconomy / Prophecy はスカラー専用なので値等価でも可。）
// =============================================================================

using System;
using System.Linq;
using ChronicleKnights.Core.Persistence;
using ChronicleKnights.Core.Units;
using ChronicleKnights.Tests.TestSupport;
using Xunit;

namespace ChronicleKnights.Tests.Core.Persistence;

public class SaveSerializerTests
{
    // ─── ラウンドトリップ: 経済 ────────────────────────────────────────────

    [Fact]
    public void Roundtrip_PreservesEconomy()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        // PointsEconomy はスカラー専用 record なので値等価で照合できる
        Assert.Equal(state.Economy, loaded!.Economy);
    }

    // ─── ラウンドトリップ: タイムライン ────────────────────────────────────

    [Fact]
    public void Roundtrip_PreservesTimeline()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        var before = state.Timeline;
        var after = loaded!.Timeline;

        Assert.Equal(before.Turn, after.Turn);
        Assert.Equal(before.CurrentOptions.Length, after.CurrentOptions.Length);

        for (var i = 0; i < before.CurrentOptions.Length; i++)
        {
            var b = before.CurrentOptions[i];
            var a = after.CurrentOptions[i];
            // Prophecy はスカラー専用 record なので値等価で全フィールド照合できる
            Assert.Equal(b, a);
        }
    }

    // ─── ラウンドトリップ: ロスター（フィールド単位の厳密照合） ─────────────

    [Fact]
    public void Roundtrip_PreservesRoster_FieldByField()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(state.Roster.Count, loaded!.Roster.Count);

        for (var i = 0; i < state.Roster.Count; i++)
        {
            AssertUnitEqual(state.Roster[i], loaded.Roster[i]);
        }
    }

    [Fact]
    public void Roundtrip_PreservesEquippedUnit_WithAffinity()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        var ironWall = loaded!.Roster.Single(u => u.Id == SampleData.IronWallUnitId);

        // 装備が完全に復元されている
        Assert.NotNull(ironWall.MainEquipment);
        Assert.Equal(SampleData.IronWallEquipmentId, ironWall.MainEquipment!.Id);
        Assert.Equal(ItemId.SwordKnight, ironWall.MainEquipment.ItemId);
        Assert.Equal(3, ironWall.MainEquipment.Level);
        Assert.Equal(
            new[] { "affix-sample-guard", "affix-sample-speed" },
            ironWall.MainEquipment.AffixKeys.ToArray());

        // 好感度（Guid キー辞書）が完全に復元されている
        Assert.Single(ironWall.BattleAffinity);
        Assert.Equal(
            SampleData.IronWallAffinityValue,
            ironWall.BattleAffinity[SampleData.AffinityTargetId]);
    }

    [Fact]
    public void Roundtrip_PreservesUnequippedUnit_WithEmptyAffinity()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        var sniper = loaded!.Roster.Single(u => u.Id == SampleData.SniperUnitId);

        Assert.Null(sniper.MainEquipment);   // 装備なしが null のまま復元
        Assert.Empty(sniper.BattleAffinity);  // 好感度なしが空のまま復元
    }

    // ─── enum は文字列で保存（並べ替え耐性の確認） ─────────────────────────

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster);

        // 数値ではなく文字列で保存されていること（JsonStringEnumConverter）。
        Assert.Contains("IronWallKnight", json);
        Assert.Contains("Sniper", json);
        Assert.Contains("SwordKnight", json);
    }

    // ─── 破損耐性: 不正入力は null（例外を投げない） ───────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not valid json ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    public void Deserialize_InvalidJson_ReturnsNull(string json)
    {
        Assert.Null(SaveSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_MissingEconomySection_ReturnsNull()
    {
        // Timeline はあるが Economy が欠落している半端なデータ → null。
        const string json = "{\"Version\":1,\"Timeline\":{\"Turn\":1,\"CurrentOptions\":[]}}";

        Assert.Null(SaveSerializer.Deserialize(json));
    }

    // ─── 引数 null ガード ─────────────────────────────────────────────────

    [Fact]
    public void Serialize_NullArguments_Throw()
    {
        var state = SampleData.BuildState();

        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(null!, state.Timeline, state.Roster));
        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(state.Economy, null!, state.Roster));
        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(state.Economy, state.Timeline, null!));
    }

    // ─── ヘルパー: Unit のフィールド単位照合 ───────────────────────────────

    private static void AssertUnitEqual(Unit expected, Unit actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Job, actual.Job);
        Assert.Equal(expected.Age, actual.Age);
        Assert.Equal(expected.MaxAge, actual.MaxAge);
        Assert.Equal(expected.FirstNameKey, actual.FirstNameKey);
        Assert.Equal(expected.LastNameKey, actual.LastNameKey);
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.Level, actual.Level);
        Assert.Equal(expected.IsDead, actual.IsDead);

        // 装備（null 同士、または個体フィールド一致）
        if (expected.MainEquipment is null)
        {
            Assert.Null(actual.MainEquipment);
        }
        else
        {
            Assert.NotNull(actual.MainEquipment);
            Assert.Equal(expected.MainEquipment.Id, actual.MainEquipment!.Id);
            Assert.Equal(expected.MainEquipment.ItemId, actual.MainEquipment.ItemId);
            Assert.Equal(expected.MainEquipment.Level, actual.MainEquipment.Level);
            Assert.Equal(
                expected.MainEquipment.AffixKeys.ToArray(),
                actual.MainEquipment.AffixKeys.ToArray());
        }

        // 好感度（Guid キー辞書）を要素単位で照合
        Assert.Equal(expected.BattleAffinity.Count, actual.BattleAffinity.Count);
        foreach (var kv in expected.BattleAffinity)
        {
            Assert.True(actual.BattleAffinity.TryGetValue(kv.Key, out var v));
            Assert.Equal(kv.Value, v);
        }
    }
}
