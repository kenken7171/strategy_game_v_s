// =============================================================================
//  ChronicleKnights — MasterDataNameResolver.cs
// -----------------------------------------------------------------------------
//  マスターデータ識別子（ジョブ / アイテム / 予言の種類）→ 表示用日本語テキスト
//  への純粋な解決層（Godot 完全非依存）。
//
//  ゲームのコード側は JobId / ItemId / ProphecyKind という型安全な enum だけを
//  扱い、人間可読な日本語ラベルや絵文字は一切持たない（設計憲法 ①）。実テキストは
//  Config/localization_ja.json の以下のネストしたセクションへ分離されている:
//
//    jobs.{JobId}.name            例: "鉄壁騎士"
//    items.{ItemId}.name          例: "🛡️ 誓いの聖剣"（絵文字は表示文字列に内包）
//    prophecyKinds.{Kind}.name    例: "報酬獲得"
//    prophecyKinds.{Kind}.icon    例: "💰"（予言だけはアイコンを別フィールドに分離）
//
//  本クラスは各セクションの該当サブフィールド（name / icon）だけを抽出し、
//  enum 名（ToString）をキーに表示テキストへ変換する。
//
//  ★ 責務分離（NameResolver / PhaseNameResolver / SaveSerializer と同じ層別パターン）:
//    - 本クラス（純粋層）: JSON 文字列 → 辞書 → 表示名解決。Godot.* を一切参照
//      しないため xUnit で完全に検証できる。FromLocalizationJson で文字列を渡す。
//    - Godot 入出力層（ChronicleGlobal）: res:// から localization_ja.json の
//      テキストを読み取り、本クラスへ文字列として渡す責務だけを持つ。
//
//  ★ 堅牢性（NameResolver / PhaseNameResolver と同一思想）:
//    未登録キー・予期せぬ enum 値は例外を投げず、生のキー文字列（enum 名）を
//    そのまま返す（フォールバック）。これにより localization_ja.json への登録漏れが
//    あっても画面が落ちず、どのキーが未登録かが表示からそのまま判別できる。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Localization;

/// <summary>
/// localization_ja.json の jobs / items / prophecyKinds セクションの表示テキストを
/// 読み込み、<see cref="JobId"/> / <see cref="ItemId"/> / <see cref="ProphecyKind"/>
/// を表示用日本語テキストへ解決する純粋なリゾルバ。
/// </summary>
public sealed class MasterDataNameResolver
{
    // ─── JSON セクション名・サブフィールド名（JSON 側のキーと一致） ─────────

    private const string JobsSectionName          = "jobs";
    private const string ItemsSectionName         = "items";
    private const string ProphecyKindsSectionName = "prophecyKinds";
    private const string EnemySkillsSectionName   = "enemySkills";
    private const string EpochsSectionName        = "epochs";
    private const string AffixesSectionName       = "affixes";
    private const string NameFieldName            = "name";
    private const string IconFieldName            = "icon";

    // ─── 解決辞書（enum 名 → 表示テキスト、Ordinal 比較で厳密一致） ─────────

    private readonly Dictionary<string, string> _jobNames;
    private readonly Dictionary<string, string> _itemNames;
    private readonly Dictionary<string, string> _prophecyKindNames;
    private readonly Dictionary<string, string> _prophecyKindIcons;

    // ─── 敵スキル名（敵の攻撃予告 AttackIntent.SkillNameKey → 表示テキスト） ──
    //  ジョブ/アイテムが enum 名をキーに引くのに対し、敵スキルだけは AttackIntentRoller
    //  が払い出す平坦な ASCII キー（例 "enemy-skill-single-strike"）をそのままキーに引く。
    private readonly Dictionary<string, string> _skillNames;

    // ─── 章（Epoch）名（100 年史の章キー → 表示テキスト） ─────────────────────
    //  ChronicleTimelineConfig が払い出す平坦な ASCII キー（例 "epoch-twilight"）を
    //  そのままキーに引く。章ボス前兆 UI（時間の矢）が章名を解決するのに使う。
    private readonly Dictionary<string, string> _epochNames;

    // ─── Affix 名（装備の付加効果キー → 表示テキスト） ───────────────────────
    //  AffixMaster が払い出す平坦な ASCII キー（例 "affix-sharp"）をそのままキーに引く。
    //  装備詳細 UI が Affix 名を解決するのに使う。
    private readonly Dictionary<string, string> _affixNames;

    /// <summary>
    /// 解決辞書を直接注入して構築する。テストや、JSON 以外の供給元から構築したい
    /// 場合に使う。null キー / 値は安全に無視する。敵スキル名（第 5 引数）は任意で、
    /// 省略・null の場合は空辞書（=常に生キーへフォールバック）として扱う。
    /// </summary>
    public MasterDataNameResolver(
        IReadOnlyDictionary<string, string> jobNames,
        IReadOnlyDictionary<string, string> itemNames,
        IReadOnlyDictionary<string, string> prophecyKindNames,
        IReadOnlyDictionary<string, string> prophecyKindIcons,
        IReadOnlyDictionary<string, string>? skillNames = null,
        IReadOnlyDictionary<string, string>? epochNames = null,
        IReadOnlyDictionary<string, string>? affixNames = null)
    {
        _jobNames          = Sanitize(jobNames);
        _itemNames         = Sanitize(itemNames);
        _prophecyKindNames = Sanitize(prophecyKindNames);
        _prophecyKindIcons = Sanitize(prophecyKindIcons);
        _skillNames        = skillNames is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Sanitize(skillNames);
        _epochNames        = epochNames is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Sanitize(epochNames);
        _affixNames        = affixNames is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Sanitize(affixNames);
    }

    /// <summary>null/空キー・null 値を除いた Ordinal 辞書へ写し替える防御的コピー。</summary>
    private static Dictionary<string, string> Sanitize(IReadOnlyDictionary<string, string> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var dict = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
        foreach (var kv in source)
        {
            if (!string.IsNullOrEmpty(kv.Key) && kv.Value is not null)
            {
                dict[kv.Key] = kv.Value;
            }
        }
        return dict;
    }

    // ─── 登録エントリ数（健全性検証・テスト用） ──────────────────────────

    /// <summary>登録済みジョブ名エントリ数。</summary>
    public int JobNameCount => _jobNames.Count;

    /// <summary>登録済みアイテム名エントリ数。</summary>
    public int ItemNameCount => _itemNames.Count;

    /// <summary>登録済み予言種別名エントリ数。</summary>
    public int ProphecyKindNameCount => _prophecyKindNames.Count;

    /// <summary>登録済み予言種別アイコンエントリ数。</summary>
    public int ProphecyKindIconCount => _prophecyKindIcons.Count;

    /// <summary>登録済み敵スキル名エントリ数。</summary>
    public int SkillNameCount => _skillNames.Count;

    /// <summary>登録済み章（Epoch）名エントリ数。</summary>
    public int EpochNameCount => _epochNames.Count;

    /// <summary>登録済み Affix 名エントリ数。</summary>
    public int AffixNameCount => _affixNames.Count;

    // ─── 構築（localization JSON 文字列から） ─────────────────────────────

    /// <summary>
    /// localization_ja.json の本文（文字列）から jobs / items / prophecyKinds の
    /// 各セクションを読み取り、name / icon サブフィールドの解決器を構築する。
    ///
    /// 各セクション・サブフィールドが欠落していても例外は投げず、見つかった分だけを
    /// 登録する（未登録キーは解決時に enum 名へフォールバックする）。
    /// </summary>
    /// <param name="localizationJson">localization_ja.json の本文。</param>
    /// <exception cref="ArgumentException">null または空白文字列の場合。</exception>
    /// <exception cref="JsonException">JSON として解析できない場合。</exception>
    public static MasterDataNameResolver FromLocalizationJson(string localizationJson)
    {
        if (string.IsNullOrWhiteSpace(localizationJson))
        {
            throw new ArgumentException(
                "localization JSON が null または空です。", nameof(localizationJson));
        }

        using var document = JsonDocument.Parse(localizationJson);
        var root = document.RootElement;

        var jobNames          = ExtractField(root, JobsSectionName,          NameFieldName);
        var itemNames         = ExtractField(root, ItemsSectionName,         NameFieldName);
        var prophecyKindNames = ExtractField(root, ProphecyKindsSectionName, NameFieldName);
        var prophecyKindIcons = ExtractField(root, ProphecyKindsSectionName, IconFieldName);
        var skillNames        = ExtractField(root, EnemySkillsSectionName,   NameFieldName);
        var epochNames        = ExtractField(root, EpochsSectionName,        NameFieldName);
        var affixNames        = ExtractField(root, AffixesSectionName,       NameFieldName);

        return new MasterDataNameResolver(
            jobNames, itemNames, prophecyKindNames, prophecyKindIcons, skillNames, epochNames, affixNames);
    }

    /// <summary>
    /// ネストしたセクション（{ "Key": { "field": "value", ... }, ... }）から、
    /// 指定サブフィールドの文字列値だけを「キー → 値」辞書へ抽出する。
    /// "_" 始まりの注釈キー（_note / _meta 等）は読み飛ばす。
    /// </summary>
    private static Dictionary<string, string> ExtractField(
        JsonElement root, string sectionName, string fieldName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(sectionName, out var section)
            && section.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in section.EnumerateObject())
            {
                // "_" 始まりは注釈キー（_note 等）の慣習として読み飛ばす。
                if (entry.Name.StartsWith('_')) continue;

                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty(fieldName, out var field)
                    && field.ValueKind == JsonValueKind.String)
                {
                    result[entry.Name] = field.GetString() ?? string.Empty;
                }
            }
        }

        return result;
    }

    // ─── 解決 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ジョブの表示用日本語名を解決する。未登録・未知 enum 値は enum 名へフォールバック。
    /// </summary>
    public string ResolveJobName(JobId job) => Lookup(_jobNames, job.ToString());

    /// <summary>
    /// アイテムの表示用日本語名を解決する（絵文字を含む）。未登録・未知 enum 値は
    /// enum 名へフォールバック。
    /// </summary>
    public string ResolveItemName(ItemId item) => Lookup(_itemNames, item.ToString());

    /// <summary>
    /// 予言種別の表示用日本語名を解決する。未登録・未知 enum 値は enum 名へフォールバック。
    /// </summary>
    public string ResolveProphecyKindName(ProphecyKind kind) => Lookup(_prophecyKindNames, kind.ToString());

    /// <summary>
    /// 予言種別のアイコン（絵文字）を解決する。未登録・未知 enum 値は enum 名へ
    /// フォールバック（登録漏れを画面から判別できるようにするため）。
    /// </summary>
    public string ResolveProphecyKindIcon(ProphecyKind kind) => Lookup(_prophecyKindIcons, kind.ToString());

    /// <summary>
    /// 敵スキル（攻撃予告）の表示用日本語名を、AttackIntent.SkillNameKey（平坦な ASCII
    /// キー）から解決する。未登録キー・null/空は生キーへフォールバック（登録漏れを
    /// 画面から判別できるようにするため）。
    /// </summary>
    public string ResolveSkillName(string skillNameKey) => Lookup(_skillNames, skillNameKey);

    /// <summary>
    /// 章（Epoch）の表示用日本語名を、ChronicleTimelineConfig の平坦な ASCII キー
    /// （例 "epoch-twilight"）から解決する。未登録キー・null/空は生キーへフォールバック
    /// （登録漏れを画面から判別できるようにするため）。
    /// </summary>
    public string ResolveEpochName(string epochNameKey) => Lookup(_epochNames, epochNameKey);

    /// <summary>
    /// Affix（装備の付加効果）の表示用日本語名を、AffixMaster の平坦な ASCII キー
    /// （例 "affix-sharp"）から解決する。未登録キー・null/空は生キーへフォールバック
    /// （登録漏れを画面から判別できるようにするため）。
    /// </summary>
    public string ResolveAffixName(string affixKey) => Lookup(_affixNames, affixKey);

    /// <summary>
    /// 辞書引き。未登録キーは生キーをそのまま返す（フォールバック）。null/空は空文字へ。
    /// </summary>
    private static string Lookup(Dictionary<string, string> dictionary, string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return dictionary.TryGetValue(key, out var value) ? value : key;
    }
}
