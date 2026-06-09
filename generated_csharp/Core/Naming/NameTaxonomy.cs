// =============================================================================
//  ChronicleKnights — NameTaxonomy.cs
// -----------------------------------------------------------------------------
//  名前生成システムの「分類軸」を定義する純粋な型システム層（Godot 完全非依存）。
//
//  TypeScript 版 data/names.ts の `Origin` 文字列 union と男女 2 プールの概念を
//  enum 化して翻訳したもの。本ファイルには enum と、ローカライズキーに埋め込む
//  ASCII スラッグを組み立てる純粋ヘルパーだけを置く。
//
//  ★ 設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md) の絶対遵守事項:
//    - 日本語テキストは一切含めない（朱雀 / ジークフリート 等の名前テキストは
//      Config/localization_ja.json の names セクションに分離する）。
//    - 本ファイルが返すのは "japanese" / "male" 等の ASCII スラッグのみ。
//
//  ★ 軸の責務分担:
//    - Origin（文化圏）: ユニットに永続保持する血統属性。スカウト・婚姻で
//      どの系譜を引いているかを識別・追跡するために Unit に格納される。
//    - Gender（性別）  : 名前プールを Male / Female に分けるための「生成時専用」
//      の選択軸。Unit には保持しない（名前キーを選ぶためだけに使う内部概念）。
// =============================================================================

using System;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Naming;

// ─── 文化圏（血統属性・ユニットに永続保持） ──────────────────────────────────

/// <summary>
/// ユニットの命名文化圏。名前プールの分離と血統系譜の追跡に使う。
/// TypeScript 版 Origin 文字列 union ("Japanese" | "European" | "Classical") の翻訳。
/// </summary>
public enum Origin
{
    /// <summary>古風かつ力強い和風の響き（戦国・古典・神話・幕末）。</summary>
    Japanese,

    /// <summary>叙事詩的・中世欧州（北欧/独/仏/英）の響き。</summary>
    European,

    /// <summary>神話・星・幻想（メソポタミア/ギリシャ/エジプト/天体）の響き。</summary>
    Classical,
}

// ─── 性別（名前プール選択の生成時専用軸・Unit には保持しない） ─────────────────

/// <summary>
/// 名前プールを分けるための性別軸。Unit には保存せず、名前キーを払い出す際に
/// どちらのプールを引くかを決めるためだけに使う内部概念。
/// </summary>
public enum Gender
{
    Male,
    Female,
}

// ─── 分類軸ヘルパー（ASCII スラッグ生成・列挙） ──────────────────────────────

/// <summary>
/// Origin / Gender からローカライズキーに埋め込む ASCII スラッグを組み立てる
/// 純粋ヘルパー群と、全列挙値の不変配列を提供する静的クラス。
///
/// マジックストリング（"japanese" 等）を呼び出し側に散らさないための単一窓口。
/// </summary>
public static class NameTaxonomy
{
    /// <summary>全文化圏（生成・検証で網羅列挙するための固定順）。</summary>
    public static readonly ImmutableArray<Origin> AllOrigins =
        ImmutableArray.Create(Origin.Japanese, Origin.European, Origin.Classical);

    /// <summary>全性別（名前プール網羅列挙用）。</summary>
    public static readonly ImmutableArray<Gender> AllGenders =
        ImmutableArray.Create(Gender.Male, Gender.Female);

    /// <summary>文化圏の ASCII スラッグ（ローカライズキー用）。</summary>
    public static string Slug(this Origin origin) => origin switch
    {
        Origin.Japanese  => "japanese",
        Origin.European  => "european",
        Origin.Classical => "classical",
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown Origin"),
    };

    /// <summary>性別の ASCII スラッグ（ローカライズキー用）。</summary>
    public static string Slug(this Gender gender) => gender switch
    {
        Gender.Male   => "male",
        Gender.Female => "female",
        _ => throw new ArgumentOutOfRangeException(nameof(gender), gender, "Unknown Gender"),
    };
}
