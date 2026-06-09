// =============================================================================
//  ChronicleKnights — NameGenerator.cs
// -----------------------------------------------------------------------------
//  重複回避ロジック付きの「ローカライズキー」生成器。Godot 完全非依存の純粋ロジック。
//
//  TypeScript 版 data/names.ts の NameGenerator を C# へ翻訳したもの。ただし
//  日本語リテラルではなく ASCII ローカライズキー（NameCatalog 由来）を払い出す。
//
//  ★ 設計憲法の絶対遵守:
//    - 乱数発生器 (Random) は永続化せず、必ず引数で外部注入する
//      （「Random はロード時に再注入」というセーブ設計と一貫）。これにより
//        seeded Random を渡せば完全に再現性のあるテストが書ける。
//    - 日本語テキストは一切扱わない（キーのみ）。
//
//  ★ アルゴリズム（TypeScript 版と同思想）:
//    1. 対象プール（文化圏 × 性別）から、まだ使われていないキーをランダムに払い出す
//    2. プールが完全に枯渇（全キーが使用済み）した場合はエラーにせず、
//       称号キーとファーストネームキーを区切り文字 '@' で連結した「複合キー」
//       （例 "title-003@name-japanese-male-007"）を未使用になるまで生成して返す
//       → 表示層 (UI) は '@' で分解し「称号」＋「名前」をローカライズ連結する
//    3. 称号 × 名前の全組み合わせすら枯渇した場合のみ例外（事実上到達不能）
//
//    ※ Jr. / II 世のような記号的重複回避は仕様により採用しない。称号方式のみ。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ChronicleKnights.Core.Naming;

// ─── 生成結果（不変スナップショット） ───────────────────────────────────────

/// <summary>
/// 名前生成の結果。ユニットへ格納するファーストネームキー・ファミリーネームキーに
/// 加え、文化圏・性別・診断情報（再抽選回数・称号フォールバック有無）を含む。
/// </summary>
public sealed record GeneratedName
{
    /// <summary>抽選された文化圏（ユニットへ永続保持する血統属性）。</summary>
    public required Origin Origin { get; init; }

    /// <summary>名前プール選択に使った性別（ユニットには保持しない診断値）。</summary>
    public required Gender Gender { get; init; }

    /// <summary>
    /// ファーストネームのローカライズキー。プール枯渇時は称号付き複合キー
    /// （"title-NNN@name-..."）になる。
    /// </summary>
    public required string FirstNameKey { get; init; }

    /// <summary>ファミリーネームのローカライズキー。</summary>
    public required string LastNameKey { get; init; }

    /// <summary>未使用キーに当たるまでに飛ばした使用済みキー数（参考診断値）。</summary>
    public required int Retries { get; init; }

    /// <summary>true ならプール枯渇により称号付き複合キーを生成した。</summary>
    public required bool Titled { get; init; }
}

// ─── 生成器本体（純粋・静的・乱数外部注入） ──────────────────────────────────

/// <summary>
/// 過去に使用された名前キー集合を避けて、新しい名前キーを払い出す純粋な静的生成器。
/// </summary>
public static class NameGenerator
{
    /// <summary>称号キーとファーストネームキーを連結する区切り文字。</summary>
    public const char TitleSeparator = '@';

    /// <summary>称号付き複合キー生成の最大試行回数（無限ループ防止）。</summary>
    private const int MaxCompositeAttempts = 10_000;

    // ─── 乱数による軸の抽選 ───────────────────────────────────────────────

    /// <summary>文化圏を均等乱択する。</summary>
    public static Origin PickRandomOrigin(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return NameTaxonomy.AllOrigins[rng.Next(NameTaxonomy.AllOrigins.Length)];
    }

    /// <summary>性別を均等乱択する。</summary>
    public static Gender PickRandomGender(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return NameTaxonomy.AllGenders[rng.Next(NameTaxonomy.AllGenders.Length)];
    }

    // ─── 名前キーの払い出し ───────────────────────────────────────────────

    /// <summary>
    /// 指定文化圏 × 性別のプールから、usedFirstNameKeys に含まれないファーストネーム
    /// キーを 1 つ払い出す。プール枯渇時は称号付き複合キーへ安全にフォールバックする。
    /// ファミリーネームキーは文化圏のプールから均等乱択する（重複許容＝同姓は自然）。
    /// </summary>
    /// <param name="origin">名前プールの文化圏。</param>
    /// <param name="gender">名前プールの性別。</param>
    /// <param name="usedFirstNameKeys">既に使用済みのファーストネームキー集合。</param>
    /// <param name="rng">乱数発生器（外部注入・seeded で再現可能）。</param>
    public static GeneratedName Draw(
        Origin origin,
        Gender gender,
        IReadOnlySet<string> usedFirstNameKeys,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(usedFirstNameKeys);
        ArgumentNullException.ThrowIfNull(rng);

        var firstPool = NameCatalog.FirstNames(origin, gender);
        var familyPool = NameCatalog.FamilyNames(origin);

        var lastNameKey = familyPool[rng.Next(familyPool.Length)];

        // 1. 未使用のファーストネームキーを収集
        var available = new List<string>(firstPool.Length);
        foreach (var key in firstPool)
        {
            if (!usedFirstNameKeys.Contains(key)) available.Add(key);
        }

        if (available.Count > 0)
        {
            var firstNameKey = available[rng.Next(available.Count)];
            return new GeneratedName
            {
                Origin       = origin,
                Gender       = gender,
                FirstNameKey = firstNameKey,
                LastNameKey  = lastNameKey,
                Retries      = firstPool.Length - available.Count,
                Titled       = false,
            };
        }

        // 2. プール枯渇 → 称号付き複合キーで未使用を探す
        var titles = NameCatalog.Titles;
        for (var attempt = 0; attempt < MaxCompositeAttempts; attempt++)
        {
            var title = titles[rng.Next(titles.Length)];
            var baseKey = firstPool[rng.Next(firstPool.Length)];
            var composite = $"{title}{TitleSeparator}{baseKey}";
            if (!usedFirstNameKeys.Contains(composite))
            {
                return new GeneratedName
                {
                    Origin       = origin,
                    Gender       = gender,
                    FirstNameKey = composite,
                    LastNameKey  = lastNameKey,
                    Retries      = attempt,
                    Titled       = true,
                };
            }
        }

        // 3. 称号 × 名前の全組み合わせも枯渇（事実上到達不能）
        throw new InvalidOperationException(
            $"[NameGenerator] {origin.Slug()}/{gender.Slug()} の名前生成に失敗" +
            "（プール + 称号も枯渇）。");
    }

    /// <summary>
    /// 文化圏・性別を均等乱択した上でファーストネームキーを払い出す便宜メソッド。
    /// 血縁を持たない外様スカウトの命名に使う（どの文化圏から来るかも乱数で決まる）。
    /// </summary>
    public static GeneratedName DrawRandom(IReadOnlySet<string> usedFirstNameKeys, Random rng)
    {
        ArgumentNullException.ThrowIfNull(usedFirstNameKeys);
        ArgumentNullException.ThrowIfNull(rng);

        var origin = PickRandomOrigin(rng);
        var gender = PickRandomGender(rng);
        return Draw(origin, gender, usedFirstNameKeys, rng);
    }
}
