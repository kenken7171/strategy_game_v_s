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
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Job;
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

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        // PointsEconomy はスカラー専用 record なので値等価で照合できる
        Assert.Equal(state.Economy, loaded!.Economy);
    }

    // ─── ラウンドトリップ: 持ち物（インベントリ・v5） ──────────────────────

    [Fact]
    public void Roundtrip_PreservesInventory_WithAffixes()
    {
        var state = SampleData.BuildState();
        var inventory = ImmutableList.Create(
            new Equipment
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                ItemId = ItemId.BowSniper,
                Level = 4,
                AffixKeys = ImmutableArray.Create("affix-sharp", "affix-swift"),
            },
            new Equipment
            {
                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                ItemId = ItemId.StaffMage,
                Level = 1,
            });

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog, inventory);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(inventory.Count, loaded!.Inventory.Count);
        for (var i = 0; i < inventory.Count; i++)
        {
            Assert.Equal(inventory[i].Id, loaded.Inventory[i].Id);
            Assert.Equal(inventory[i].ItemId, loaded.Inventory[i].ItemId);
            Assert.Equal(inventory[i].Level, loaded.Inventory[i].Level);
            Assert.Equal(inventory[i].AffixKeys.ToArray(), loaded.Inventory[i].AffixKeys.ToArray());
        }
    }

    [Fact]
    public void Roundtrip_DefaultInventory_IsEmpty_BackwardCompatible()
    {
        var state = SampleData.BuildState();

        // 旧シグネチャ（inventory 省略）で保存 → 持ち物欠落セーブ相当。
        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Inventory); // 欠落は空リストで後方互換復元。
    }

    // ─── ラウンドトリップ: タイムライン ────────────────────────────────────

    [Fact]
    public void Roundtrip_PreservesTimeline()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
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

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
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

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
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

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
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

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);

        // 数値ではなく文字列で保存されていること（JsonStringEnumConverter）。
        Assert.Contains("IronWallKnight", json);
        Assert.Contains("Sniper", json);
        Assert.Contains("SwordKnight", json);
    }

    // ─── ラウンドトリップ: 旅団史（年代記ナレーション素材） ─────────────────

    [Fact]
    public void Roundtrip_PreservesChronicleLog_FieldByField()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(state.ChronicleLog.Length, loaded!.ChronicleLog.Length);

        for (var i = 0; i < state.ChronicleLog.Length; i++)
        {
            AssertChronicleEntryEqual(state.ChronicleLog[i], loaded.ChronicleLog[i]);
        }
    }

    [Fact]
    public void Roundtrip_PreservesDismissalEntry_AgeAndFinalLevel()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        var dismissal = loaded!.ChronicleLog.Single(
            e => e.Kind == ChronicleEventKind.Dismissed);

        // 解雇は Age（解雇時年齢）と FromLevel（解雇時の最終 Lv）の双方を運ぶ。
        Assert.Equal(JobId.Sniper, dismissal.Job);
        Assert.Equal(41, dismissal.Age);
        Assert.Equal(3, dismissal.FromLevel);
        Assert.Equal(4, dismissal.Generation);
    }

    [Fact]
    public void Serialize_WritesChronicleLogKinds_AsStrings()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster, state.ChronicleLog);

        // enum Kind は数値でなく文字列で保存（並べ替え耐性）。
        Assert.Contains("LevelGained", json);
        Assert.Contains("Dismissed", json);
    }

    [Fact]
    public void Serialize_EmptyChronicleLog_RoundtripsToEmpty()
    {
        var state = SampleData.BuildState();

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, state.Roster,
            ImmutableArray<ChronicleLogEntry>.Empty);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.ChronicleLog);
    }

    // ─── 後方互換: 旧 v1 セーブ（ChronicleLog 欠落）は空配列で復元 ───────────

    [Fact]
    public void Deserialize_LegacyV1Save_WithoutChronicleLog_YieldsEmptyLog()
    {
        // ChronicleLog セクションを持たない旧 v1 セーブを模した JSON。
        const string legacyJson =
            "{\"Version\":1," +
            "\"Economy\":{\"CurrentBalance\":7,\"TotalEarned\":7,\"TotalSpent\":0}," +
            "\"Timeline\":{\"Turn\":1,\"CurrentOptions\":[]}," +
            "\"Roster\":[]}";

        var loaded = SaveSerializer.Deserialize(legacyJson);

        Assert.NotNull(loaded);
        // 経済は正しくパースされ、欠落した旅団史は空配列で安全に復元される。
        Assert.Equal(7, loaded!.Economy.CurrentBalance);
        Assert.Empty(loaded.ChronicleLog);
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
            () => SaveSerializer.Serialize(null!, state.Timeline, state.Roster, state.ChronicleLog));
        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(state.Economy, null!, state.Roster, state.ChronicleLog));
        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(state.Economy, state.Timeline, null!, state.ChronicleLog));
        Assert.Throws<ArgumentNullException>(
            () => SaveSerializer.Serialize(state.Economy, state.Timeline, state.Roster, null!));
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
        Assert.Equal(expected.Gender, actual.Gender);
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

    // ─── ヘルパー: ChronicleLogEntry のフィールド単位照合 ─────────────────────

    private static void AssertChronicleEntryEqual(ChronicleLogEntry expected, ChronicleLogEntry actual)
    {
        // ChronicleLogEntry はスカラー専用 record（コレクションを持たない）ため値等価でも
        // 照合できるが、欠落フィールドの取りこぼしを明示検出するため 1 つずつ突き合わせる。
        Assert.Equal(expected.Generation, actual.Generation);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.UnitFirstNameKey, actual.UnitFirstNameKey);
        Assert.Equal(expected.UnitLastNameKey, actual.UnitLastNameKey);
        Assert.Equal(expected.Job, actual.Job);
        Assert.Equal(expected.Age, actual.Age);
        Assert.Equal(expected.FromLevel, actual.FromLevel);
        Assert.Equal(expected.ToLevel, actual.ToLevel);
    }
}
