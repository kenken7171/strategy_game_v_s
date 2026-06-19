// =============================================================================
//  ChronicleKnights — Equipment.cs
// -----------------------------------------------------------------------------
//  ユニットが装備するアイテム 1 個を表す完全不変なレコード。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  フェーズ 2 + 5 大初期アイテムマスターデータ) の中核データ:
//
//    - 装備種別: ItemId enum で 5 大マスターを型安全に識別
//    - レベル: 1〜5。Lv1〜2 は +1 加算、Lv3〜5 は累積乗算でスケール
//    - 基礎ステ: BaseStatsRegistry が ItemId → ItemBaseStats の SoT を提供
//    - AffinityMultiplier: 装備しているだけで自然婚姻ポイント獲得量をブースト
//                          基本式 1.0 + Level*0.1、RingPurelove は純愛補正で上乗せ
//    - HasGreedEffect: CoinGreed の強欲効果フラグ（LH 時にポイント強奪）
//
//  完全ロスト仕様: ユニットが戦闘中に死亡 (Unit.IsDead = true) した場合、
//  装着していた本 Equipment も同時にロストする（旅団から消滅）。これは
//  本 record 自体には責務がなく、戦闘終了時の RosterManager 側で
//  「死亡ユニットの MainEquipment を破棄する」処理を行う設計。
//
//  日本語表示テキスト（アイテム名・効果説明）は本ファイルに一切含まれず、
//  Config/localization_ja.json の items.{ItemId}.{name|description} から
//  enum 値の文字列をキーに引く。
//
//  略称 (BDF/SDF/AB/HL/FA/RA) は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Units;

// ─── アイテム識別子（5 大初期アイテム） ────────────────────────────────────

/// <summary>
/// 5 大初期アイテムマスターの型安全な識別子。
///
/// 文字列キー (item-iron-sword 等) を完全に排除し、Equipment の種別を
/// コンパイル時に検証可能な enum で表現する。各 ID の表示名・効果説明は
/// Config/localization_ja.json の items.{ItemId}.{name|description} から
/// enum 値の文字列 (ToString) で引く。
///
/// 拡張時は本 enum に項目追加 → BaseStatsRegistry にエントリ追加 →
/// localization_ja.json に items.{NewId} セクション追加、の 3 段階で完結。
/// </summary>
public enum ItemId
{
    /// <summary>聖剣系。前衛汎用、攻撃と分隊守護を両立。</summary>
    SwordKnight,
    /// <summary>魔弓系。狙撃兵の腕を引き出す純粋火力。</summary>
    BowSniper,
    /// <summary>魔杖系。後衛魔職向け、攻撃 + 突撃号令の支援要素。</summary>
    StaffMage,
    /// <summary>誓輪系。純愛補正で自然婚姻ポイント獲得を大幅ブースト。</summary>
    RingPurelove,
    /// <summary>古銭系。強欲効果でラストヒット時に追加ポイントを強奪。</summary>
    CoinGreed,
}

// ─── アイテム基礎ステータス（SoT 値レコード） ──────────────────────────────

/// <summary>
/// 1 アイテム種別の Lv1 時点の基礎ステータス。
/// 「攻撃力」「分隊守護力」「突撃号令」の純戦闘ステに加え、
/// アイテム固有の特殊効果（純愛補正・強欲効果）も保持する。
///
/// このレコードは BaseStatsRegistry の値として静的に初期化される SoT。
/// レベル補正は Equipment.ComputeScaledStat が動的に適用する責務。
/// </summary>
public sealed record ItemBaseStats
{
    /// <summary>基礎攻撃力。ComputeScaledStat でレベル補正される。</summary>
    public int AttackPower { get; init; }

    /// <summary>基礎分隊守護力（旧 SDF）。ComputeScaledStat でレベル補正される。</summary>
    public int SquadDefense { get; init; }

    /// <summary>基礎突撃号令量（旧 AB）。ComputeScaledStat でレベル補正される。</summary>
    public int InitiativeBuff { get; init; }

    /// <summary>
    /// 純愛補正のベース倍率。RingPurelove のみ &gt; 1.0 を持つ。
    /// AffinityMultiplier の最終値はこの値と (1.0 + Level*0.1) の積。
    /// 既定 1.0（補正なし）。
    /// </summary>
    public double BaseAffinityMultiplier { get; init; } = 1.0;

    /// <summary>
    /// 強欲効果フラグ。CoinGreed のみ true。
    /// 真のとき、装着ユニットがラストヒット (BattleManager.ExecuteLastHit) を
    /// 取った際、追加で婚姻ポイントを強奪する判定を発動する。
    /// 既定 false。
    /// </summary>
    public bool HasGreedEffect { get; init; } = false;
}

// ─── 装備本体 ──────────────────────────────────────────────────────────────

/// <summary>
/// 装備アイテム 1 個分の不変データ。
/// </summary>
public sealed record Equipment
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>装備レベルの上限（ハクスラ憲法仕様: 1〜5）。</summary>
    public const int MaxEquipmentLevel = 5;

    /// <summary>装備レベルの下限（生成直後の最低 Lv）。</summary>
    public const int MinEquipmentLevel = 1;

    /// <summary>
    /// Lv3 以上で適用される累積乗算係数。
    ///   index 0 → 1.2 (Lv3 で適用開始)
    ///   index 1 → 1.3 (Lv3 で適用開始)
    ///   index 2 → 1.4 (Lv4 で追加適用)
    ///   index 3 → 1.5 (Lv5 で追加適用)
    /// Level L (L>=3) では先頭 (L-1) 個を順次乗算する（Lv3=2 個、Lv4=3 個、Lv5=4 個）。
    /// </summary>
    private static readonly double[] LevelMultipliers = { 1.2, 1.3, 1.4, 1.5 };

    // ─── 5 大アイテムの基礎ステータス SoT ────────────────────────────────

    /// <summary>
    /// 5 大初期アイテムの Lv1 基礎ステータスを保持する不変辞書。
    /// 新規アイテム追加時は本辞書にエントリを足し、
    /// localization_ja.json の items.{NewItemId} に表示テキストを追加する。
    /// </summary>
    public static readonly ImmutableDictionary<ItemId, ItemBaseStats> BaseStatsRegistry
        = BuildBaseStatsRegistry();

    private static ImmutableDictionary<ItemId, ItemBaseStats> BuildBaseStatsRegistry()
    {
        var b = ImmutableDictionary.CreateBuilder<ItemId, ItemBaseStats>();

        // 🛡️ 誓いの聖剣 — 前衛汎用剣
        b.Add(ItemId.SwordKnight, new ItemBaseStats
        {
            AttackPower  = 3,
            SquadDefense = 1,
        });

        // 🎯 必中の魔弓 — 後衛純粋火力
        b.Add(ItemId.BowSniper, new ItemBaseStats
        {
            AttackPower = 4,
        });

        // ⚡ 賢者の破滅杖 — 後衛魔職、攻撃+号令
        b.Add(ItemId.StaffMage, new ItemBaseStats
        {
            AttackPower    = 2,
            InitiativeBuff = 1,
        });

        // 💞 絆の誓輪 — 純愛ルート専用、自然婚姻ポイント獲得をベース 1.5 倍ブースト
        b.Add(ItemId.RingPurelove, new ItemBaseStats
        {
            AttackPower            = 1,
            BaseAffinityMultiplier = 1.5,
        });

        // 🪙 強欲の古銭 — 強欲効果フラグ立て、LH 時にポイント強奪
        b.Add(ItemId.CoinGreed, new ItemBaseStats
        {
            AttackPower    = 1,
            HasGreedEffect = true,
        });

        return b.ToImmutable();
    }

    // ─── 必須プロパティ ───────────────────────────────────────────────────

    /// <summary>
    /// アイテムの一意な識別子。同じ ItemId の装備でも個体ごとに別 Guid を持ち、
    /// 個体追跡（誰が拾った、誰に装備されている、誰と一緒にロストした）を可能にする。
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// アイテム種別（5 大マスターのいずれか）。
    /// 表示テキストは Config/localization_ja.json の items.{ItemId}.{name|description}
    /// から enum 値の文字列で引く。プロパティ名と型名が同じ「Color, Color」パターン
    /// だが、C# の名前解決規則により問題なく動作する。
    /// </summary>
    public required ItemId ItemId { get; init; }

    /// <summary>
    /// 現在のアイテムレベル。MinEquipmentLevel (1) 〜 MaxEquipmentLevel (5) の範囲。
    /// ComputeScaledStat による動的計算で実効ステータスが算出される。
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// この個体が持つランダムな付加効果 (Affix) の識別キーリスト。
    /// 例: ["affix-marriage-point-bonus", "affix-spd-plus"]
    /// 各キーは AffixMaster で具体的な効果値・婚姻ポイント加算等に解決される。
    /// 既定値は空 (ImmutableArray&lt;string&gt;.Empty)。
    /// </summary>
    public IReadOnlyList<string> AffixKeys { get; init; } = ImmutableArray<string>.Empty;

    // ─── 基礎ステ参照ヘルパー ─────────────────────────────────────────────

    /// <summary>
    /// この装備の Lv1 基礎ステータス（SoT 参照）。
    /// 動的計算には ComputeScaledStat / CurrentAttackPower 等を使うこと。
    /// </summary>
    public ItemBaseStats BaseStats => BaseStatsRegistry[this.ItemId];

    // ─── レベル乗算ステータス計算 ─────────────────────────────────────────

    /// <summary>
    /// 基礎値からレベル補正を適用した動的ステータスを返す純粋関数。
    ///
    /// 計算ルール:
    ///   Lv1     : 基礎値
    ///   Lv2     : 基礎値 + 1
    ///   Lv3〜5  : (基礎値 + 1) × 累積乗算（LevelMultipliers の先頭 Level-1 個を乗算）
    ///
    /// 例（基礎値 = 3, SwordKnight.AttackPower）:
    ///   Lv1 →  3.000
    ///   Lv2 →  4.000  (3 + 1)
    ///   Lv3 →  6.240  ((3+1) × 1.2 × 1.3)
    ///   Lv4 →  8.736  ((3+1) × 1.2 × 1.3 × 1.4)
    ///   Lv5 → 13.104  ((3+1) × 1.2 × 1.3 × 1.4 × 1.5)
    ///
    /// 整数化が必要な場合は呼び出し側で Math.Floor / Math.Round 等を適用する
    /// （HP のような容量値は floor、ダメージは round 等、用途による）。
    /// </summary>
    public double ComputeScaledStat(int baseValue)
    {
        if (Level <= 1) return baseValue;
        if (Level == 2) return baseValue + 1;

        // Lv3 以上: (基礎値 + 1) を起点に累積乗算
        double value = baseValue + 1;
        int multiplierCount = Math.Min(Level - 1, LevelMultipliers.Length);
        for (int i = 0; i < multiplierCount; i++)
        {
            value *= LevelMultipliers[i];
        }
        return value;
    }

    // ─── 動的ステータス（派生プロパティ） ─────────────────────────────────

    /// <summary>装備の現在実効攻撃力（レベル補正後）。</summary>
    public double CurrentAttackPower => ComputeScaledStat(BaseStats.AttackPower);

    /// <summary>装備の現在実効分隊守護力（レベル補正後）。</summary>
    public double CurrentSquadDefense => ComputeScaledStat(BaseStats.SquadDefense);

    /// <summary>装備の現在実効突撃号令量（レベル補正後）。</summary>
    public double CurrentInitiativeBuff => ComputeScaledStat(BaseStats.InitiativeBuff);

    // ─── Affix 由来のフラット補正（レベル乗算を通さない素の加算） ──────────

    /// <summary>この個体の Affix が与える攻撃力 (ATK) フラット補正の総和。未知キーは 0。</summary>
    public int AffixAttackBonus => AffixMaster.SumAttackBonus(AffixKeys);

    /// <summary>この個体の Affix が与える分隊守護力 (DEF) フラット補正の総和。未知キーは 0。</summary>
    public int AffixDefenseBonus => AffixMaster.SumDefenseBonus(AffixKeys);

    /// <summary>この個体の Affix が与える行動速度 (SPD) フラット補正の総和。未知キーは 0。</summary>
    public int AffixSpeedBonus => AffixMaster.SumSpeedBonus(AffixKeys);

    // ─── 自然婚姻ポイント倍率 ─────────────────────────────────────────────

    /// <summary>
    /// 装備しているだけで戦闘後の自然婚姻ポイント (BattleAffinity) 獲得量を
    /// ブーストする倍率。
    ///
    /// 計算式:
    ///   AffinityMultiplier = (1.0 + Level × 0.1) × BaseAffinityMultiplier
    ///
    /// 通常アイテム (BaseAffinityMultiplier = 1.0):
    ///   Lv1 → 1.10, Lv5 → 1.50
    ///
    /// RingPurelove (BaseAffinityMultiplier = 1.5、純愛補正):
    ///   Lv1 → 1.65, Lv5 → 2.25
    ///
    /// 将来 Affix システムで追加倍率を載せる場合は、本プロパティの戻り値に
    /// 別途累積乗算する設計の余白を BaseAffinityMultiplier が提供している。
    /// </summary>
    public double AffinityMultiplier => (1.0 + Level * 0.1) * BaseStats.BaseAffinityMultiplier;

    // ─── 強欲効果フラグ ───────────────────────────────────────────────────

    /// <summary>
    /// 強欲効果を持つか。CoinGreed のみ true。
    /// BattleManager.ExecuteLastHit はこのフラグを参照して、ラストヒット時に
    /// 追加の婚姻ポイント強奪処理を発動する想定。
    /// </summary>
    public bool HasGreedEffect => BaseStats.HasGreedEffect;

    // ─── 不変更新メソッド ─────────────────────────────────────────────────

    /// <summary>
    /// アイテムレベルを +1 した新しい Equipment インスタンスを返す。
    /// 既に MaxEquipmentLevel に達している場合はレベル変化なしで新インスタンスを返す
    /// （with 式は常に新オブジェクトを生成するため、戻り値の不変性契約を維持）。
    /// </summary>
    public Equipment WithLevelUp()
    {
        if (Level >= MaxEquipmentLevel)
        {
            return this with { };
        }
        return this with { Level = Level + 1 };
    }

    /// <summary>
    /// 指定 Affix を追加した新しい Equipment インスタンスを返す。
    /// 同じキーの重複追加も許容する（Affix システムの設計次第で重複に意味を
    /// 持たせる場合があるため、ここでは判定しない）。
    /// </summary>
    public Equipment WithAddedAffix(string affixKey)
    {
        if (string.IsNullOrEmpty(affixKey))
            throw new ArgumentException("affixKey must be non-empty", nameof(affixKey));

        var next = AffixKeys is ImmutableArray<string> imm
            ? imm.Add(affixKey)
            : ImmutableArray.CreateRange(AffixKeys).Add(affixKey);
        return this with { AffixKeys = next };
    }

    // ─── 派生プロパティ ───────────────────────────────────────────────────

    /// <summary>レベルが上限 (5) に達しているか。</summary>
    public bool IsAtMaxLevel => Level >= MaxEquipmentLevel;

    /// <summary>Affix を 1 つ以上持つか。</summary>
    public bool HasAnyAffix => AffixKeys.Count > 0;

    /// <summary>純愛補正持ち（BaseAffinityMultiplier &gt; 1.0）か。</summary>
    public bool HasPureloveBoost => BaseStats.BaseAffinityMultiplier > 1.0;
}
