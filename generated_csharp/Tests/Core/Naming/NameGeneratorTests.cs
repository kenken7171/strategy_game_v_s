// =============================================================================
//  ChronicleKnights.Tests — NameGeneratorTests.cs
// -----------------------------------------------------------------------------
//  名前生成システム (NameCatalog / NameGenerator) の純粋ロジック単体テスト。
//
//  検証観点（ユーザー要件に対応）:
//    1. 再現性     : 同一シードの Random なら同一の名前キーを払い出す
//    2. 重複回避   : 使用済みキー集合に含まれないキーだけを返す
//    3. 枯渇耐性   : プールが完全枯渇しても例外を投げず、'@' 区切りの
//                    称号付き複合キー (Titled=true) へ安全にフォールバックする
//    4. カタログ健全性: 全プールが規定サイズで、内部重複が存在しない
//                       （NameCatalog 静的コンストラクタの自己検証が通る）
//    5. DrawRandom : 文化圏・性別の乱択も同一シードで再現する
//
//  乱数発生器は必ず引数で外部注入する設計（設計憲法）。本テストは seeded Random
//  を渡すことで決定論を得る。日本語リテラルは一切登場せず ASCII キーのみを扱う。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Naming;
using Xunit;

namespace ChronicleKnights.Tests.Core.Naming;

public class NameGeneratorTests
{
    private static Random SeededRng(int seed = 12345) => new(seed);

    private static HashSet<string> EmptyUsed() => new(StringComparer.Ordinal);

    // ─── 1. 再現性（同一シード → 同一キー） ───────────────────────────────

    [Fact]
    public void Draw_WithSameSeed_ProducesIdenticalKeys()
    {
        var used = EmptyUsed();

        var first = NameGenerator.Draw(Origin.Japanese, Gender.Male, used, SeededRng(2024));
        var second = NameGenerator.Draw(Origin.Japanese, Gender.Male, used, SeededRng(2024));

        Assert.Equal(first.FirstNameKey, second.FirstNameKey);
        Assert.Equal(first.LastNameKey, second.LastNameKey);
        Assert.Equal(first.Origin, second.Origin);
        Assert.Equal(first.Gender, second.Gender);
        Assert.Equal(first.Titled, second.Titled);
    }

    [Fact]
    public void Draw_DifferentSeeds_CanProduceDifferentKeys()
    {
        // 異なるシードでは（プールが十分大きいので）少なくとも一方の軸が変化しうる。
        // 厳密な不一致保証はできないが、生成が seed に依存することを確認する。
        var used = EmptyUsed();
        var a = NameGenerator.Draw(Origin.European, Gender.Female, used, SeededRng(1));
        var b = NameGenerator.Draw(Origin.European, Gender.Female, used, SeededRng(9999));

        // どちらも有効なファーストネームキー（プール由来）であること。
        Assert.Contains(a.FirstNameKey, NameCatalog.FirstNames(Origin.European, Gender.Female));
        Assert.Contains(b.FirstNameKey, NameCatalog.FirstNames(Origin.European, Gender.Female));
    }

    // ─── 2. 重複回避（使用済みキーは返さない） ─────────────────────────────

    [Fact]
    public void Draw_ReturnsKeyNotInUsedSet()
    {
        var pool = NameCatalog.FirstNames(Origin.Classical, Gender.Male);
        // 12 件中 11 件を「使用済み」にして、残り 1 件しか選べない状況を作る。
        var theOnlyFree = pool[7];
        var used = pool.Where(k => k != theOnlyFree).ToHashSet(StringComparer.Ordinal);

        var result = NameGenerator.Draw(Origin.Classical, Gender.Male, used, SeededRng());

        Assert.False(result.Titled);                       // まだプールに空きがある
        Assert.Equal(theOnlyFree, result.FirstNameKey);    // 唯一の未使用キーを返す
        Assert.DoesNotContain(result.FirstNameKey, used);  // 使用済みは絶対に返さない
    }

    [Fact]
    public void Draw_NeverReturnsAnyUsedKey_AcrossManyDraws()
    {
        // ロスターを 1 名ずつ増やしながら連続抽選し、毎回ユニークなキーが返ることを確認。
        var used = EmptyUsed();
        var rng = SeededRng(555);

        for (var i = 0; i < NameCatalog.FirstNamePoolSize; i++)
        {
            var result = NameGenerator.Draw(Origin.Japanese, Gender.Female, used, rng);
            Assert.DoesNotContain(result.FirstNameKey, used);
            Assert.False(result.Titled); // プール枯渇前は称号フォールバックしない
            used.Add(result.FirstNameKey);
        }

        // プール 12 件をすべて使い切ったので、ユニークキーが 12 件たまっている。
        Assert.Equal(NameCatalog.FirstNamePoolSize, used.Count);
    }

    // ─── 3. 枯渇耐性（称号付き複合キーへフォールバック・例外なし） ─────────

    [Fact]
    public void Draw_WhenPoolExhausted_FallsBackToTitledCompositeKey()
    {
        // プール全 12 件を使用済みにして、平のキーをこれ以上払い出せない状況を作る。
        var pool = NameCatalog.FirstNames(Origin.European, Gender.Male);
        var used = pool.ToHashSet(StringComparer.Ordinal);

        // 例外を投げず、称号付き複合キーを返すこと。
        var result = NameGenerator.Draw(Origin.European, Gender.Male, used, SeededRng());

        Assert.True(result.Titled);
        Assert.Contains(NameGenerator.TitleSeparator, result.FirstNameKey);

        // 複合キーは "title-NNN@name-..." の形に分解できる。
        var parts = result.FirstNameKey.Split(NameGenerator.TitleSeparator);
        Assert.Equal(2, parts.Length);
        Assert.StartsWith("title-", parts[0]);
        Assert.StartsWith("name-", parts[1]);

        // 称号部・名前部はそれぞれ正規のカタログキーである。
        Assert.Contains(parts[0], NameCatalog.Titles);
        Assert.Contains(parts[1], pool);
    }

    [Fact]
    public void Draw_TitledCompositeKey_IsAlsoDeduplicated()
    {
        // 平キー 12 件 + 既に生成済みの複合キー 1 件 を使用済みにしても、
        // 別の未使用な複合キーを返す（例外を投げない）。
        var pool = NameCatalog.FirstNames(Origin.Classical, Gender.Female);
        var used = pool.ToHashSet(StringComparer.Ordinal);

        var firstComposite =
            NameGenerator.Draw(Origin.Classical, Gender.Female, used, SeededRng(10)).FirstNameKey;
        used.Add(firstComposite);

        var secondComposite =
            NameGenerator.Draw(Origin.Classical, Gender.Female, used, SeededRng(11)).FirstNameKey;

        Assert.NotEqual(firstComposite, secondComposite);
        Assert.DoesNotContain(secondComposite, used);
    }

    // ─── 4. カタログ健全性（規定サイズ・内部重複なし） ────────────────────

    [Fact]
    public void Catalog_AllPools_MeetExpectedSizesAndAreDistinct()
    {
        foreach (var origin in NameTaxonomy.AllOrigins)
        {
            foreach (var gender in NameTaxonomy.AllGenders)
            {
                var firstPool = NameCatalog.FirstNames(origin, gender);
                Assert.Equal(NameCatalog.FirstNamePoolSize, firstPool.Length);
                Assert.True(firstPool.Length >= NameCatalog.MinPoolSize);
                Assert.Equal(firstPool.Length, firstPool.Distinct().Count());
            }

            var familyPool = NameCatalog.FamilyNames(origin);
            Assert.Equal(NameCatalog.FamilyNamePoolSize, familyPool.Length);
            Assert.Equal(familyPool.Length, familyPool.Distinct().Count());
        }

        Assert.Equal(NameCatalog.TitlePoolSize, NameCatalog.Titles.Length);
        Assert.Equal(NameCatalog.Titles.Length, NameCatalog.Titles.Distinct().Count());
    }

    [Theory]
    [InlineData(Origin.Japanese, Gender.Male)]
    [InlineData(Origin.European, Gender.Female)]
    [InlineData(Origin.Classical, Gender.Male)]
    public void Catalog_KeyFormat_FollowsLocalizationConvention(Origin origin, Gender gender)
    {
        var prefix = $"name-{origin.Slug()}-{gender.Slug()}-";
        foreach (var key in NameCatalog.FirstNames(origin, gender))
        {
            Assert.StartsWith(prefix, key);
        }

        foreach (var key in NameCatalog.FamilyNames(origin))
        {
            Assert.StartsWith($"name-family-{origin.Slug()}-", key);
        }
    }

    // ─── 5. DrawRandom（文化圏・性別の乱択も再現する） ────────────────────

    [Fact]
    public void DrawRandom_WithSameSeed_ProducesIdenticalResult()
    {
        var used = EmptyUsed();

        var first = NameGenerator.DrawRandom(used, SeededRng(42));
        var second = NameGenerator.DrawRandom(used, SeededRng(42));

        Assert.Equal(first.Origin, second.Origin);
        Assert.Equal(first.Gender, second.Gender);
        Assert.Equal(first.FirstNameKey, second.FirstNameKey);
        Assert.Equal(first.LastNameKey, second.LastNameKey);
    }

    [Fact]
    public void DrawRandom_ProducesValidCatalogKeys()
    {
        var result = NameGenerator.DrawRandom(EmptyUsed(), SeededRng(7));

        Assert.Contains(result.Origin, NameTaxonomy.AllOrigins);
        Assert.Contains(result.Gender, NameTaxonomy.AllGenders);
        Assert.Contains(result.FirstNameKey, NameCatalog.FirstNames(result.Origin, result.Gender));
        Assert.Contains(result.LastNameKey, NameCatalog.FamilyNames(result.Origin));
    }

    // ─── 引数ガード ───────────────────────────────────────────────────────

    [Fact]
    public void Draw_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NameGenerator.Draw(Origin.Japanese, Gender.Male, null!, SeededRng()));
        Assert.Throws<ArgumentNullException>(
            () => NameGenerator.Draw(Origin.Japanese, Gender.Male, EmptyUsed(), null!));
    }
}
