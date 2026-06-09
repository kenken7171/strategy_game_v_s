// =============================================================================
//  ChronicleKnights — JobData.cs
// -----------------------------------------------------------------------------
//  ジョブ・パッシブ・配置ガイドの型定義のみを宣言する型システム層。
//
//  本ファイルは「形」だけを定義し、具体的な数値（HP, ATK, BDF 等の値）や
//  人間可読な日本語テキスト（役割・使い方・フレーバー・パッシブの表示名）は
//  一切含まない。
//
//  数値データの SoT     : JobMaster.cs
//  日本語ローカリゼーション: Config/localization_ja.json
//
//  設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md) 準拠:
//   - すべての値型は record / sealed record で完全不変
//   - 略称 (BDF/SDF/AB/HL) は内部コードから完全廃止し正式名称で宣言
//   - ジョブ名・配置 row は型安全な enum
//   - フェーズ 3 (手動婚姻ポイントシステム) との拡張連携点として
//     JobMarriageHooks を用意
// =============================================================================

using System.Collections.Immutable;

namespace ChronicleKnights.Core.Job;

// ─── ジョブ識別子 ───────────────────────────────────────────────────────────

/// <summary>
/// 全 8 ジョブの識別子。TypeScript 版 JobType 文字列 union を enum 化したもの。
/// 拡張時はここに項目追加 → JobMaster.All にエントリ追加 → localization_ja.json
/// に対応キー追加、の 3 段階で完結する。
/// </summary>
public enum JobId
{
    IronWallKnight,
    HeavyInfantry,
    StandardBearer,
    Tactician,
    Medic,
    Sniper,
    Sorcerer,
    Scout,
}

// ─── 配置 row（V 字フォーメーション） ──────────────────────────────────────

/// <summary>
/// V 字フォーメーションの分隊位置。
/// Front (中央上)、RearLeft (左下)、RearRight (右下) の三角配置。
/// UI 表示は localization_ja.json の squadRows セクションから引く。
/// </summary>
public enum SquadRow
{
    Front,
    RearLeft,
    RearRight,
}

// ─── パッシブの種別（略称完全廃止・正式名称化） ────────────────────────────

/// <summary>
/// ジョブが持つパッシブ能力の種別。
///
/// 命名規約: コード内には BDF / SDF / AB / HL 等の略称を一切使わない。
/// 略称と正式名称の対応:
///   BDF (Battalion Defense) ─────► BattalionDefense
///   SDF (Squad Defense)     ─────► SquadDefense
///   AB  (Ability Buff)      ─────► InitiativeBuff
///   HL  (Heal)              ─────► TurnEndSquadHeal
///   sniper 2連撃             ─────► ConsecutiveStrike
///
/// UI 表示ラベル（🛡️ 大隊総守護力 等）は localization_ja.json の
/// passives セクションから enum 名をキーに引く。
///
/// スコープ分類（コメント末尾の (X) を参照）:
///   (A) Brigade Scope — 大隊全体に常駐
///   (B) Turn Scope    — ターン頭 or ターン末で発動
///   (C) Action Scope  — 特定行動（攻撃/被弾）の瞬間のみ
/// </summary>
public enum PassiveKind
{
    /// <summary>
    /// 大隊全員の受けるダメージを軽減 (C — Damage Scope)。
    /// FRONT 配置時のみ発動する条件付きパッシブ。
    /// </summary>
    BattalionDefense,

    /// <summary>
    /// 自分隊（所属分隊）の受けるダメージを軽減 (C — Damage Scope)。
    /// 配置 row 問わず常時発動。
    /// </summary>
    SquadDefense,

    /// <summary>
    /// ターン開始時に大隊全員（自分以外）の速度・攻撃力を底上げ (A — Brigade)。
    /// 重ね掛け可能。
    /// </summary>
    InitiativeBuff,

    /// <summary>
    /// ターン終了時に所属分隊の生存者全員を HP 回復 (B — Turn End)。
    /// 最大 HP でキャップされる。
    /// </summary>
    TurnEndSquadHeal,

    /// <summary>
    /// イニシアチブ 1 番手かつ分隊先頭スロット時、通常攻撃が連続 2 回行われる
    /// (C — Action Scope)。数値プロパティを持たない binary パッシブ。
    /// </summary>
    ConsecutiveStrike,
}

// ─── 効果範囲 / 効果系統 ───────────────────────────────────────────────────

/// <summary>
/// パッシブ能力の効果が届く範囲（スコープ）。BattleManager 翻訳時の
/// 適用ロジック分岐と、UI のミニ V 字図のハイライト対象決定の両方に使う。
/// </summary>
public enum EffectScope
{
    /// <summary>自分自身のみ</summary>
    SelfOnly,
    /// <summary>所属分隊のみ（例: SquadDefense, TurnEndSquadHeal）</summary>
    OwnSquad,
    /// <summary>大隊全体（例: InitiativeBuff）</summary>
    EntireBattalion,
    /// <summary>FRONT 配置時のみ大隊全体（例: BattalionDefense）</summary>
    EntireBattalionWhenFront,
    /// <summary>自分の所属 row（攻撃などの個別アクション）</summary>
    OwnRow,
}

/// <summary>
/// ジョブの効果系統。UI 配色を決定する（青/金/緑/朱）。
/// 表示名は localization_ja.json の effectKinds セクションから引く。
/// </summary>
public enum EffectKind
{
    /// <summary>防御系（鉄壁騎士・重装歩兵）— UI 配色: 青</summary>
    Defend,
    /// <summary>支援系（旗手・戦術官）— UI 配色: 金</summary>
    Buff,
    /// <summary>回復系（衛生兵）— UI 配色: 緑</summary>
    Heal,
    /// <summary>攻撃系（狙撃兵・呪術師・斥候）— UI 配色: 朱</summary>
    Attack,
}

// ─── ジョブ基礎ステータス（完全不変） ─────────────────────────────────────

/// <summary>
/// 1 ジョブのデフォルト戦闘ステータス。
///
/// プロパティ名はすべて正式名称（BDF/SDF/AB/HL 等の略称使用は禁止）。
/// 全フィールド required の init-only — 一度生成したら絶対に変更されない。
/// </summary>
public sealed record JobStats
{
    /// <summary>最大 HP。容量値のため成長係数を掛けない。</summary>
    public required int MaxHp { get; init; }

    /// <summary>素のスピード値（バフ前の基礎値）。</summary>
    public required int Speed { get; init; }

    /// <summary>FRONT スロット配置時の攻撃力。</summary>
    public required int FrontAttack { get; init; }

    /// <summary>REAR スロット配置時の攻撃力（FA と独立）。</summary>
    public required int RearAttack { get; init; }

    /// <summary>
    /// 大隊全員の被ダメージ軽減量（旧 BDF）。
    /// FRONT 配置時のみ発動する条件付き軽減。所属する全分隊に効果が届く。
    /// </summary>
    public required int BattalionDefense { get; init; }

    /// <summary>
    /// 所属分隊の被ダメージ軽減量（旧 SDF）。
    /// 配置 row 問わず常時発動し、自分隊のみに効果が届く。
    /// </summary>
    public required int SquadDefense { get; init; }

    /// <summary>
    /// ターン開始時に大隊全員（自分以外）へ撒く攻撃力バフ量（旧 AB）。
    /// 同時に速度バフも自身の Speed 値分撒かれる（BattleManager 仕様）。
    /// </summary>
    public required int InitiativeBuff { get; init; }

    /// <summary>
    /// ターン終了時に所属分隊の生存者全員を回復する HP 量（旧 HL）。
    /// </summary>
    public required int TurnEndSquadHeal { get; init; }
}

// ─── 配置ガイド ────────────────────────────────────────────────────────────

/// <summary>
/// ジョブの推奨配置 + 効果範囲メタデータ。
///
/// UI（「ジョブ説明」のミニ V 字図と推奨配置ヘッドライン）の表示を駆動する。
/// テキスト（headline / effectRangeNote）はこのレコードに含めず、ローカリ
/// ゼーション JSON 側 (jobs.&lt;JobId&gt;.formationGuide.*) で管理する。
/// </summary>
public sealed record JobFormationGuide
{
    /// <summary>推奨される配置 row（V 字図でハイライト対象になる行）。</summary>
    public required ImmutableArray<SquadRow> RecommendedRows { get; init; }

    /// <summary>このジョブの能力が届く範囲（UI で副ハイライト）。</summary>
    public required ImmutableArray<SquadRow> EffectRange { get; init; }

    /// <summary>効果範囲スコープ（条件付き発動など意味的種別）。</summary>
    public required EffectScope EffectScope { get; init; }

    /// <summary>効果系統。UI のハイライト色を決定（青/金/緑/朱）。</summary>
    public required EffectKind EffectKind { get; init; }
}

// ─── 婚姻ポイントシステムとの連携メタデータ ────────────────────────────────

/// <summary>
/// ジョブごとの「手動婚姻ポイントシステム」連携メタデータ。
///
/// docs/MIGRATION_GODOT_HACK_AND_SLASH.md セクション 4 で定義された
/// 手動婚姻ポイントシステムへの拡張フック。
///
///   - MarriageCostMultiplier   : このジョブのユニットを結婚相手にする時の
///                                必要ポイントに掛ける倍率（既定 1.0）。
///                                強力なジョブほど高く設定して「エリート配合は
///                                ハイコスト」の戦略性を演出する。
///
///   - MarriagePointGainOnKill  : このジョブのユニットが敵を撃破した時に
///                                追加で獲得する婚姻ポイント（既定 0）。
///                                支援系ジョブの貢献を可視化したり、
///                                「攻撃役を活躍させると配合が捗る」設計が可能。
///
///   - ChildPassiveInheritanceBonus :
///         このジョブのユニットを親にした時、子のパッシブ Affix 継承確率に
///         上乗せされる係数（既定 0.0）。フェーズ 3 で本格利用。
///
/// フェーズ 1（現時点）では全ジョブを既定値で初期化し、フェーズ 2 の Affix
/// システム導入後にバランス調整するための土台とする。
/// </summary>
public sealed record JobMarriageHooks
{
    public double MarriageCostMultiplier { get; init; } = 1.0;
    public int MarriagePointGainOnKill { get; init; } = 0;
    public double ChildPassiveInheritanceBonus { get; init; } = 0.0;
}

// ─── ジョブ定義集約 ────────────────────────────────────────────────────────

/// <summary>
/// 1 ジョブの完全な不変定義。
///
/// 数値（JobStats）・配置メタ（JobFormationGuide）・婚姻連携
/// （JobMarriageHooks）・ロール貢献値（RoleBonus）・特殊パッシブ（数値プロパ
/// ティで表現できない種別）を集約する。
///
/// ローカリゼーション文字列（名前・役割・使い方・フレーバー・パッシブ表示名）
/// は一切含まない。それらは localization_ja.json から enum 名をキーに引く。
/// </summary>
public sealed record JobDefinition
{
    public required JobId Id { get; init; }
    public required JobStats Stats { get; init; }
    public required JobFormationGuide FormationGuide { get; init; }
    public required JobMarriageHooks MarriageHooks { get; init; }

    /// <summary>
    /// UI 比較用のロール貢献ボーナス（純戦闘ステで不利になりがちな
    /// バフ役・回復役・防御役を底上げするための加算値）。
    /// CalculateTargetRating でジョブ基準値に加算される。
    /// </summary>
    public required int RoleBonus { get; init; }

    /// <summary>
    /// 数値プロパティ（JobStats）で表現できない特殊パッシブの集合。
    /// 現時点では ConsecutiveStrike（sniper の二の矢）のみ。
    /// </summary>
    public ImmutableArray<PassiveKind> SpecialPassives { get; init; }
        = ImmutableArray<PassiveKind>.Empty;
}

// ─── ローカリゼーションキー解決ヘルパー ───────────────────────────────────

/// <summary>
/// localization_ja.json への参照キーを型安全に組み立てる純粋関数群。
///
/// 使用例:
///   var key = LocalizationKeys.PassiveLabel(PassiveKind.BattalionDefense);
///   // "passives.BattalionDefense.label"
///   var label = locale.Resolve(key); // → "🛡️ 大隊総守護力"
///
/// JSON 側のキー命名規約と完全一致させている。マジックストリングを
/// 呼び出し側に漏らさないための薄いラッパー。
/// </summary>
public static class LocalizationKeys
{
    public static string PassiveLabel(PassiveKind kind) => $"passives.{kind}.label";
    public static string PassiveScope(PassiveKind kind) => $"passives.{kind}.scope";
    public static string PassiveDescription(PassiveKind kind) => $"passives.{kind}.description";

    public static string JobName(JobId id)             => $"jobs.{id}.name";
    public static string JobRole(JobId id)             => $"jobs.{id}.role";
    public static string JobAbilitySummary(JobId id)   => $"jobs.{id}.abilitySummary";
    public static string JobUsage(JobId id)            => $"jobs.{id}.usage";
    public static string JobFlavor(JobId id)           => $"jobs.{id}.flavor";
    public static string JobHeadline(JobId id)         => $"jobs.{id}.formationGuide.headline";
    public static string JobEffectRangeNote(JobId id)  => $"jobs.{id}.formationGuide.effectRangeNote";

    public static string SquadRowName(SquadRow row)    => $"squadRows.{row}";
    public static string EffectKindName(EffectKind k)  => $"effectKinds.{k}";
}
