// =============================================================================
//  ChronicleKnights — UserInterface/Hub/FormationSlotControl.cs
// -----------------------------------------------------------------------------
//  ▲（デルタ）陣形の 1 スロット。ドロップ先（名簿配置・スロット入替）かつドラッグ元（スロット移動）。
//
//  ★ 無状態の徹底（変数保持の禁止）:
//    スロットが持つのは「自分の盤面座標（Coordinate）」という不変のレイアウト事実と、描画時に差し込まれる
//    占有者 ID（OccupantId。ドラッグ元判定用の一過性の写し）だけ。ドロップが確定した一瞬に、SoT 書込みを
//    委譲デリゲート（PlaceRequested / SwapRequested）へ要求するのみで、配置状態を一切キャッシュしない。
//    デリゲートは HubView の Core 配置 API 呼び出しへ繋がり、FormationChanged シグナルで全体が再描画される。
//
//  ★ リークフリー:
//    本ノードは HubView の _formationNodes 台帳に載り、再描画冒頭と _ExitTree で QueueFree される。
//    デリゲートは HubView のメソッド参照で、ノード解放時に解ける（HubView を越えて生存しない）。
//
//  ★ 開発憲法①（ASCII 限定）: ペイロードは FormationDragPayload（ASCII）に委譲。
// =============================================================================

using System;
using ChronicleKnights.Core.Formation;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>▲ 陣形スロット。Godot ネイティブのドラッグ＆ドロップでドロップ確定時に SoT 書込みを要求する。</summary>
public partial class FormationSlotControl : PanelContainer
{
    /// <summary>このスロットが対応する盤面座標（不変のレイアウト事実）。</summary>
    public SlotCoordinate Coordinate { get; set; }

    /// <summary>描画時の占有者 ID（空ならドラッグ元にならない）。</summary>
    public Guid OccupantId { get; set; }

    /// <summary>名簿ユニットがドロップされた時に「この座標へ配置せよ」と SoT へ要求するデリゲート。</summary>
    public Action<SlotCoordinate, Guid>? PlaceRequested { get; set; }

    /// <summary>他スロットがドロップされた時に「2 座標を入れ替えよ」と SoT へ要求するデリゲート。</summary>
    public Action<SlotCoordinate, SlotCoordinate>? SwapRequested { get; set; }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (OccupantId == Guid.Empty)
        {
            return default; // 空スロットからはドラッグしない。
        }

        SetDragPreview(new Label { Text = "MOVE" });
        return FormationDragPayload.ForSlot(Coordinate);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.String)
        {
            return false;
        }

        var payload = data.AsString();
        return FormationDragPayload.IsUnit(payload) || FormationDragPayload.IsSlot(payload);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.String)
        {
            return;
        }

        var payload = data.AsString();
        if (FormationDragPayload.TryUnit(payload, out var unitId))
        {
            PlaceRequested?.Invoke(Coordinate, unitId); // SoT: この座標へ配置 → FormationChanged。
        }
        else if (FormationDragPayload.TrySlot(payload, out var source))
        {
            SwapRequested?.Invoke(source, Coordinate); // SoT: 2 座標を入替 → FormationChanged。
        }
    }
}
