// =============================================================================
//  ChronicleKnights.Tests — MasterDataNameResolverTests.cs
// -----------------------------------------------------------------------------
//  マスターデータ名解決層 (MasterDataNameResolver) の純粋ロジック単体テスト。
//
//  検証観点（ユーザー要件 #3 に対応）:
//    1. 読込       : localization JSON の jobs / items / prophecyKinds セクションを
//                    name / icon サブフィールド単位で辞書へ抽出できる
//    2. 単純解決   : 既知 enum は表示テキストへ、未知 enum は生キー（enum 名）へ
//                    フォールバックする
//    3. アイコン   : prophecyKinds は name と icon を別フィールドとして解決できる
//    4. 堅牢性     : セクション欠落・不正 JSON・null 引数・空文字列を安全に扱う
//
//  ★ テスト規約: 日本語リテラルは用いず、表示文字列も ASCII プレースホルダで
//    与える（PhaseNameResolverTests / NameResolverTests と同じ方針）。enum 名は
//    そのまま JSON キーへ一致させ、解決時の ToString() 経路をテストでも担保する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Localization;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Localization;

public class MasterDataNameResolverTests
{
    // 表示文字列はすべて ASCII プレースホルダ（一意性・経路検証のため接頭辞付き）。
    private const string JobIronWallValue  = "JOB-IRONWALL";
    private const string JobSniperValue    = "JOB-SNIPER";
    private const string ItemSwordValue    = "ITEM-SWORD";
    private const string ItemCoinValue     = "ITEM-COIN";
    private const string ProphRewardName   = "PROPHECY-REWARD-NAME";
    private const string ProphRewardIcon   = "PROPHECY-REWARD-ICON";
    private const string ProphBattleName   = "PROPHECY-BATTLE-NAME";
    private const string ProphBattleIcon   = "PROPHECY-BATTLE-ICON";
    private const string SkillSingleValue  = "SKILL-SINGLE-STRIKE";
    private const string SkillPincerValue  = "SKILL-PINCER";
    private const string SkillSingleKey    = "enemy-skill-single-strike";
    private const string SkillPincerKey    = "enemy-skill-pincer";

    // jobs / items / prophecyKinds を部分的に持つ最小フィクスチャ。
    //   - キーは enum.ToString()（PascalCase）に一致させる。
    //   - 一部 enum 値は意図的に未登録（生キーフォールバックの検証用）。
    //   - "_note"（注釈キー）と非対象セクション ui は読み飛ばされる。
    private static string SampleLocalizationJson() => $$"""
    {
      "jobs": {
        "_note": "test fixture (ascii only)",
        "IronWallKnight": { "name": "{{JobIronWallValue}}" },
        "Sniper":         { "name": "{{JobSniperValue}}" }
      },
      "items": {
        "SwordKnight": { "name": "{{ItemSwordValue}}" },
        "CoinGreed":   { "name": "{{ItemCoinValue}}" }
      },
      "prophecyKinds": {
        "_note": "icon と name を分離して保持する",
        "RewardPoints": { "icon": "{{ProphRewardIcon}}", "name": "{{ProphRewardName}}" },
        "Battle":       { "icon": "{{ProphBattleIcon}}", "name": "{{ProphBattleName}}" }
      },
      "enemySkills": {
        "_note": "敵スキルは enum ではなく平坦な ASCII キーで引く",
        "{{SkillSingleKey}}": { "name": "{{SkillSingleValue}}" },
        "{{SkillPincerKey}}": { "name": "{{SkillPincerValue}}" }
      },
      "ui": { "ignored": "should-not-be-loaded" }
    }
    """;

    private static MasterDataNameResolver SampleResolver() =>
        MasterDataNameResolver.FromLocalizationJson(SampleLocalizationJson());

    // ─── 1. 読込（各セクション・サブフィールドのエントリ数） ───────────────

    [Fact]
    public void FromLocalizationJson_LoadsEachSectionIndependently()
    {
        var resolver = SampleResolver();

        // _note は読み飛ばし、ui は対象外。各 2 件ずつ登録される。
        Assert.Equal(2, resolver.JobNameCount);
        Assert.Equal(2, resolver.ItemNameCount);
        Assert.Equal(2, resolver.ProphecyKindNameCount);
        Assert.Equal(2, resolver.ProphecyKindIconCount);
        Assert.Equal(2, resolver.SkillNameCount);
    }

    // ─── 敵スキル名（平坦な ASCII キーで引く・enum 非依存） ──────────────────

    [Theory]
    [InlineData(SkillSingleKey, SkillSingleValue)]
    [InlineData(SkillPincerKey, SkillPincerValue)]
    public void ResolveSkillName_KnownKey_ReturnsDisplayString(string key, string expected)
    {
        Assert.Equal(expected, SampleResolver().ResolveSkillName(key));
    }

    [Fact]
    public void ResolveSkillName_UnknownKey_FallsBackToRawKey()
    {
        // 未登録キーは生キー（ASCII）をそのまま返す（登録漏れを画面から判別可能に）。
        Assert.Equal(
            "enemy-skill-total-assault",
            SampleResolver().ResolveSkillName("enemy-skill-total-assault"));
    }

    // ─── 2. 単純解決（既知キー） ────────────────────────────────────────────

    [Theory]
    [InlineData(JobId.IronWallKnight, JobIronWallValue)]
    [InlineData(JobId.Sniper, JobSniperValue)]
    public void ResolveJobName_KnownJob_ReturnsDisplayString(JobId job, string expected)
    {
        Assert.Equal(expected, SampleResolver().ResolveJobName(job));
    }

    [Theory]
    [InlineData(ItemId.SwordKnight, ItemSwordValue)]
    [InlineData(ItemId.CoinGreed, ItemCoinValue)]
    public void ResolveItemName_KnownItem_ReturnsDisplayString(ItemId item, string expected)
    {
        Assert.Equal(expected, SampleResolver().ResolveItemName(item));
    }

    [Theory]
    [InlineData(ProphecyKind.RewardPoints, ProphRewardName)]
    [InlineData(ProphecyKind.Battle, ProphBattleName)]
    public void ResolveProphecyKindName_KnownKind_ReturnsDisplayString(
        ProphecyKind kind, string expected)
    {
        Assert.Equal(expected, SampleResolver().ResolveProphecyKindName(kind));
    }

    // ─── 3. アイコン（name とは別フィールド） ──────────────────────────────

    [Theory]
    [InlineData(ProphecyKind.RewardPoints, ProphRewardIcon)]
    [InlineData(ProphecyKind.Battle, ProphBattleIcon)]
    public void ResolveProphecyKindIcon_KnownKind_ReturnsIconString(
        ProphecyKind kind, string expected)
    {
        Assert.Equal(expected, SampleResolver().ResolveProphecyKindIcon(kind));
    }

    [Fact]
    public void ProphecyKind_NameAndIcon_AreIndependentFields()
    {
        var resolver = SampleResolver();

        // 同一 enum でも name と icon は別フィールドから解決され、混線しない。
        Assert.Equal(ProphRewardName, resolver.ResolveProphecyKindName(ProphecyKind.RewardPoints));
        Assert.Equal(ProphRewardIcon, resolver.ResolveProphecyKindIcon(ProphecyKind.RewardPoints));
        Assert.NotEqual(
            resolver.ResolveProphecyKindName(ProphecyKind.RewardPoints),
            resolver.ResolveProphecyKindIcon(ProphecyKind.RewardPoints));
    }

    // ─── 4. フォールバック（未登録 enum → 生キー = enum 名） ───────────────

    [Fact]
    public void ResolveJobName_UnregisteredJob_FallsBackToEnumName()
    {
        // Medic は本フィクスチャ未登録 → enum 名そのものへフォールバック。
        Assert.Equal("Medic", SampleResolver().ResolveJobName(JobId.Medic));
    }

    [Fact]
    public void ResolveItemName_UnregisteredItem_FallsBackToEnumName()
    {
        Assert.Equal("BowSniper", SampleResolver().ResolveItemName(ItemId.BowSniper));
    }

    [Fact]
    public void ResolveProphecyKind_Unregistered_FallsBackToEnumNameForNameAndIcon()
    {
        var resolver = SampleResolver();
        // Rest は未登録 → name / icon ともに enum 名へフォールバック。
        Assert.Equal("Rest", resolver.ResolveProphecyKindName(ProphecyKind.Rest));
        Assert.Equal("Rest", resolver.ResolveProphecyKindIcon(ProphecyKind.Rest));
    }

    // ─── 5. 堅牢性（欠落・不正・null） ─────────────────────────────────────

    [Fact]
    public void FromLocalizationJson_NoTargetSections_YieldsEmptyResolverWithFallback()
    {
        const string json = """{ "phases": { "chronicle": "x" }, "names": {} }""";

        var resolver = MasterDataNameResolver.FromLocalizationJson(json);

        Assert.Equal(0, resolver.JobNameCount);
        Assert.Equal(0, resolver.ItemNameCount);
        Assert.Equal(0, resolver.ProphecyKindNameCount);
        Assert.Equal(0, resolver.ProphecyKindIconCount);

        // 空でも例外を出さず、すべて enum 名へフォールバックする。
        Assert.Equal("IronWallKnight", resolver.ResolveJobName(JobId.IronWallKnight));
        Assert.Equal("SwordKnight", resolver.ResolveItemName(ItemId.SwordKnight));
        Assert.Equal("RewardPoints", resolver.ResolveProphecyKindName(ProphecyKind.RewardPoints));
        Assert.Equal("RewardPoints", resolver.ResolveProphecyKindIcon(ProphecyKind.RewardPoints));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromLocalizationJson_NullOrWhitespace_Throws(string? json)
    {
        Assert.Throws<ArgumentException>(
            () => MasterDataNameResolver.FromLocalizationJson(json!));
    }

    [Fact]
    public void FromLocalizationJson_MalformedJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(
            () => MasterDataNameResolver.FromLocalizationJson("{ this is not valid json "));
    }

    [Fact]
    public void Constructor_NullDictionary_Throws()
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Throws<ArgumentNullException>(
            () => new MasterDataNameResolver(null!, empty, empty, empty));
        Assert.Throws<ArgumentNullException>(
            () => new MasterDataNameResolver(empty, null!, empty, empty));
        Assert.Throws<ArgumentNullException>(
            () => new MasterDataNameResolver(empty, empty, null!, empty));
        Assert.Throws<ArgumentNullException>(
            () => new MasterDataNameResolver(empty, empty, empty, null!));
    }

    [Fact]
    public void Constructor_DirectDictionaries_ResolveEntries()
    {
        var jobNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IronWallKnight"] = JobIronWallValue,
        };
        var itemNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SwordKnight"] = ItemSwordValue,
        };
        var prophecyNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RewardPoints"] = ProphRewardName,
        };
        var prophecyIcons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RewardPoints"] = ProphRewardIcon,
        };

        var resolver = new MasterDataNameResolver(
            jobNames, itemNames, prophecyNames, prophecyIcons);

        Assert.Equal(JobIronWallValue, resolver.ResolveJobName(JobId.IronWallKnight));
        Assert.Equal(ItemSwordValue, resolver.ResolveItemName(ItemId.SwordKnight));
        Assert.Equal(ProphRewardName, resolver.ResolveProphecyKindName(ProphecyKind.RewardPoints));
        Assert.Equal(ProphRewardIcon, resolver.ResolveProphecyKindIcon(ProphecyKind.RewardPoints));

        // 未登録は enum 名へフォールバック。
        Assert.Equal("Scout", resolver.ResolveJobName(JobId.Scout));
    }
}
