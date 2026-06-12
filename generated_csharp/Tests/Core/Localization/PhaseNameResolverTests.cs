// =============================================================================
//  ChronicleKnights.Tests — PhaseNameResolverTests.cs
// -----------------------------------------------------------------------------
//  フェーズ名解決層 (PhaseNameResolver) の純粋ロジック単体テスト。
//
//  検証観点（ユーザー要件 #3 に対応）:
//    1. 読込       : localization JSON の phases セクションを辞書へ統合できる
//    2. 単純解決   : 既知スラッグは表示名へ、未知スラッグは生スラッグへフォールバック
//    3. 列挙連携   : GamePhase オーバーロードが Slug() 経由で正しく解決する
//    4. 規約一致   : 全 GamePhase で Resolve(phase) == Resolve(phase.Slug())
//    5. 堅牢性     : phases 欠落・不正 JSON・null 引数を安全に扱う
//
//  ★ テスト規約: 日本語リテラルは用いず、表示文字列も ASCII プレースホルダで
//    与える（NameResolverTests / GamePhaseFlowTests と同じ方針）。スラッグは
//    GamePhase.Slug() に結合させ、localization 側キーとの一致をテストでも担保する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Localization;
using Xunit;

namespace ChronicleKnights.Tests.Core.Localization;

public class PhaseNameResolverTests
{
    // 表示文字列はすべて ASCII プレースホルダ（順序・一意性検証のため接頭辞付き）。
    private const string ChronicleValue = "PHASE-0-CHRONICLE";
    private const string GuildValue     = "PHASE-1-GUILD";
    private const string FormationValue = "PHASE-2-FORMATION";
    private const string BattleValue    = "PHASE-3-BATTLE";

    // phases セクションを持つ最小の localization JSON フィクスチャ。
    // キーは GamePhase.Slug() が払い出す ASCII スラッグと一致させる。
    // "_note" は注釈キーとして読み飛ばされる（エントリ数に数えない）。
    private static string SampleLocalizationJson() => $$"""
    {
      "phases": {
        "_note": "test fixture (ascii only)",
        "chronicle": "{{ChronicleValue}}",
        "guild": "{{GuildValue}}",
        "formation": "{{FormationValue}}",
        "battle": "{{BattleValue}}"
      },
      "ui": { "ignored": "should-not-be-loaded" }
    }
    """;

    private static PhaseNameResolver SampleResolver() =>
        PhaseNameResolver.FromLocalizationJson(SampleLocalizationJson());

    // ─── 1. 読込 ───────────────────────────────────────────────────────────

    [Fact]
    public void FromLocalizationJson_LoadsPhasesSection()
    {
        var resolver = SampleResolver();

        // chronicle/guild/formation/battle = 4。_note は読み飛ばし、ui は対象外。
        Assert.Equal(4, resolver.EntryCount);
    }

    // ─── 2. 単純解決 / フォールバック ──────────────────────────────────────

    [Theory]
    [InlineData("chronicle", ChronicleValue)]
    [InlineData("guild", GuildValue)]
    [InlineData("formation", FormationValue)]
    [InlineData("battle", BattleValue)]
    public void Resolve_KnownSlug_ReturnsDisplayString(string slug, string expected)
    {
        Assert.Equal(expected, SampleResolver().Resolve(slug));
    }

    [Fact]
    public void Resolve_UnknownSlug_FallsBackToRawSlug()
    {
        const string missing = "epilogue";
        Assert.Equal(missing, SampleResolver().Resolve(missing));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_NullOrEmpty_ReturnsEmpty(string? slug)
    {
        Assert.Equal(string.Empty, SampleResolver().Resolve(slug));
    }

    // ─── 3. GamePhase オーバーロード（Slug 経由） ─────────────────────────

    [Theory]
    [InlineData(GamePhase.Chronicle, ChronicleValue)]
    [InlineData(GamePhase.Guild, GuildValue)]
    [InlineData(GamePhase.Formation, FormationValue)]
    [InlineData(GamePhase.Battle, BattleValue)]
    public void Resolve_GamePhaseOverload_MapsToDisplayName(GamePhase phase, string expected)
    {
        Assert.Equal(expected, SampleResolver().Resolve(phase));
    }

    // ─── 4. 規約一致（全フェーズで slug 経路と一致） ───────────────────────

    [Fact]
    public void Resolve_GamePhaseOverload_IsConsistentWithSlugForAllPhases()
    {
        var resolver = SampleResolver();
        foreach (var phase in GamePhaseFlow.Cycle)
        {
            Assert.Equal(resolver.Resolve(phase.Slug()), resolver.Resolve(phase));
        }
    }

    [Fact]
    public void Resolve_AllCanonicalPhases_HaveDistinctNonEmptyNames()
    {
        var resolver = SampleResolver();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phase in GamePhaseFlow.Cycle)
        {
            var name = resolver.Resolve(phase);
            Assert.False(string.IsNullOrEmpty(name));
            Assert.True(seen.Add(name), $"フェーズ名が重複しました: {name}");
        }
    }

    // ─── 5. 堅牢性 ─────────────────────────────────────────────────────────

    [Fact]
    public void FromLocalizationJson_NoPhasesSection_YieldsEmptyResolver()
    {
        const string json = """{ "ui": { "title": "x" }, "names": {} }""";

        var resolver = PhaseNameResolver.FromLocalizationJson(json);

        Assert.Equal(0, resolver.EntryCount);
        // 空でも例外を出さず、各フェーズは生スラッグへフォールバックする。
        foreach (var phase in GamePhaseFlow.Cycle)
        {
            Assert.Equal(phase.Slug(), resolver.Resolve(phase));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromLocalizationJson_NullOrWhitespace_Throws(string? json)
    {
        Assert.Throws<ArgumentException>(() => PhaseNameResolver.FromLocalizationJson(json!));
    }

    [Fact]
    public void FromLocalizationJson_MalformedJson_ThrowsJsonException()
    {
        // System.Text.Json throws JsonReaderException (a JsonException subclass) for
        // malformed input; ThrowsAny accepts the base type and any derived parse error.
        Assert.ThrowsAny<JsonException>(
            () => PhaseNameResolver.FromLocalizationJson("{ this is not valid json "));
    }

    [Fact]
    public void Constructor_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PhaseNameResolver(null!));
    }

    [Fact]
    public void Constructor_DirectDictionary_ResolvesEntries()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chronicle"] = ChronicleValue,
            ["battle"]    = BattleValue,
        };

        var resolver = new PhaseNameResolver(entries);

        Assert.Equal(2, resolver.EntryCount);
        Assert.Equal(ChronicleValue, resolver.Resolve(GamePhase.Chronicle));
        Assert.Equal(BattleValue, resolver.Resolve(GamePhase.Battle));
        // 辞書未登録のフェーズは生スラッグへフォールバック。
        Assert.Equal(GamePhase.Guild.Slug(), resolver.Resolve(GamePhase.Guild));
    }
}
