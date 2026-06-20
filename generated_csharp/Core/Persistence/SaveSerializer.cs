// =============================================================================
//  ChronicleKnights — SaveSerializer.cs
// -----------------------------------------------------------------------------
//  セーブデータの「状態 ⇄ JSON 文字列」純粋変換層（Godot 完全非依存）。
//
//  保存対象は ChronicleGlobal が保持する 4 つの不変状態:
//    1. PointsEconomy                       — ポイント一元経済の財布
//    2. TimelineEngine                      — 予言タイムラインの現在状態
//    3. ImmutableList<Unit>                 — 大隊の全旅団員リスト
//    4. ImmutableArray<ChronicleLogEntry>   — 旅団史（引退/戦死/昇級/解雇のナレーション素材）
//
//  ★ 重要設計判断 — DTO マッピング方式:
//    不変レコード (sealed record + required/init + ImmutableArray/
//    ImmutableDictionary) を System.Text.Json で直接ラウンドトリップすると、
//    ImmutableArray<T> の default 値や Guid キー辞書などで微妙な落とし穴がある。
//    そこで本ファイルでは「ディスク上のスキーマ」を表す可変 DTO クラスを別に
//    定義し、record ⇄ DTO の明示マッピングを経由する。これにより:
//      - ドメインレコードの内部リファクタがセーブ互換を壊さない（疎結合）
//      - スキーマのバージョニング（Version フィールド）が容易
//      - enum は文字列で保存（JsonStringEnumConverter）し可読・並べ替え耐性確保
//
//  ★ Random は保存しない:
//    乱数発生器 (System.Random) はシリアライズ対象に含めない。ロード時に
//    ChronicleGlobal が新しい Random を再注入する設計（ユーザー仕様）。
//
//  ★ Godot ランタイム非依存 — 単体テスト可能:
//    本ファイルは Godot.* を一切参照しない純粋な System.Text.Json 変換のみ。
//    実ファイル I/O（user:// 仮想パス・アトミック書き込み）は SaveManager.cs が
//    担当し、本ファイルとは明確に責務分離している。これにより xUnit から
//    Serialize → Deserialize のラウンドトリップ等価性を Godot 無しで検証できる。
//
//  略称 (BDF/SDF/AB/HL) は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Persistence;

// ─── ロード結果（Random を含まない確定状態のスナップショット） ───────────────

/// <summary>
/// セーブデータから復元された 3 つの確定状態の束。
///
/// ★ Random は含まない。呼び出し側（ChronicleGlobal.LoadGame）が新しい
///   Random を再注入する責務を持つ。
/// </summary>
public sealed record LoadedGameState
{
    /// <summary>復元されたポイント経済。</summary>
    public required PointsEconomy Economy { get; init; }

    /// <summary>復元された予言タイムライン。</summary>
    public required TimelineEngine Timeline { get; init; }

    /// <summary>復元された旅団員リスト。</summary>
    public required ImmutableList<Unit> Roster { get; init; }

    /// <summary>
    /// 復元された旅団史（年代記ナレーションの素材）。旧 v1 セーブには無いため既定は空配列
    /// （required を付けず、欠落セーブでも安全に空で復元できるようにする＝後方互換）。
    /// </summary>
    public ImmutableArray<ChronicleLogEntry> ChronicleLog { get; init; }
        = ImmutableArray<ChronicleLogEntry>.Empty;

    /// <summary>
    /// 復元された旅団共有の持ち物（未装着の装備個体）。旧 v1〜v4 セーブには無いため既定は空
    /// （required を付けず、欠落セーブでも安全に空で復元できるようにする＝後方互換）。
    /// </summary>
    public ImmutableList<Equipment> Inventory { get; init; }
        = ImmutableList<Equipment>.Empty;
}

// ─── 純粋変換ユーティリティ（Godot 非依存・テスト可能） ──────────────────────

/// <summary>
/// 不変レコード状態と JSON 文字列の相互変換を提供する静的ユーティリティ。
/// 実ファイル I/O は持たず、完全に純粋（副作用なし・Godot 非依存）。
/// </summary>
public static class SaveSerializer
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// セーブスキーマのバージョン。破壊的変更時にインクリメントする。
    /// v2: 旅団史（ChronicleLog）を追加（旧 v1 セーブは ChronicleLog 欠落 → 空配列で後方互換復元）。
    /// v3: 血統リンク（Unit.Parentage / Unit.SpouseId）を追加（旧 v1/v2 セーブは欠落 → null で
    ///     後方互換復元。生者同士の親子・婚姻の縦横軸を永続化し、家系図をロード後も再構築可能にする）。
    /// v4: 性別（Unit.Gender）を追加（旧 v1〜v3 セーブは欠落 → 既定 Male で後方互換復元。
    ///     婚姻の男女ペア制約をロード後も維持する）。
    /// v5: 旅団共有の持ち物（Inventory: 未装着の装備個体）を追加（旧 v1〜v4 セーブは欠落 →
    ///     空リストで後方互換復元。Affix 付きドロップ等を外して保管しても消えない土台）。
    /// v6: 旅団史 ChronicleLogEntry に Gender を追加（旧 v1〜v5 セーブは欠落 → 既定 Male で
    ///     後方互換復元。年代記ログに性別別のジョブ立ち絵アイコンを出すため）。
    /// </summary>
    public const int CurrentSaveVersion = 6;

    // ─── JSON シリアライズ設定 ────────────────────────────────────────────

    /// <summary>
    /// 全 serialize / deserialize 共通のオプション。
    ///   - WriteIndented: 人間が読めるインデント付き JSON
    ///   - JsonStringEnumConverter: enum を文字列で保存（"Sniper" 等）し可読性と
    ///     並べ替え耐性を確保（数値だと enum 定義順変更でデータ破損する）
    ///   - WhenWritingNull: null プロパティ（装備なし等）は出力から省略
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ─── 純粋変換 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 4 つの状態を JSON 文字列へ変換する純粋関数。Godot ランタイム不要。
    /// </summary>
    /// <param name="economy">保存するポイント経済。</param>
    /// <param name="timeline">保存する予言タイムライン。</param>
    /// <param name="roster">保存する旅団員リスト。</param>
    /// <param name="chronicleLog">保存する旅団史（年代記ナレーションの素材）。空でも可。</param>
    public static string Serialize(
        PointsEconomy economy,
        TimelineEngine timeline,
        IReadOnlyList<Unit> roster,
        IReadOnlyList<ChronicleLogEntry> chronicleLog,
        IReadOnlyList<Equipment>? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(chronicleLog);

        var dto = new SaveDataDto
        {
            Version      = CurrentSaveVersion,
            SavedAtUtc   = DateTime.UtcNow.ToString("o"),
            Economy      = ToDto(economy),
            Timeline     = ToDto(timeline),
            Roster       = roster.Select(ToDto).ToList(),
            ChronicleLog = chronicleLog.Select(ToDto).ToList(),
            // 持ち物（v5）。未指定（旧シグネチャ呼び出し）は空として保存する。
            Inventory    = (inventory ?? Enumerable.Empty<Equipment>()).Select(ToDto).ToList(),
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>
    /// JSON 文字列から 3 つの状態を復元する純粋関数。Godot ランタイム不要。
    /// パースに失敗、または必須セクション欠落の場合は null を返す（例外を投げない）。
    /// </summary>
    public static LoadedGameState? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        SaveDataDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SaveDataDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null; // 壊れた / 互換性のない JSON
        }

        if (dto is null || dto.Economy is null || dto.Timeline is null) return null;

        return new LoadedGameState
        {
            Economy  = FromDto(dto.Economy),
            Timeline = FromDto(dto.Timeline),
            Roster   = (dto.Roster ?? new List<UnitDto>())
                       .Select(FromDto)
                       .ToImmutableList(),
            // 旧 v1 セーブには ChronicleLog が無い → null → 空配列で後方互換復元。
            ChronicleLog = (dto.ChronicleLog ?? new List<ChronicleLogEntryDto>())
                           .Select(FromDto)
                           .ToImmutableArray(),
            // 旧 v1〜v4 セーブには Inventory が無い → null → 空リストで後方互換復元。
            Inventory = (dto.Inventory ?? new List<EquipmentDto>())
                        .Select(FromDto)
                        .ToImmutableList(),
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    //  record → DTO（保存方向のマッピング）
    // ════════════════════════════════════════════════════════════════════════

    private static EconomyDto ToDto(PointsEconomy e) => new()
    {
        CurrentBalance = e.CurrentBalance,
        TotalEarned    = e.TotalEarned,
        TotalSpent     = e.TotalSpent,
    };

    private static TimelineDto ToDto(TimelineEngine t) => new()
    {
        Turn           = t.Turn,
        CurrentOptions = t.CurrentOptions.Select(ToDto).ToList(),
    };

    private static ProphecyDto ToDto(Prophecy p) => new()
    {
        Id             = p.Id,
        Kind           = p.Kind,
        SkipYears      = p.SkipYears,
        Value          = p.Value,
        DescriptionKey = p.DescriptionKey,
    };

    private static UnitDto ToDto(Unit u) => new()
    {
        Id            = u.Id,
        Job           = u.Job,
        Age           = u.Age,
        MaxAge        = u.MaxAge,
        FirstNameKey  = u.FirstNameKey,
        LastNameKey   = u.LastNameKey,
        Origin        = u.Origin,
        Gender        = u.Gender,
        Level         = u.Level,
        MainEquipment = u.MainEquipment is null ? null : ToDto(u.MainEquipment),
        // Guid キーは JSON 互換性のため文字列キー辞書へ正規化
        BattleAffinity = u.BattleAffinity.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value),
        IsDead = u.IsDead,
        // 血統リンク（v3）。婚姻で生まれた子のみ非 null（初代は null で省略される）。
        Parentage = u.Parentage is null ? null : ToDto(u.Parentage),
        SpouseId = u.SpouseId,
    };

    private static ParentageDto ToDto(Parentage p) => new()
    {
        FatherId = p.FatherId,
        MotherId = p.MotherId,
    };

    private static EquipmentDto ToDto(Equipment e) => new()
    {
        Id        = e.Id,
        ItemId    = e.ItemId,
        Level     = e.Level,
        AffixKeys = e.AffixKeys.ToList(),
    };

    private static ChronicleLogEntryDto ToDto(ChronicleLogEntry c) => new()
    {
        Generation       = c.Generation,
        Kind             = c.Kind,
        UnitFirstNameKey = c.UnitFirstNameKey,
        UnitLastNameKey  = c.UnitLastNameKey,
        Job              = c.Job,
        Gender           = c.Gender,
        Age              = c.Age,
        FromLevel        = c.FromLevel,
        ToLevel          = c.ToLevel,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  DTO → record（復元方向のマッピング）
    // ════════════════════════════════════════════════════════════════════════

    private static PointsEconomy FromDto(EconomyDto d) => new()
    {
        CurrentBalance = d.CurrentBalance,
        TotalEarned    = d.TotalEarned,
        TotalSpent     = d.TotalSpent,
    };

    private static TimelineEngine FromDto(TimelineDto d)
    {
        var options = (d.CurrentOptions ?? new List<ProphecyDto>())
            .Select(FromDto)
            .ToImmutableArray();
        return new TimelineEngine
        {
            Turn           = d.Turn,
            CurrentOptions = options,
        };
    }

    private static Prophecy FromDto(ProphecyDto d) => new()
    {
        Id             = d.Id,
        Kind           = d.Kind,
        SkipYears      = d.SkipYears,
        Value          = d.Value,
        DescriptionKey = d.DescriptionKey ?? string.Empty,
    };

    private static Unit FromDto(UnitDto d)
    {
        var affinity = ImmutableDictionary<Guid, int>.Empty;
        if (d.BattleAffinity is { Count: > 0 })
        {
            var builder = ImmutableDictionary.CreateBuilder<Guid, int>();
            foreach (var kv in d.BattleAffinity)
            {
                if (Guid.TryParse(kv.Key, out var g))
                {
                    builder[g] = kv.Value;
                }
            }
            affinity = builder.ToImmutable();
        }

        return new Unit
        {
            Id             = d.Id,
            Job            = d.Job,
            Age            = d.Age,
            MaxAge         = d.MaxAge,
            FirstNameKey   = d.FirstNameKey ?? string.Empty,
            LastNameKey    = d.LastNameKey ?? string.Empty,
            Origin         = d.Origin,
            Gender         = d.Gender,
            Level          = d.Level,
            MainEquipment  = d.MainEquipment is null ? null : FromDto(d.MainEquipment),
            BattleAffinity = affinity,
            IsDead         = d.IsDead,
            // 血統リンク（v3）。旧 v1/v2 セーブは欠落 → null で後方互換復元。
            Parentage      = d.Parentage is null ? null : FromDto(d.Parentage),
            SpouseId       = d.SpouseId,
        };
    }

    private static Parentage FromDto(ParentageDto d) => new()
    {
        FatherId = d.FatherId,
        MotherId = d.MotherId,
    };

    private static Equipment FromDto(EquipmentDto d) => new()
    {
        Id        = d.Id,
        ItemId    = d.ItemId,
        Level     = d.Level,
        AffixKeys = (d.AffixKeys ?? new List<string>()).ToImmutableArray(),
    };

    private static ChronicleLogEntry FromDto(ChronicleLogEntryDto d) => new()
    {
        Generation       = d.Generation,
        Kind             = d.Kind,
        UnitFirstNameKey = d.UnitFirstNameKey ?? string.Empty,
        UnitLastNameKey  = d.UnitLastNameKey ?? string.Empty,
        Job              = d.Job,
        Gender           = d.Gender,
        Age              = d.Age,
        FromLevel        = d.FromLevel,
        ToLevel          = d.ToLevel,
    };

    // ════════════════════════════════════════════════════════════════════════
    //  ディスクスキーマ DTO（可変・System.Text.Json 用）
    // ════════════════════════════════════════════════════════════════════════
    //  ドメインレコードとは独立した「保存フォーマット」の定義。
    //  すべて get/set の可変プロパティ + パラメータレスコンストラクタを持ち、
    //  System.Text.Json が素直にラウンドトリップできる形にしてある。

    /// <summary>セーブファイル全体のルート DTO。</summary>
    private sealed class SaveDataDto
    {
        /// <summary>スキーマバージョン（前方/後方互換判定用）。</summary>
        public int Version { get; set; }

        /// <summary>保存日時（ISO 8601 / round-trip 形式）。表示・デバッグ用。</summary>
        public string SavedAtUtc { get; set; } = string.Empty;

        public EconomyDto? Economy { get; set; }
        public TimelineDto? Timeline { get; set; }
        public List<UnitDto> Roster { get; set; } = new();

        /// <summary>
        /// 旅団史（年代記ナレーションの素材）。v2 で追加。旧 v1 セーブには無く JSON 上で欠落するため
        /// nullable とし、Deserialize 側で null → 空配列に正規化する（後方互換）。
        /// </summary>
        public List<ChronicleLogEntryDto>? ChronicleLog { get; set; }

        /// <summary>
        /// 旅団共有の持ち物（未装着の装備個体）。v5 で追加。旧 v1〜v4 セーブには無く JSON 上で
        /// 欠落するため nullable とし、Deserialize 側で null → 空リストに正規化する（後方互換）。
        /// </summary>
        public List<EquipmentDto>? Inventory { get; set; }
    }

    /// <summary>PointsEconomy の保存形。</summary>
    private sealed class EconomyDto
    {
        public int CurrentBalance { get; set; }
        public int TotalEarned { get; set; }
        public int TotalSpent { get; set; }
    }

    /// <summary>TimelineEngine の保存形。</summary>
    private sealed class TimelineDto
    {
        public int Turn { get; set; }
        public List<ProphecyDto> CurrentOptions { get; set; } = new();
    }

    /// <summary>Prophecy の保存形。</summary>
    private sealed class ProphecyDto
    {
        public Guid Id { get; set; }
        public ProphecyKind Kind { get; set; }
        public int SkipYears { get; set; }
        public int Value { get; set; }
        public string DescriptionKey { get; set; } = string.Empty;
    }

    /// <summary>Unit の保存形。</summary>
    private sealed class UnitDto
    {
        public Guid Id { get; set; }
        public JobId Job { get; set; }
        public int Age { get; set; }
        public int MaxAge { get; set; }
        public string FirstNameKey { get; set; } = string.Empty;
        public string LastNameKey { get; set; } = string.Empty;
        /// <summary>命名文化圏（血統属性）。旧セーブ互換のため既定 European。</summary>
        public Origin Origin { get; set; } = Origin.European;
        /// <summary>性別（婚姻の男女ペア制約軸）。v4 で追加。旧セーブ欠落時は既定 Male。</summary>
        public Gender Gender { get; set; } = Gender.Male;
        public int Level { get; set; }
        public EquipmentDto? MainEquipment { get; set; }
        /// <summary>Guid → ポイントの好感度。Guid は文字列キーへ正規化して保存。</summary>
        public Dictionary<string, int> BattleAffinity { get; set; } = new();
        public bool IsDead { get; set; }

        /// <summary>
        /// 血統リンク（父母 Id）。v3 で追加。婚姻で生まれた子のみ非 null。
        /// 旧 v1/v2 セーブには無く JSON 上で欠落するため nullable とし、FromDto 側で null 許容。
        /// </summary>
        public ParentageDto? Parentage { get; set; }

        /// <summary>
        /// 配偶者ユニット Id。v3 で追加。未婚は null（JSON 上で省略される）。
        /// 旧 v1/v2 セーブには無く欠落するため nullable とし、FromDto 側で null 許容。
        /// </summary>
        public Guid? SpouseId { get; set; }
    }

    /// <summary>Parentage（血統リンク）の保存形。v3 で追加。</summary>
    private sealed class ParentageDto
    {
        public Guid FatherId { get; set; }
        public Guid MotherId { get; set; }
    }

    /// <summary>Equipment の保存形。</summary>
    private sealed class EquipmentDto
    {
        public Guid Id { get; set; }
        public ItemId ItemId { get; set; }
        public int Level { get; set; }
        public List<string> AffixKeys { get; set; } = new();
    }

    /// <summary>
    /// ChronicleLogEntry（旅団史の一行）の保存形。enum Kind は文字列で保存し、
    /// 表示テキストは持たない（名前キー・JobId・数値だけ）＝ロード後に UI が localization 解決する。
    /// </summary>
    private sealed class ChronicleLogEntryDto
    {
        public int Generation { get; set; }
        public ChronicleEventKind Kind { get; set; }
        public string UnitFirstNameKey { get; set; } = string.Empty;
        public string UnitLastNameKey { get; set; } = string.Empty;
        public JobId Job { get; set; }
        // v6 で追加。旧セーブには無く JSON 上で欠落 → enum 既定 Male で後方互換復元。
        public Gender Gender { get; set; } = Gender.Male;
        public int Age { get; set; }
        public int FromLevel { get; set; }
        public int ToLevel { get; set; }
    }
}
