// =============================================================================
//  ChronicleKnights — UserInterface/Hub/RosterDragCard.cs
// -----------------------------------------------------------------------------
//  名簿（Roster）カード。ドラッグ元として「どのユニットか」を渡すだけの薄い PanelContainer。
//
//  ★ 無状態の徹底:
//    ゲーム状態はキャッシュしない。保持するのは描画時に外から差し込まれた UnitId のみで、これは
//    SoT を写し取った「このカードが今表す相手」の識別子（RosterChanged のたびにカードごと作り直される
//    一過性のレンダ産物）。ドラッグ時はこの UnitId を不変ペイロードへ符号化して渡すだけ。
//
//  ★ 開発憲法①（ASCII 限定）: ペイロードは FormationDragPayload（ASCII）に委譲。
// =============================================================================

using System;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>名簿カード。ドラッグで自身が表すユニットの ID を不変ペイロードとして払い出す。</summary>
public partial class RosterDragCard : PanelContainer
{
    /// <summary>このカードが今表すユニットの ID（描画時に外から差し込まれる。空ならドラッグ不可）。</summary>
    public Guid UnitId { get; set; }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (UnitId == Guid.Empty)
        {
            return default; // 空カードはドラッグしない。
        }

        SetDragPreview(new Label { Text = "UNIT" });
        return FormationDragPayload.ForUnit(UnitId);
    }
}
