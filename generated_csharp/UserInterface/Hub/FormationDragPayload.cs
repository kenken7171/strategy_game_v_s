// =============================================================================
//  ChronicleKnights — UserInterface/Hub/FormationDragPayload.cs
// -----------------------------------------------------------------------------
//  ドラッグ＆ドロップのペイロードを ASCII 文字列へ可逆符号化する状態なしの静的コーデック。
//
//  ★ なぜ文字列か:
//    Godot のドラッグ API は Variant を運ぶ。ゲーム状態を UI へ持たせず、ドロップ確定の一瞬だけ
//    「誰を / どこから」を文字列で受け渡し、即座に SoT（Core 配置 API）へ書き込むため、最小の
//    不変ペイロードとして ASCII 文字列を用いる（変数保持の禁止に適合）。
//
//  ★ 2 系統のペイロード:
//    - 名簿ユニット: "unit:{guid:N}"      … 名簿カードから盤面スロットへの新規配置。
//    - 盤面スロット: "slot:{rowInt}:{col}" … スロット間の入れ替え（rowInt = (int)SquadRow）。
//
//  ★ 開発憲法①（ASCII 限定）: 接頭辞・区切り・GUID(N 形式) すべて ASCII。日本語を一切含まない。
// =============================================================================

using System;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>ドラッグ＆ドロップのペイロード（ユニット / スロット）を ASCII 文字列で符号化する静的コーデック。</summary>
public static class FormationDragPayload
{
    private const string UnitPrefix = "unit:";
    private const string SlotPrefix = "slot:";

    /// <summary>名簿ユニットのペイロード（"unit:{guid:N}"）。</summary>
    public static string ForUnit(Guid unitId) => UnitPrefix + unitId.ToString("N");

    /// <summary>盤面スロットのペイロード（"slot:{rowInt}:{col}"）。</summary>
    public static string ForSlot(SlotCoordinate coordinate)
        => SlotPrefix + (int)coordinate.Row + ":" + coordinate.Column;

    /// <summary>ユニットペイロードか。</summary>
    public static bool IsUnit(string payload) => payload.StartsWith(UnitPrefix, StringComparison.Ordinal);

    /// <summary>スロットペイロードか。</summary>
    public static bool IsSlot(string payload) => payload.StartsWith(SlotPrefix, StringComparison.Ordinal);

    /// <summary>ユニットペイロードを復号する。失敗時 false。</summary>
    public static bool TryUnit(string payload, out Guid unitId)
    {
        unitId = Guid.Empty;
        if (!IsUnit(payload)) return false;
        return Guid.TryParseExact(payload.Substring(UnitPrefix.Length), "N", out unitId);
    }

    /// <summary>スロットペイロードを復号する。失敗時 false。</summary>
    public static bool TrySlot(string payload, out SlotCoordinate coordinate)
    {
        coordinate = new SlotCoordinate(SquadRow.Front, 0);
        if (!IsSlot(payload)) return false;

        var parts = payload.Substring(SlotPrefix.Length).Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var rowInt)) return false;
        if (!int.TryParse(parts[1], out var column)) return false;

        coordinate = new SlotCoordinate((SquadRow)rowInt, column);
        return true;
    }
}
