// =============================================================================
//  ChronicleKnights — Prophecy.cs
// -----------------------------------------------------------------------------
//  プレイヤーがタイムライン上で選択する「予言 (Prophecy)」1 件分を表す
//  完全不変なレコード。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  セクション 1 + フェーズ 3) の中核データ:
//
//    - プレイヤーは 3 つの予言から 1 つを選ぶ
//    - 選んだ予言の SkipYears だけ時間が一瞬で進む（タイムスキップ）
//    - 予言の Kind に応じた結果が発生（ポイント獲得 / 戦闘 / 加入 等）
//    - 結果解決後、次の 3 つの予言が新たに提示される
//
//  ★ 重要設計判断:
//    本 record は「次の予言のリスト」を一切持たない。これは「次の未来は
//    現在の選択を確定するまで見えない」という設計憲法を構造で保証するため。
//    予言ツリーの分岐生成は TimelineEngine（後続翻訳）が司る責務とする。
//
//  本ファイルには:
//    - 予言の不変構造（Id / Kind / SkipYears / Value / DescriptionKey）
//    - 予言種別の enum（拡張可能、将来 Affix システムと連携可能）
//  含まれないもの:
//    - 日本語表示テキスト   → Config/localization_ja.json (キーで引く)
//    - 数値ハードコード     → ProphecyMaster で集中管理する想定
//    - 効果解決ロジック     → TimelineEngine / BattleManager の責務
// =============================================================================

using System;

namespace ChronicleKnights.Core.Timeline;

// ─── 予言の種類 ─────────────────────────────────────────────────────────────

/// <summary>
/// プレイヤーに提示される予言の種類。
///
/// 各 Kind は「タイムスキップ後に何が起きるか」を一意に決める。具体的な
/// 効果値（獲得ポイント数・敵レベル・加入人数など）は Prophecy.Value で
/// 持ち、その意味は本 enum で文脈解決される。
///
/// 表示ラベル・フレーバーは Config/localization_ja.json の prophecies
/// セクションから enum 名をキーに引く（コード側にはハードコード一切なし）。
///
/// 拡張時は本 enum に項目追加 → ProphecyMaster で生成ルール定義 →
/// localization_ja.json にキー追加、の 3 段階で完結する。
/// </summary>
public enum ProphecyKind
{
    /// <summary>
    /// 婚姻ポイントを獲得する安全な予言。
    /// Value = 獲得ポイント数。タイムスキップ中の最低保証収入とは別枠の
    /// ボーナス収入を提供する（憲法セクション 4-2 参照）。
    /// </summary>
    RewardPoints,

    /// <summary>
    /// 敵との戦闘が発生する予言。
    /// Value = 敵のレベル/強さ係数。撃破ポイントが大きいが、ユニットが
    /// 死亡 (Unit.IsDead = true) するリスクを伴う（完全ロスト仕様）。
    /// </summary>
    Battle,

    /// <summary>
    /// 新ユニットが旅団に加入する予言。
    /// Value = 加入人数。タイムスキップ中に出会う志願者・流れ者・
    /// 元エリート等を表現する。
    /// </summary>
    ScoutReward,

    /// <summary>
    /// 装備がドロップする予言。
    /// Value = ドロップ装備のレベル（Equipment.Level 1〜5）。
    /// Equipment.MaxEquipmentLevel を超える Value は呼び出し側でクランプする。
    /// </summary>
    EquipmentDrop,

    /// <summary>
    /// 休息の予言（戦闘も報酬もなくタイムスキップのみ）。
    /// Value = 休息効果の強度（HP 回復量倍率や経験値減衰の緩和等、
    /// 解釈は TimelineEngine 側に委ねる）。
    /// </summary>
    Rest,
}

// ─── 予言レコード ───────────────────────────────────────────────────────────

/// <summary>
/// プレイヤーに提示される 1 つの予言。完全不変。
///
/// ライフサイクル:
///   1. TimelineEngine が 3 つの Prophecy を生成（毎ターン頭の選択肢）
///   2. プレイヤーがいずれか 1 つを選択
///   3. SkipYears だけ全旅団員の Age が進む (Unit.WithAgeProgress)
///   4. Kind に応じた効果が解決される（ポイント加算・戦闘発生・加入等）
///   5. 次の 3 つの Prophecy が新たに生成される（このとき前回選択した
///      Prophecy 自体は破棄される）
/// </summary>
public sealed record Prophecy
{
    // ─── 必須プロパティ ───────────────────────────────────────────────────

    /// <summary>
    /// 予言の一意な識別子。同じ Kind / Value の予言でも個体ごとに別 Guid を持ち、
    /// UI 上での選択追跡・ログ記録・E2E テストのアサート等に使う。
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// 予言の種類。タイムスキップ後の効果解決を一意に決定する。
    /// </summary>
    public required ProphecyKind Kind { get; init; }

    /// <summary>
    /// この予言を選択した際に一瞬で経過するタイムスキップ年数。
    /// 想定範囲は 2 〜 4 年（憲法フェーズ 3 「30 戦闘 ≒ 30 年」の時間軸圧縮に
    /// 整合する標準値）。生成ルールは ProphecyMaster で管理する。
    /// </summary>
    public required int SkipYears { get; init; }

    /// <summary>
    /// 予言の効果量。意味は Kind により決まる:
    ///   RewardPoints  → 獲得ポイント数
    ///   Battle        → 敵のレベル/強さ係数
    ///   ScoutReward   → 加入人数
    ///   EquipmentDrop → ドロップ装備のレベル (1〜5)
    ///   Rest          → 休息効果の強度
    /// </summary>
    public required int Value { get; init; }

    /// <summary>
    /// プレイヤーに予言内容を表示するための Config 引き当て用キー。
    /// 日本語テキストは Config/localization_ja.json の prophecies セクションから
    /// このキーで解決する。例: "prophecy-reward-points-small",
    /// "prophecy-elite-battle-y20" 等の意味的識別子を想定。
    /// </summary>
    public required string DescriptionKey { get; init; }
}
