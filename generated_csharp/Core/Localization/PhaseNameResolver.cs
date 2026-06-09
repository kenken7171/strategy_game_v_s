// =============================================================================
//  ChronicleKnights — PhaseNameResolver.cs
// -----------------------------------------------------------------------------
//  ゲームフェーズのスラッグ → 表示用日本語文字列への純粋な解決層（Godot 完全非依存）。
//
//  GamePhaseFlow が払い出すのは ASCII スラッグ（例 "chronicle" / "guild"）のみで、
//  実テキストは Config/localization_ja.json の phases セクションに分離されている。
//  本クラスはその phases セクションを読み込み、スラッグを実際の表示名へ変換する。
//
//  ★ 責務分離（NameResolver / SaveSerializer と同じ層別パターン）:
//    - 本クラス（純粋層）: JSON 文字列 → 辞書 → 表示名解決。Godot.* を一切参照
//      しないため xUnit で完全に検証できる。FromLocalizationJson で文字列を渡す。
//    - Godot 入出力層（ChronicleGlobal）: res:// から localization_ja.json の
//      テキストを読み取り、本クラスへ文字列として渡す責務だけを持つ。
//
//  ★ 堅牢性（NameResolver と同一思想）:
//    未知スラッグは例外を投げず、生のスラッグ文字列をそのまま返す（フォールバック）。
//    これにより localization_ja.json への登録漏れがあっても画面が落ちず、
//    どのフェーズが未登録かが表示からそのまま判別できる。
//
//  略称（正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ChronicleKnights.Core.GameFlow;

namespace ChronicleKnights.Core.Localization;

/// <summary>
/// localization_ja.json の phases セクション（スラッグ → 表示名）を読み込み、
/// <see cref="GamePhase"/> のスラッグを表示用日本語フェーズ名へ解決する純粋なリゾルバ。
/// </summary>
public sealed class PhaseNameResolver
{
    /// <summary>localization JSON 側のフェーズ表示名セクション名。</summary>
    private const string PhasesSectionName = "phases";

    /// <summary>スラッグ → 表示用日本語文字列の辞書（Ordinal 比較で厳密一致）。</summary>
    private readonly Dictionary<string, string> _entries;

    /// <summary>
    /// 解決辞書を直接注入して構築する。テストや、JSON 以外の供給元から
    /// 構築したい場合に使う。null キー / 値は安全に無視する。
    /// </summary>
    /// <param name="entries">スラッグ → 表示文字列の辞書。</param>
    public PhaseNameResolver(IReadOnlyDictionary<string, string> entries)
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
    /// localization_ja.json の本文（文字列）から phases セクションを読み取り、
    /// スラッグ → 表示名の解決器を構築する。
    ///
    /// phases セクションが欠落していても例外は投げず、見つかった分だけを登録する
    /// （未登録スラッグは解決時に生スラッグへフォールバックする）。
    /// </summary>
    /// <param name="localizationJson">localization_ja.json の本文。</param>
    /// <exception cref="ArgumentException">null または空白文字列の場合。</exception>
    /// <exception cref="JsonException">JSON として解析できない場合。</exception>
    public static PhaseNameResolver FromLocalizationJson(string localizationJson)
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
            && root.TryGetProperty(PhasesSectionName, out var phases)
            && phases.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in phases.EnumerateObject())
            {
                // "_" 始まりは注釈キー（_note / _meta 等）の慣習として読み飛ばす。
                if (entry.Name.StartsWith('_')) continue;

                if (entry.Value.ValueKind == JsonValueKind.String)
                {
                    entries[entry.Name] = entry.Value.GetString() ?? string.Empty;
                }
            }
        }

        return new PhaseNameResolver(entries);
    }

    // ─── 解決 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// フェーズのスラッグを表示文字列へ解決する。未登録スラッグはそのまま返す
    /// （フォールバック）。null / 空は空文字へ。
    /// </summary>
    public string Resolve(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return string.Empty;
        return _entries.TryGetValue(slug, out var value) ? value : slug;
    }

    /// <summary>
    /// <see cref="GamePhase"/> を表示用日本語フェーズ名へ解決する便宜オーバーロード。
    /// 内部で <see cref="GamePhaseFlow.Slug(GamePhase)"/> を介してスラッグへ変換する。
    /// </summary>
    public string Resolve(GamePhase phase) => Resolve(phase.Slug());
}
