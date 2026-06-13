// =============================================================================
//  ChronicleKnights — EquipmentService.cs
// -----------------------------------------------------------------------------
//  旅団員への装備の「脱着（装着・取り外し）」を担う Godot 非依存の純粋層。
//
//  ★ TS 正史・既存資産との対応:
//    本サービスは「無償の直接脱着」を担う（ポイント経済を一切消費しない）。
//    既存の ShopService.TryBuyEquipment / TryUpgradeEquipment が「共通サイフを
//    消費して新品を購入・強化する兵器廠」だったのに対し、本サービスは
//    「すでに旅団が所有している装備種別を、ユニットへ素手で着けたり外したりする」
//    編成寄りの操作を表現する。FormationUI / RosterUI の装備スロットが直接叩く
//    単方向 API（ChronicleGlobal.EquipItem / UnequipItem）の純粋な土台となる。
//
//  ★ 設計判断 — RosterAdminService / ScoutService と同じ層別（純粋・静的）:
//    元データ（不変ロスタ）から新しい不変ロスタを返すだけで副作用は一切ない。
//    これにより Godot ランタイム無しで xUnit から脱着の挙動を 1 ビット単位で
//    単体テストでき、ChronicleGlobal は「状態の差し替えとシグナル発火」だけに
//    専念できる（責務分離・単一 SoT・設計憲法③）。
//
//  ★ ヌル安全契約（番兵）:
//    指定 Id がロスタに存在しなければ null を返す（例外は投げない）。これにより
//    呼び出し側（ChronicleGlobal.EquipItem / UnequipItem）の「失敗 = null・no-op」
//    契約が保てる。ItemId は型安全な enum のため不正値の混入はコンパイル時に排除
//    される（番兵 None を増設せず、未装備は MainEquipment == null で表現する）。
//
//  ★ 個体追跡:
//    装着時は ShopService.TryBuyEquipment と同様に Equipment へ新規 Guid を割り当て、
//    「誰が・どの個体を・いつ着けたか」を将来追跡可能にする。Guid 採番は本サービスの
//    外（呼び出し側）から注入できるオーバーロードも提供し、テストの決定論を担保する。
//
//  略称（BattalionDefense 等の正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;

namespace ChronicleKnights.Core.Units;

// ─── 脱着結果（不変スナップショット） ───────────────────────────────────────

/// <summary>
/// 装備の脱着（装着・取り外し）成功時に確定する不変な結果の束。
/// 呼び出し側（ChronicleGlobal）はこの結果でロスタを丸ごと差し替える。
/// </summary>
public sealed record EquipmentChangeResult
{
    /// <summary>脱着を反映した新しい旅団員リスト（元の並び順を保持）。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>脱着後のユニット本体（UI の「○○が装備した／外した」演出に使う）。</summary>
    public required Unit AffectedUnit { get; init; }

    /// <summary>
    /// 今回の操作で外れた旧装備（装着の差し替え or 取り外しで手放した個体）。
    /// 元から装備が無かった場合は null。呼び出し側は UI で「旧装備を失った」等の
    /// 演出に使う（在庫システム未導入のため、外した装備は現状ロストする設計）。
    /// </summary>
    public Equipment? ReplacedEquipment { get; init; }

    /// <summary>
    /// 今回の操作でロスタの内容が実際に変化したか。取り外し要求時に元から未装備だった
    /// 場合は false（no-op）になり、呼び出し側は不要な RosterChanged 発火を抑止できる。
    /// </summary>
    public required bool RosterMutated { get; init; }
}

// ─── 脱着サービス本体（純粋・静的） ─────────────────────────────────────────

/// <summary>
/// 旅団員への装備の脱着を行う Godot 非依存の純粋ユーティリティ。状態を持たない。
/// </summary>
public static class EquipmentService
{
    /// <summary>
    /// 指定 Id の旅団員へ、指定種別（<paramref name="itemId"/>）の新品 Lv1 装備を装着した
    /// 結果を返す純粋関数。Equipment の個体 Guid は <see cref="Guid.NewGuid"/> で採番する。
    ///
    /// 判定:
    ///   - roster が null → 例外（呼び出し前提の契約違反）。
    ///   - 指定 Id が見つからない → null（no-op・例外は投げない）。
    ///   - 見つかった → 新装備を生成・装着した新ロスタを束ねて返す（旧装備は ReplacedEquipment）。
    ///
    /// 入力の roster は一切変更しない（不変・副作用なし）。装着は常にロスタを変化させるため、
    /// 戻り値の RosterMutated は常に true。
    /// </summary>
    /// <param name="roster">現在の旅団員リスト（不変・null 不可）。</param>
    /// <param name="unitId">装備を装着する対象ユニット Id。</param>
    /// <param name="itemId">装着する装備種別（5 大マスターのいずれか）。</param>
    /// <returns>成功時は <see cref="EquipmentChangeResult"/>、対象不在時は null。</returns>
    /// <exception cref="ArgumentNullException">roster が null の場合。</exception>
    public static EquipmentChangeResult? TryEquip(
        ImmutableList<Unit> roster, Guid unitId, ItemId itemId)
        => TryEquip(roster, unitId, itemId, Guid.NewGuid());

    /// <summary>
    /// <see cref="TryEquip(ImmutableList{Unit}, Guid, ItemId)"/> の決定論オーバーロード。
    /// Equipment の個体 Guid を外部から注入できるため、テストで結果を厳密にアサートできる。
    /// </summary>
    /// <param name="roster">現在の旅団員リスト（不変・null 不可）。</param>
    /// <param name="unitId">装備を装着する対象ユニット Id。</param>
    /// <param name="itemId">装着する装備種別（5 大マスターのいずれか）。</param>
    /// <param name="equipmentId">生成する装備個体に与える Guid（決定論注入用）。</param>
    /// <returns>成功時は <see cref="EquipmentChangeResult"/>、対象不在時は null。</returns>
    /// <exception cref="ArgumentNullException">roster が null の場合。</exception>
    public static EquipmentChangeResult? TryEquip(
        ImmutableList<Unit> roster, Guid unitId, ItemId itemId, Guid equipmentId)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var index = roster.FindIndex(u => u.Id == unitId);
        if (index < 0) return null; // 不在 Guid は no-op（例外は投げない）。

        var target = roster[index];

        var fitted = new Equipment
        {
            Id     = equipmentId,
            ItemId = itemId,
            Level  = Equipment.MinEquipmentLevel,
        };

        var replaced = target.MainEquipment;            // 旧装備は差し替えで手放す。
        var equippedUnit = target.WithEquipment(fitted); // 非破壊クローン（純粋遷移）。

        return new EquipmentChangeResult
        {
            NewRoster         = roster.SetItem(index, equippedUnit),
            AffectedUnit      = equippedUnit,
            ReplacedEquipment = replaced,
            RosterMutated     = true,
        };
    }

    /// <summary>
    /// 指定 Id の旅団員から現装備を取り外した結果を返す純粋関数。
    ///
    /// 判定:
    ///   - roster が null → 例外（呼び出し前提の契約違反）。
    ///   - 指定 Id が見つからない → null（no-op・例外は投げない）。
    ///   - 見つかったが元から未装備 → RosterMutated=false の no-op 結果（ロスタは同一参照）。
    ///   - 見つかって装備あり → MainEquipment=null にした新ロスタを束ねて返す。
    ///
    /// 入力の roster は一切変更しない（不変・副作用なし）。
    /// </summary>
    /// <param name="roster">現在の旅団員リスト（不変・null 不可）。</param>
    /// <param name="unitId">装備を取り外す対象ユニット Id。</param>
    /// <returns>成功時は <see cref="EquipmentChangeResult"/>、対象不在時は null。</returns>
    /// <exception cref="ArgumentNullException">roster が null の場合。</exception>
    public static EquipmentChangeResult? TryUnequip(ImmutableList<Unit> roster, Guid unitId)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var index = roster.FindIndex(u => u.Id == unitId);
        if (index < 0) return null; // 不在 Guid は no-op（例外は投げない）。

        var target = roster[index];
        var removed = target.MainEquipment;

        // 元から未装備なら変化なし（no-op）。ロスタを作り直さず、発火抑止情報を返す。
        if (removed is null)
        {
            return new EquipmentChangeResult
            {
                NewRoster         = roster,
                AffectedUnit      = target,
                ReplacedEquipment = null,
                RosterMutated     = false,
            };
        }

        var barehandedUnit = target.WithEquipment(null); // 非破壊クローン（純粋遷移）。

        return new EquipmentChangeResult
        {
            NewRoster         = roster.SetItem(index, barehandedUnit),
            AffectedUnit      = barehandedUnit,
            ReplacedEquipment = removed,
            RosterMutated     = true,
        };
    }
}
