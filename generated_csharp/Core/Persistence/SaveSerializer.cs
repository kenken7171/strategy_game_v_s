// =============================================================================
//  ChronicleKnights — SaveSerializer.cs
// -----------------------------------------------------------------------------
//  セーブデータの「状態 ⇄ JSON 文字列」純粋変換層（Godot 完全非依存）。
//
//  保存対象は ChronicleGlobal が保持する 3 つの不変レコード状態:
//    1. PointsEconomy        — ポイント一元経済の財布
//    2. TimelineEngine       — 予言タイムラインの現在状態
//    3. ImmutableList<Unit>  — 大隊の全旅団員リスト
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
}

// ─── 純粋変換ユーティリティ（Godot 非依存・テスト可能） ──────────────────────

/// <summary>
/// 不変レコード状態と JSON 文字列の相互変換を提供する静的ユーティリティ。
/// 実ファイル I/O は持たず、完全に純粋（副作用なし・Godot 非依存）。
/// </summary>
public static class SaveSerializer
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>セーブスキーマのバージョン。破壊的変更時にインクリメントする。</summary>
    public const int CurrentSaveVersion = 1;

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
    /// 3 つの状態を JSON 文字列へ変換する純粋関数。Godot ランタイム不要。
    /// </summary>
    public static string Serialize(
        PointsEconomy economy,
        TimelineEngine timeline,
        IReadOnlyList<Unit> roster)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(roster);

        var dto = new SaveDataDto
        {
            Version    = CurrentSaveVersion,
            SavedAtUtc = DateTime.UtcNow.ToString("o"),
            Economy    = ToDto(economy),
            Timeline   = ToDto(timeline),
            Roster     = roster.Select(ToDto).ToList(),
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
        Level         = u.Level,
        MainEquipment = u.MainEquipment is null ? null : ToDto(u.MainEquipment),
        // Guid キーは JSON 互換性のため文字列キー辞書へ正規化
        BattleAffinity = u.BattleAffinity.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value),
        IsDead = u.IsDead,
    };

    private static EquipmentDto ToDto(Equipment e) => new()
    {
        Id        = e.Id,
        ItemId    = e.ItemId,
        Level     = e.Level,
        AffixKeys = e.AffixKeys.ToList(),
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
            Level          = d.Level,
            MainEquipment  = d.MainEquipment is null ? null : FromDto(d.MainEquipment),
            BattleAffinity = affinity,
            IsDead         = d.IsDead,
        };
    }

    private static Equipment FromDto(EquipmentDto d) => new()
    {
        Id        = d.Id,
        ItemId    = d.ItemId,
        Level     = d.Level,
        AffixKeys = (d.AffixKeys ?? new List<string>()).ToImmutableArray(),
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
        public int Level { get; set; }
        public EquipmentDto? MainEquipment { get; set; }
        /// <summary>Guid → ポイントの好感度。Guid は文字列キーへ正規化して保存。</summary>
        public Dictionary<string, int> BattleAffinity { get; set; } = new();
        public bool IsDead { get; set; }
    }

    /// <summary>Equipment の保存形。</summary>
    private sealed class EquipmentDto
    {
        public Guid Id { get; set; }
        public ItemId ItemId { get; set; }
        public int Level { get; set; }
        public List<string> AffixKeys { get; set; } = new();
    }
}
