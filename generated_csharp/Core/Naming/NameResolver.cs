// =============================================================================
//  ChronicleKnights — NameResolver.cs
// -----------------------------------------------------------------------------
//  名前ローカライズキー → 表示用日本語文字列への純粋な解決層（Godot 完全非依存）。
//
//  NameGenerator が払い出すのは ASCII キー（例 "name-japanese-male-007"）のみで、
//  実テキストは Config/localization_ja.json の names セクションに分離されている。
//  本クラスはその names セクションを読み込み、キーを実際の表示名へ変換する。
//
//  ★ 責務分離（SaveSerializer / SaveManager と同じ層別パターン）:
//    - 本クラス（純粋層）: JSON 文字列 → 辞書 → 表示名解決。Godot.* を一切参照
//      しないため xUnit で完全に検証できる。FromLocalizationJson で文字列を渡す。
//    - Godot I/O 層（ChronicleGlobal 等）: res:// から localization_ja.json の
//      テキストを読み取り、本クラスへ文字列として渡す責務だけを持つ。
//
//  ★ 複合キー（'@' 連結）の自動分解:
//    プール枯渇時、NameGenerator は称号キーとファーストネームキーを
//    区切り文字 '@' で連結した複合キー（例 "title-003@name-japanese-male-007"）を
//    払い出す。本リゾルバはこれを自動でパースし、
//      「称号」＋「名前」＋（あれば）「姓」
//    の順で 1 つの美しい日本語氏名へ連結する。
//
//  ★ 堅牢性:
//    未知キーは例外を投げず、生のキー文字列をそのまま返す（フォールバック）。
//    これにより localization_ja.json への登録漏れがあっても画面が落ちず、
//    どのキーが未登録かが表示からそのまま判別できる。
//
//  略称（正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ChronicleKnights.Core.Naming;

/// <summary>
/// localization_ja.json の names セクション（firstNames / familyNames / titles）を
/// 1 つの辞書へ統合し、名前キーを表示用日本語文字列へ解決する純粋なリゾルバ。
/// </summary>
public sealed class NameResolver
{
    /// <summary>称号キーとファーストネームキーを連結する区切り文字（NameGenerator と一致）。</summary>
    public const char TitleSeparator = NameGenerator.TitleSeparator;

    /// <summary>names セクションの各サブセクション名（JSON 側のキーと一致）。</summary>
    private static readonly string[] SectionNames = { "firstNames", "familyNames", "titles" };

    /// <summary>キー → 表示用日本語文字列の統合辞書（Ordinal 比較で厳密一致）。</summary>
    private readonly Dictionary<string, string> _entries;

    /// <summary>
    /// 解決辞書を直接注入して構築する。テストや、JSON 以外の供給元から
    /// 構築したい場合に使う。null キー / 値は安全に無視する。
    /// </summary>
    /// <param name="entries">キー → 表示文字列の辞書。</param>
    public NameResolver(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = new Dictionary<string, string>(entries.Count, StringComparer.Ordinal);
        foreach (var kv in entries)
        {
            if (!string.IsNullOrEmpty(kv.Key) && kv.Value is not null)
            {
                _entries[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>登録済みの表示エントリ数（健全性検証・テスト用）。</summary>
    public int EntryCount => _entries.Count;

    // ─── 構築（localization JSON 文字列から） ─────────────────────────────

    /// <summary>
    /// localization_ja.json の本文（文字列）から names セクションを読み取り、
    /// firstNames / familyNames / titles を 1 つの辞書へ統合した解決器を構築する。
    ///
    /// names セクション、または各サブセクションが欠落していても例外は投げず、
    /// 見つかった分だけを登録する（未登録キーは解決時に生キーへフォールバック）。
    /// </summary>
    /// <param name="localizationJson">localization_ja.json の本文。</param>
    /// <exception cref="ArgumentException">null または空白文字列の場合。</exception>
    /// <exception cref="JsonException">JSON として解析できない場合。</exception>
    public static NameResolver FromLocalizationJson(string localizationJson)
    {
        if (string.IsNullOrWhiteSpace(localizationJson))
        {
            throw new ArgumentException(
                "localization JSON が null または空です。", nameof(localizationJson));
        }

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(localizationJson);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("names", out var names)
            && names.ValueKind == JsonValueKind.Object)
        {
            foreach (var sectionName in SectionNames)
            {
                if (names.TryGetProperty(sectionName, out var section)
                    && section.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in section.EnumerateObject())
                    {
                        if (entry.Value.ValueKind == JsonValueKind.String)
                        {
                            entries[entry.Name] = entry.Value.GetString() ?? string.Empty;
                        }
                    }
                }
            }
        }

        return new NameResolver(entries);
    }

    // ─── 解決 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 単一キーを表示文字列へ解決する。未登録キーはそのまま返す（フォールバック）。
    /// null / 空は空文字へ。
    /// </summary>
    public string ResolveToken(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return _entries.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// ファーストネームキーを表示文字列へ解決する。'@' を含む複合キーなら
    /// 称号キーとファーストネームキーへ自動分解し、「称号 ＋ 名前」を連結して返す。
    /// </summary>
    /// <param name="firstNameKey">単純キー、または "title-NNN@name-..." 複合キー。</param>
    public string ResolveFirstName(string? firstNameKey)
    {
        if (string.IsNullOrEmpty(firstNameKey)) return string.Empty;

        var separatorIndex = firstNameKey.IndexOf(TitleSeparator);
        if (separatorIndex < 0)
        {
            // 単純キー → そのまま解決
            return ResolveToken(firstNameKey);
        }

        // 複合キー → 称号部と名前部へ分解して「称号 ＋ 名前」
        var titleKey = firstNameKey[..separatorIndex];
        var givenKey = firstNameKey[(separatorIndex + 1)..];
        return ResolveToken(titleKey) + ResolveToken(givenKey);
    }

    /// <summary>
    /// ユニットの氏名を「称号 ＋ 名前 ＋（あれば）姓」の順で 1 つの表示文字列へ連結する。
    ///
    /// - <paramref name="firstNameKey"/> が複合キーなら称号と名前へ自動分解。
    /// - <paramref name="lastNameKey"/> が null / 空なら姓は連結しない。
    /// </summary>
    /// <param name="firstNameKey">ファーストネームキー（複合キー可）。</param>
    /// <param name="lastNameKey">ファミリーネームキー（任意）。</param>
    public string ResolveFullName(string? firstNameKey, string? lastNameKey = null)
    {
        var builder = new StringBuilder();

        // 1. 称号 ＋ 名前（複合キーは自動分解）
        builder.Append(ResolveFirstName(firstNameKey));

        // 2. （あれば）姓
        if (!string.IsNullOrEmpty(lastNameKey))
        {
            builder.Append(ResolveToken(lastNameKey));
        }

        return builder.ToString();
    }
}
