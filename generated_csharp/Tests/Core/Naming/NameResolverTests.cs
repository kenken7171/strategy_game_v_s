// =============================================================================
//  ChronicleKnights.Tests — NameResolverTests.cs
// -----------------------------------------------------------------------------
//  名前解決層 (NameResolver) の純粋ロジック単体テスト。
//
//  検証観点（ユーザー要件 #4-a に対応）:
//    1. 読込       : localization JSON の names セクション（firstNames /
//                    familyNames / titles）を 1 つの辞書へ統合できる
//    2. 単純解決   : 既知キーは表示文字列へ、未知キーは生キーへフォールバック
//    3. 複合キー   : '@' 連結キーを自動分解し「称号 ＋ 名前」を連結する
//    4. 氏名連結   : 「称号 ＋ 名前 ＋（あれば）姓」を期待通りの順序で連結する
//    5. 堅牢性     : names 欠落・不正 JSON・null 引数を安全に扱う
//    6. 結合       : NameGenerator が枯渇時に払い出す複合キーを正しく解決できる
//
//  ★ テスト規約: 日本語リテラルは用いず、表示文字列も ASCII プレースホルダで
//    与える（NameGeneratorTests と同じ方針）。連結順序は ASCII 文字列の並びと
//    出現位置 (IndexOf) で厳密に検証する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ChronicleKnights.Core.Naming;
using Xunit;

namespace ChronicleKnights.Tests.Core.Naming;

public class NameResolverTests
{
    // 表示文字列はすべて ASCII プレースホルダ（順序検証を一意にするため接頭辞付き）。
    private const string GivenValueA  = "1-GIVEN-A";
    private const string GivenValueB  = "1-GIVEN-B";
    private const string FamilyValueA = "3-FAMILY-A";
    private const string TitleValueC  = "0-TITLE-C";

    private const string GivenKeyA  = "name-japanese-male-001";
    private const string GivenKeyB  = "name-european-female-002";
    private const string FamilyKeyA = "name-family-japanese-001";
    private const string TitleKeyC  = "title-003";

    // names セクションを持つ最小の localization JSON フィクスチャ。
    private static string SampleLocalizationJson() => $$"""
    {
      "names": {
        "_note": "test fixture (ascii only)",
        "firstNames": {
          "{{GivenKeyA}}": "{{GivenValueA}}",
          "{{GivenKeyB}}": "{{GivenValueB}}"
        },
        "familyNames": {
          "{{FamilyKeyA}}": "{{FamilyValueA}}"
        },
        "titles": {
          "{{TitleKeyC}}": "{{TitleValueC}}"
        }
      },
      "ui": { "ignored": "should-not-be-loaded" }
    }
    """;

    private static NameResolver SampleResolver() =>
        NameResolver.FromLocalizationJson(SampleLocalizationJson());

    // ─── 1. 読込（3 サブセクションを統合） ─────────────────────────────────

    [Fact]
    public void FromLocalizationJson_LoadsAllThreeSections()
    {
        var resolver = SampleResolver();

        // firstNames(2) + familyNames(1) + titles(1) = 4。_note と ui は無視される。
        Assert.Equal(4, resolver.EntryCount);
    }

    // ─── 2. 単純解決 / フォールバック ──────────────────────────────────────

    [Fact]
    public void ResolveToken_KnownKey_ReturnsDisplayString()
    {
        var resolver = SampleResolver();

        Assert.Equal(GivenValueA, resolver.ResolveToken(GivenKeyA));
        Assert.Equal(FamilyValueA, resolver.ResolveToken(FamilyKeyA));
        Assert.Equal(TitleValueC, resolver.ResolveToken(TitleKeyC));
    }

    [Fact]
    public void ResolveToken_UnknownKey_FallsBackToRawKey()
    {
        var resolver = SampleResolver();

        const string missing = "name-japanese-male-999";
        Assert.Equal(missing, resolver.ResolveToken(missing));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveToken_NullOrEmpty_ReturnsEmpty(string? key)
    {
        Assert.Equal(string.Empty, SampleResolver().ResolveToken(key));
    }

    // ─── 3. 複合キー（'@' 分解） ───────────────────────────────────────────

    [Fact]
    public void TitleSeparator_MatchesNameGenerator()
    {
        Assert.Equal(NameGenerator.TitleSeparator, NameResolver.TitleSeparator);
    }

    [Fact]
    public void ResolveFirstName_SimpleKey_ReturnsGivenName()
    {
        Assert.Equal(GivenValueA, SampleResolver().ResolveFirstName(GivenKeyA));
    }

    [Fact]
    public void ResolveFirstName_CompositeKey_ConcatenatesTitleThenGiven()
    {
        var resolver = SampleResolver();
        var composite = $"{TitleKeyC}{NameResolver.TitleSeparator}{GivenKeyA}";

        var resolved = resolver.ResolveFirstName(composite);

        // 称号 ＋ 名前 の順で連結され、'@' は表示に残らない。
        Assert.Equal(TitleValueC + GivenValueA, resolved);
        Assert.DoesNotContain(NameResolver.TitleSeparator, resolved);
        Assert.True(resolved.IndexOf(TitleValueC, StringComparison.Ordinal)
                    < resolved.IndexOf(GivenValueA, StringComparison.Ordinal));
    }

    // ─── 4. 氏名連結（称号 ＋ 名前 ＋（あれば）姓） ─────────────────────────

    [Fact]
    public void ResolveFullName_SimpleGivenWithFamily_ConcatenatesGivenThenFamily()
    {
        var resolved = SampleResolver().ResolveFullName(GivenKeyA, FamilyKeyA);

        Assert.Equal(GivenValueA + FamilyValueA, resolved);
        Assert.True(resolved.IndexOf(GivenValueA, StringComparison.Ordinal)
                    < resolved.IndexOf(FamilyValueA, StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveFullName_CompositeWithFamily_OrdersTitleGivenFamily()
    {
        var resolver = SampleResolver();
        var composite = $"{TitleKeyC}{NameResolver.TitleSeparator}{GivenKeyA}";

        var resolved = resolver.ResolveFullName(composite, FamilyKeyA);

        // 称号 ＋ 名前 ＋ 姓 の厳密な順序。
        Assert.Equal(TitleValueC + GivenValueA + FamilyValueA, resolved);

        var titleAt  = resolved.IndexOf(TitleValueC, StringComparison.Ordinal);
        var givenAt  = resolved.IndexOf(GivenValueA, StringComparison.Ordinal);
        var familyAt = resolved.IndexOf(FamilyValueA, StringComparison.Ordinal);
        Assert.True(titleAt < givenAt);
        Assert.True(givenAt < familyAt);
    }

    [Fact]
    public void ResolveFullName_CompositeWithoutFamily_OmitsFamily()
    {
        var resolver = SampleResolver();
        var composite = $"{TitleKeyC}{NameResolver.TitleSeparator}{GivenKeyB}";

        Assert.Equal(TitleValueC + GivenValueB, resolver.ResolveFullName(composite));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveFullName_NullOrEmptyFamily_OmitsFamily(string? lastNameKey)
    {
        Assert.Equal(GivenValueA, SampleResolver().ResolveFullName(GivenKeyA, lastNameKey));
    }

    [Fact]
    public void ResolveFullName_UnknownKeys_FallBackToRawKeysInOrder()
    {
        var resolver = SampleResolver();

        const string missingGiven  = "name-classical-male-099";
        const string missingFamily = "name-family-classical-099";

        var resolved = resolver.ResolveFullName(missingGiven, missingFamily);

        // 未登録でも落ちず、生キーをそのまま順序通り連結する。
        Assert.Equal(missingGiven + missingFamily, resolved);
    }

    // ─── 5. 堅牢性 ─────────────────────────────────────────────────────────

    [Fact]
    public void FromLocalizationJson_NoNamesSection_YieldsEmptyResolver()
    {
        const string json = """{ "ui": { "title": "x" }, "items": {} }""";

        var resolver = NameResolver.FromLocalizationJson(json);

        Assert.Equal(0, resolver.EntryCount);
        // 空でも例外を出さず、生キーへフォールバックする。
        Assert.Equal("name-japanese-male-001", resolver.ResolveToken("name-japanese-male-001"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromLocalizationJson_NullOrWhitespace_Throws(string? json)
    {
        Assert.Throws<ArgumentException>(() => NameResolver.FromLocalizationJson(json!));
    }

    [Fact]
    public void FromLocalizationJson_MalformedJson_ThrowsJsonException()
    {
        // System.Text.Json throws JsonReaderException (a JsonException subclass) for
        // malformed input; ThrowsAny accepts the base type and any derived parse error.
        Assert.ThrowsAny<JsonException>(
            () => NameResolver.FromLocalizationJson("{ this is not valid json "));
    }

    [Fact]
    public void Constructor_NullEntries_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NameResolver(null!));
    }

    [Fact]
    public void Constructor_DirectDictionary_ResolvesEntries()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GivenKeyA] = GivenValueA,
            [FamilyKeyA] = FamilyValueA,
        };

        var resolver = new NameResolver(entries);

        Assert.Equal(2, resolver.EntryCount);
        Assert.Equal(GivenValueA + FamilyValueA, resolver.ResolveFullName(GivenKeyA, FamilyKeyA));
    }

    // ─── 6. NameGenerator との結合（枯渇時の複合キーを解決できる） ──────────

    [Fact]
    public void Resolves_CompositeKey_ProducedByNameGeneratorOnPoolExhaustion()
    {
        const Origin origin = Origin.European;
        const Gender gender = Gender.Male;

        // プールを完全枯渇させ、NameGenerator に称号付き複合キーを払い出させる。
        var pool = NameCatalog.FirstNames(origin, gender);
        var used = pool.ToHashSet(StringComparer.Ordinal);
        var generated = NameGenerator.Draw(origin, gender, used, new Random(2024));

        Assert.True(generated.Titled);
        Assert.Contains(NameGenerator.TitleSeparator, generated.FirstNameKey);

        // カタログ由来の全キー（称号・名前・姓）へ ASCII 表示値を割り当てた
        // リゾルバを構築する（日本語リテラルは使わない）。
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var titleKey in NameCatalog.Titles)
        {
            entries[titleKey] = $"T<{titleKey}>";
        }
        foreach (var nameKey in pool)
        {
            entries[nameKey] = $"G<{nameKey}>";
        }
        var familyPool = NameCatalog.FamilyNames(origin);
        foreach (var familyKey in familyPool)
        {
            entries[familyKey] = $"F<{familyKey}>";
        }
        var resolver = new NameResolver(entries);

        // 複合キーを分解して期待通り「称号 ＋ 名前」へ解決できる。
        var parts = generated.FirstNameKey.Split(NameGenerator.TitleSeparator);
        var expectedFirst = entries[parts[0]] + entries[parts[1]];
        Assert.Equal(expectedFirst, resolver.ResolveFirstName(generated.FirstNameKey));

        // 姓まで含めた氏名も「称号 ＋ 名前 ＋ 姓」で連結される。
        var expectedFull = expectedFirst + entries[generated.LastNameKey];
        Assert.Equal(expectedFull, resolver.ResolveFullName(generated.FirstNameKey, generated.LastNameKey));
        Assert.DoesNotContain(NameGenerator.TitleSeparator, resolver.ResolveFullName(
            generated.FirstNameKey, generated.LastNameKey));
    }
}
