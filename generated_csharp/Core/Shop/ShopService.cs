// =============================================================================
//  ChronicleKnights — ShopService.cs
// -----------------------------------------------------------------------------
//  拠点（Guild フェーズ）の「旅団兵器廠（商店・装備強化）」の純粋ロジック層。
//
//  ★ 何をするのか（2 つの消費アクション）:
//    1. 購入 (TryBuyEquipment): ポイントを支払い、指定ユニットへ新品の Lv1 装備を
//       1 個装着する。既に装備していた場合、旧装備は完全ロストする（差し替え破棄）。
//    2. 強化 (TryUpgradeEquipment): ポイントを支払い、指定ユニットの現装備を 1 段階
//       レベルアップ (Equipment.WithLevelUp) する。上限 (Lv5) では失敗（浪費防止）。
//
//  ★ 設計判断 — Godot 非依存の純粋関数として分離（ScoutService と同じ層別）:
//    元データ（不変の経済・不変ロスタ）から新しい不変スナップショットを返すだけで
//    副作用は一切ない。これにより Godot ランタイム無しで xUnit から購入・強化の
//    挙動（残高検証・装備差し替え・上限ガード・不変性）を完全に単体テストでき、
//    ChronicleGlobal は「状態の差し替えとシグナル発火」だけに専念できる（責務分離）。
//
//  ★ 経済は単一の PointsEconomy（pt）。設計憲法③「単一 SoT」に従い、装備の購入・
//    強化もスカウト・婚姻と同じ共通サイフ (SpendPoints) を通す。専用通貨は設けない。
//
//  ★ ヌル安全契約（ScoutService と同じ）:
//    対象不在・残高不足・強化対象なし・上限到達・不正コストは、いずれも例外ではなく
//    null を返す。これにより呼び出し側（ChronicleGlobal.ExecuteBuyEquipment /
//    ExecuteUpgradeEquipment）の「失敗 = null・no-op」契約が保てる。
//
//  ★ コストの SoT:
//    購入コストは固定 (BuyCost)。強化コストは現レベルに比例 (UpgradeCostFor) し、
//    レベルが上がるほど高くつく。UI はこれらの公開 API を呼んで表示コストを算出する
//    （UI とロジックでコスト式を二重定義しない・単一 SoT）。
//
//  略称（BattalionDefense 等の正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Shop;

// ─── 購入結果（不変スナップショット） ───────────────────────────────────────

/// <summary>
/// 装備購入成功時に確定する不変な結果の束。
/// 呼び出し側（ChronicleGlobal.ExecuteBuyEquipment）はこの結果で状態を丸ごと差し替える。
/// </summary>
public sealed record ShopBuyResult
{
    /// <summary>コスト消費後の新しいポイント経済。</summary>
    public required PointsEconomy NewEconomy { get; init; }

    /// <summary>対象ユニットへ新装備を装着した新しい旅団員リスト（並び順は保持）。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>新装備を装着したユニット本体（UI の「○○が装備した」演出に使う）。</summary>
    public required Unit EquippedUnit { get; init; }

    /// <summary>新規購入された装備本体（Lv1・固有 Guid）。</summary>
    public required Equipment PurchasedEquipment { get; init; }

    /// <summary>
    /// 差し替えで失われた旧装備（装備なしのユニットに購入した場合は null）。
    /// 完全ロスト仕様により旧装備は復元手段を持たない（UI の喪失通知の素材に使える）。
    /// </summary>
    public Equipment? ReplacedEquipment { get; init; }
}

// ─── 持ち物への購入結果（不変スナップショット） ─────────────────────────────

/// <summary>
/// 「何を買うか」を先に選ぶ購入（ユニットを介さず持ち物へ入れる）の成功結果。
/// 呼び出し側（ChronicleGlobal.BuyEquipmentToInventory）はこの結果で経済・持ち物を差し替える。
/// </summary>
public sealed record ShopInventoryBuyResult
{
    /// <summary>コスト消費後の新しいポイント経済。</summary>
    public required PointsEconomy NewEconomy { get; init; }

    /// <summary>新規購入装備を末尾に加えた新しい持ち物（BrigadeInventory）。</summary>
    public required ImmutableList<Equipment> NewInventory { get; init; }

    /// <summary>新規購入された装備本体（Lv1・固有 Guid・未装着）。</summary>
    public required Equipment PurchasedEquipment { get; init; }
}

// ─── 強化結果（不変スナップショット） ───────────────────────────────────────

/// <summary>
/// 装備強化成功時に確定する不変な結果の束。
/// 呼び出し側（ChronicleGlobal.ExecuteUpgradeEquipment）はこの結果で状態を丸ごと差し替える。
/// </summary>
public sealed record ShopUpgradeResult
{
    /// <summary>コスト消費後の新しいポイント経済。</summary>
    public required PointsEconomy NewEconomy { get; init; }

    /// <summary>強化後の装備を装着し直した新しい旅団員リスト（並び順は保持）。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>強化後の装備を持つユニット本体（UI の「○○の装備が強化された」演出に使う）。</summary>
    public required Unit UpgradedUnit { get; init; }

    /// <summary>レベルが +1 された強化後の装備本体。</summary>
    public required Equipment UpgradedEquipment { get; init; }
}

// ─── 商店サービス本体（純粋・静的） ─────────────────────────────────────────

/// <summary>
/// 装備の購入・強化の残高検証・ポイント消費・装備差し替えを行う Godot 非依存の
/// 純粋ユーティリティ。状態を持たない。
/// </summary>
public static class ShopService
{
    // ─── コスト SoT 定数 ──────────────────────────────────────────────────

    /// <summary>新品 Lv1 装備 1 個の購入コスト (pt)。固定。</summary>
    public const int BuyCost = 5;

    /// <summary>
    /// 装備強化コストの基準係数。実コストは現レベルに比例する。黄金均衡フェーズで投資効率を改善し
    /// 後半インフレに戦力を追従させるため 3 → 2 へデフレ調律済み（多重宇宙の絶滅率 0% 均衡の物価ノブ）。
    /// </summary>
    public const int BaseUpgradeCost = 2;

    /// <summary>
    /// 現在レベルの装備を 1 段階強化するのに必要なコストを返す純粋関数。
    /// 計算式: BaseUpgradeCost × currentLevel（Lv1→2 は 2pt、Lv2→3 は 4pt、… と逓増）。
    /// UI と ChronicleGlobal の双方がこの単一 SoT を参照し、コスト式を二重定義しない。
    /// </summary>
    /// <param name="currentLevel">強化前の装備レベル（1 以上を想定）。</param>
    public static int UpgradeCostFor(int currentLevel) => BaseUpgradeCost * currentLevel;

    // ─── 購入試行 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 指定コストを共通サイフから消費して、指定ユニットへ新品 Lv1 装備を装着した結果を返す純粋関数。
    ///
    /// 判定順:
    ///   1. economy / roster が null → 例外（呼び出し前提の契約違反）。
    ///   2. cost が負 → null（不正入力）。
    ///   3. 対象ユニットがロスタに居ない → null（no-op）。
    ///   4. 残高不足 (CanAfford(cost) == false) → null。
    ///   5. SpendPoints が万一例外を投げた（並列で残高が割れた等）→ null。
    ///   6. 成功 → 新装備を生成・装着して ShopBuyResult を返す（旧装備は ReplacedEquipment で返却）。
    ///
    /// 入力の economy / roster は一切変更しない（不変・副作用なし）。
    /// </summary>
    /// <param name="economy">現在のポイント経済。</param>
    /// <param name="roster">現在の旅団員リスト。</param>
    /// <param name="unitId">装備を購入して装着する対象ユニット Id。</param>
    /// <param name="itemId">購入する装備種別（5 大マスターのいずれか）。</param>
    /// <param name="cost">購入に支払うポイント数（非負）。</param>
    /// <returns>成功時は <see cref="ShopBuyResult"/>、失敗（対象不在・残高不足・不正入力）時は null。</returns>
    /// <exception cref="ArgumentNullException">economy または roster が null の場合。</exception>
    public static ShopBuyResult? TryBuyEquipment(
        PointsEconomy economy,
        ImmutableList<Unit> roster,
        Guid unitId,
        ItemId itemId,
        int cost)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(roster);

        if (cost < 0) return null;

        var index = roster.FindIndex(u => u.Id == unitId);
        if (index < 0) return null;

        if (!economy.CanAfford(cost)) return null;

        PointsEconomy nextEconomy;
        try
        {
            nextEconomy = economy.SpendPoints(cost);
        }
        catch (InvalidOperationException)
        {
            return null; // 残高検証直後の競合等は「失敗 = null」へ正規化（呼び出し側の契約を守る）。
        }

        var target = roster[index];

        var purchased = new Equipment
        {
            Id     = Guid.NewGuid(),
            ItemId = itemId,
            Level  = Equipment.MinEquipmentLevel,
        };

        var replaced = target.MainEquipment;          // 旧装備は差し替えで完全ロスト。
        var equippedUnit = target.WithEquipment(purchased);

        return new ShopBuyResult
        {
            NewEconomy         = nextEconomy,
            NewRoster          = roster.SetItem(index, equippedUnit),
            EquippedUnit       = equippedUnit,
            PurchasedEquipment = purchased,
            ReplacedEquipment  = replaced,
        };
    }

    // ─── 購入試行（持ち物へ・ユニット非依存） ─────────────────────────────

    /// <summary>
    /// 指定コストを共通サイフから消費し、新品の Lv1 装備（itemId）を旅団の持ち物へ加える純粋関数。
    /// 「誰に買うか」ではなく「何を買うか」だけを決める購入経路（装着は後で持ち物から行う）。
    ///
    /// 判定順:
    ///   1. economy / inventory が null → 例外（呼び出し前提の契約違反）。
    ///   2. cost が負 → null（不正入力）。
    ///   3. 残高不足 (CanAfford(cost) == false) → null。
    ///   4. SpendPoints が万一例外を投げた → null。
    ///   5. 成功 → 新品 Lv1 装備を末尾に加えた持ち物と新経済を返す。
    ///
    /// 入力の economy / inventory は一切変更しない（不変・副作用なし）。
    /// </summary>
    public static ShopInventoryBuyResult? TryBuyEquipmentToInventory(
        PointsEconomy economy,
        ImmutableList<Equipment> inventory,
        ItemId itemId,
        int cost)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(inventory);

        if (cost < 0) return null;
        if (!economy.CanAfford(cost)) return null;

        PointsEconomy nextEconomy;
        try
        {
            nextEconomy = economy.SpendPoints(cost);
        }
        catch (InvalidOperationException)
        {
            return null; // 残高検証直後の競合等は「失敗 = null」へ正規化。
        }

        var purchased = new Equipment
        {
            Id     = Guid.NewGuid(),
            ItemId = itemId,
            Level  = Equipment.MinEquipmentLevel,
        };

        return new ShopInventoryBuyResult
        {
            NewEconomy         = nextEconomy,
            NewInventory       = inventory.Add(purchased),
            PurchasedEquipment = purchased,
        };
    }

    // ─── 強化試行 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 指定コストを共通サイフから消費して、指定ユニットの現装備を 1 段階レベルアップした結果を返す純粋関数。
    ///
    /// 判定順:
    ///   1. economy / roster が null → 例外（呼び出し前提の契約違反）。
    ///   2. cost が負 → null（不正入力）。
    ///   3. 対象ユニットがロスタに居ない → null（no-op）。
    ///   4. 対象が装備を持たない → null（強化対象なし）。
    ///   5. 現装備が既に上限 (Lv5) → null（ポイント浪費を防ぐ）。
    ///   6. 残高不足 (CanAfford(cost) == false) → null。
    ///   7. SpendPoints が万一例外を投げた → null。
    ///   8. 成功 → WithLevelUp した装備を装着し直して ShopUpgradeResult を返す。
    ///
    /// 入力の economy / roster は一切変更しない（不変・副作用なし）。
    /// </summary>
    /// <param name="economy">現在のポイント経済。</param>
    /// <param name="roster">現在の旅団員リスト。</param>
    /// <param name="unitId">装備を強化する対象ユニット Id。</param>
    /// <param name="cost">強化に支払うポイント数（非負・典型は <see cref="UpgradeCostFor"/> の戻り値）。</param>
    /// <returns>成功時は <see cref="ShopUpgradeResult"/>、失敗時は null。</returns>
    /// <exception cref="ArgumentNullException">economy または roster が null の場合。</exception>
    public static ShopUpgradeResult? TryUpgradeEquipment(
        PointsEconomy economy,
        ImmutableList<Unit> roster,
        Guid unitId,
        int cost)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(roster);

        if (cost < 0) return null;

        var index = roster.FindIndex(u => u.Id == unitId);
        if (index < 0) return null;

        var target = roster[index];
        var current = target.MainEquipment;
        if (current is null) return null;       // 強化対象なし。
        if (current.IsAtMaxLevel) return null;   // 既に上限。これ以上は強化できない（浪費防止）。

        if (!economy.CanAfford(cost)) return null;

        PointsEconomy nextEconomy;
        try
        {
            nextEconomy = economy.SpendPoints(cost);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var upgraded = current.WithLevelUp();
        var upgradedUnit = target.WithEquipment(upgraded);

        return new ShopUpgradeResult
        {
            NewEconomy        = nextEconomy,
            NewRoster         = roster.SetItem(index, upgradedUnit),
            UpgradedUnit      = upgradedUnit,
            UpgradedEquipment = upgraded,
        };
    }
}
