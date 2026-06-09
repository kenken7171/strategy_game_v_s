// =============================================================================
//  ChronicleKnights — NameCatalog.cs
// -----------------------------------------------------------------------------
//  名前ローカライズキーの単一の真実 (SoT)。Godot 完全非依存の純粋データ層。
//
//  ★ 設計憲法の絶対遵守 — 日本語テキストは一切持たない:
//    TypeScript 版 data/names.ts は「朱雀」「ジークフリート」等の日本語リテラルを
//    直接保持していたが、本クラスはそれらに 1 対 1 対応する ASCII キー文字列
//    （例 "name-japanese-male-001"）だけを保持する。実テキストは
//    Config/localization_ja.json の names セクションが引き当て先。
//
//  ★ キー命名規約（JSON 側と完全一致）:
//    - ファーストネーム : name-{origin}-{gender}-{NNN}   例 name-japanese-male-001
//    - ファミリーネーム : name-family-{origin}-{NNN}      例 name-family-european-002
//    - 称号（接頭辞）   : title-{NNN}                      例 title-003
//    （origin/gender は NameTaxonomy.Slug() の ASCII スラッグ、NNN は 3 桁連番）
//
//  ★ 健全性の自己検証:
//    型ロード時（静的コンストラクタ）に全プールが「最小サイズを満たすか」
//    「内部重複がないか」を検証し、違反があれば即座に例外を投げて早期に気づける
//    ようにする（TypeScript 版 validatePool と同思想）。キーは連番生成のため
//    通常は重複しないが、将来の手書き拡張に備えた堅牢なガードとして残す。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ChronicleKnights.Core.Naming;

/// <summary>
/// 文化圏 × 性別ごとのファーストネームキー、文化圏ごとのファミリーネームキー、
/// および称号キーの不変プールを保持する静的 SoT。
/// </summary>
public static class NameCatalog
{
    // ─── プール規模の定数 ─────────────────────────────────────────────────

    /// <summary>(文化圏 × 性別) ごとのファーストネームキー数。</summary>
    public const int FirstNamePoolSize = 12;

    /// <summary>文化圏ごとのファミリーネームキー数。</summary>
    public const int FamilyNamePoolSize = 6;

    /// <summary>称号（プール枯渇時の接頭辞）キー数。</summary>
    public const int TitlePoolSize = 8;

    /// <summary>
    /// 健全性検証で要求するプールの最小サイズ。各プールがこの値を下回ると
    /// 型ロード時に例外を投げる（プール枯渇に対する安全余裕の保証）。
    /// </summary>
    public const int MinPoolSize = 6;

    // ─── ビルド済み不変プール ─────────────────────────────────────────────

    private static readonly ImmutableDictionary<(Origin, Gender), ImmutableArray<string>> FirstNamePools;
    private static readonly ImmutableDictionary<Origin, ImmutableArray<string>> FamilyNamePools;

    /// <summary>称号キーの不変プール。</summary>
    public static readonly ImmutableArray<string> Titles;

    // ─── キー組み立て（純粋関数・JSON 命名規約と一致） ───────────────────

    /// <summary>ファーストネームキーを組み立てる（index は 1 始まり）。</summary>
    public static string FirstNameKey(Origin origin, Gender gender, int index)
        => $"name-{origin.Slug()}-{gender.Slug()}-{index:D3}";

    /// <summary>ファミリーネームキーを組み立てる（index は 1 始まり）。</summary>
    public static string FamilyNameKey(Origin origin, int index)
        => $"name-family-{origin.Slug()}-{index:D3}";

    /// <summary>称号キーを組み立てる（index は 1 始まり）。</summary>
    public static string TitleKey(int index)
        => $"title-{index:D3}";

    // ─── アクセサ ─────────────────────────────────────────────────────────

    /// <summary>指定文化圏 × 性別のファーストネームキー一覧を返す。</summary>
    public static ImmutableArray<string> FirstNames(Origin origin, Gender gender)
        => FirstNamePools[(origin, gender)];

    /// <summary>指定文化圏のファミリーネームキー一覧を返す。</summary>
    public static ImmutableArray<string> FamilyNames(Origin origin)
        => FamilyNamePools[origin];

    // ─── 構築 + 健全性検証（静的コンストラクタ） ─────────────────────────

    static NameCatalog()
    {
        var firstBuilder = ImmutableDictionary.CreateBuilder<(Origin, Gender), ImmutableArray<string>>();
        foreach (var origin in NameTaxonomy.AllOrigins)
        {
            foreach (var gender in NameTaxonomy.AllGenders)
            {
                var pool = Enumerable.Range(1, FirstNamePoolSize)
                    .Select(i => FirstNameKey(origin, gender, i))
                    .ToImmutableArray();
                firstBuilder.Add((origin, gender), pool);
            }
        }
        FirstNamePools = firstBuilder.ToImmutable();

        var familyBuilder = ImmutableDictionary.CreateBuilder<Origin, ImmutableArray<string>>();
        foreach (var origin in NameTaxonomy.AllOrigins)
        {
            var pool = Enumerable.Range(1, FamilyNamePoolSize)
                .Select(i => FamilyNameKey(origin, i))
                .ToImmutableArray();
            familyBuilder.Add(origin, pool);
        }
        FamilyNamePools = familyBuilder.ToImmutable();

        Titles = Enumerable.Range(1, TitlePoolSize)
            .Select(TitleKey)
            .ToImmutableArray();

        ValidateAllPools();
    }

    /// <summary>
    /// 全プールの健全性（最小サイズ充足・内部重複なし）を検証する。
    /// 違反があれば InvalidOperationException を投げ、型ロード時点で破綻を露呈させる。
    /// </summary>
    private static void ValidateAllPools()
    {
        foreach (var origin in NameTaxonomy.AllOrigins)
        {
            foreach (var gender in NameTaxonomy.AllGenders)
            {
                ValidatePool(
                    $"firstNames/{origin.Slug()}/{gender.Slug()}",
                    FirstNamePools[(origin, gender)]);
            }
            ValidatePool($"familyNames/{origin.Slug()}", FamilyNamePools[origin]);
        }
        ValidatePool("titles", Titles);
    }

    private static void ValidatePool(string label, ImmutableArray<string> pool)
    {
        if (pool.Length < MinPoolSize)
        {
            throw new InvalidOperationException(
                $"[NameCatalog] プール '{label}' は {pool.Length} 件で最小サイズ {MinPoolSize} 未満。");
        }

        var distinct = new HashSet<string>(pool);
        if (distinct.Count != pool.Length)
        {
            var dupes = pool.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key);
            throw new InvalidOperationException(
                $"[NameCatalog] プール '{label}' に内部重複: {string.Join(", ", dupes)}");
        }
    }
}
