// =============================================================================
//  ChronicleKnights — RosterAdminService.cs
// -----------------------------------------------------------------------------
//  拠点（Guild フェーズ）における旅団の「人事（戦力外通告 = 手動解雇）」の純粋層。
//
//  ★ TS 正史との対応:
//    TypeScript 版 HumanDecisionService.dismissUnit(brigade, unitId) に相当する。
//    プレイヤーの意思で現役ロスタから 1 名を即時に外す「手動の新陳代謝」を担う。
//    世代交代（RosterLifecycle.AdvanceGeneration）が寿命到達・戦闘死を *自動* で
//    外すのに対し、本サービスは寿命前のユニットを *任意* に外す（席を空ける・
//    戦力外を切る）プレイヤー主導の決定を表現する。
//
//  ★ 設計判断 — Godot 非依存の純粋関数として分離（ScoutService と同じ層別）:
//    元データ（不変ロスタ）から新しい不変ロスタを返すだけで副作用は一切ない。
//    これにより Godot ランタイム無しで xUnit から解雇の挙動を完全に単体テストでき、
//    ChronicleGlobal は「状態の差し替えとシグナル発火」だけに専念できる（責務分離）。
//
//  ★ 不可逆性:
//    解雇されたユニットは復元手段を持たない（完全ロストと同じ扱い）。返却される
//    Dismissed は UI 演出（「○○に戦力外を通告した」）や年代記の素材に使える。
//
//  ★ ヌル安全契約:
//    指定 Id がロスタに存在しなければ null を返す（例外は投げない）。これにより
//    呼び出し側（ChronicleGlobal.ExecuteDismiss）の「失敗 = null・no-op」契約が保てる。
//
//  略称（BattalionDefense 等の正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

// ─── 解雇結果（不変スナップショット） ───────────────────────────────────────

/// <summary>
/// 戦力外通告（手動解雇）成功時に確定する不変な結果の束。
/// 呼び出し側（ChronicleGlobal.ExecuteDismiss）はこの結果でロスタを丸ごと差し替える。
/// </summary>
public sealed record DismissResult
{
    /// <summary>対象ユニットを除去した新しい旅団員リスト（元の並び順を保持）。</summary>
    public required ImmutableList<Unit> NewRoster { get; init; }

    /// <summary>解雇されたユニット本体（UI の「○○に戦力外を通告」演出に使う）。</summary>
    public required Unit Dismissed { get; init; }
}

// ─── 人事サービス本体（純粋・静的） ─────────────────────────────────────────

/// <summary>
/// 旅団の手動解雇（戦力外通告）を行う Godot 非依存の純粋ユーティリティ。状態を持たない。
/// </summary>
public static class RosterAdminService
{
    /// <summary>
    /// 指定 Id の旅団員を現役ロスタから外した結果を返す純粋関数。
    ///
    /// 判定:
    ///   - roster が null → 例外（呼び出し前提の契約違反）。
    ///   - 指定 Id が見つからない → null（no-op・例外は投げない）。
    ///   - 見つかった → 当該ユニットを 1 名だけ除去した新ロスタと本体を束ねて返す。
    ///
    /// 入力の roster は一切変更しない（不変・副作用なし）。同 Id は仕様上一意のため、
    /// RemoveAll でも除去対象はちょうど 1 名に限られる。
    /// </summary>
    /// <param name="roster">現在の旅団員リスト（不変・null 不可）。</param>
    /// <param name="unitId">戦力外通告の対象ユニット Id。</param>
    /// <returns>成功時は <see cref="DismissResult"/>、対象不在時は null。</returns>
    /// <exception cref="ArgumentNullException">roster が null の場合。</exception>
    public static DismissResult? TryDismiss(ImmutableList<Unit> roster, Guid unitId)
    {
        ArgumentNullException.ThrowIfNull(roster);

        var target = roster.FirstOrDefault(u => u.Id == unitId);
        if (target is null) return null;

        return new DismissResult
        {
            NewRoster = roster.RemoveAll(u => u.Id == unitId),
            Dismissed = target,
        };
    }
}
