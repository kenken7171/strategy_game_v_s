// =============================================================================
//  ChronicleKnights — UserInterface/UserInterfaceRoot.cs
// -----------------------------------------------------------------------------
//  新 UI 層の最初の画面遷移トポロジーを司る、状態を持たないシーンルータ。
//
//  ★ 役割（タイトル → 拠点の無状態遷移）:
//    起動時にタイトル（TitleView）を前面に立て、その StartRequested を受けて拠点（HubView）へ
//    切り替える。遷移可否の判断はビュー側が持たず、本ルータが「次にどのビューを立てるか」だけを
//    担う（脳と身体の分離）。ゲーム数理は一切持たない。
//
//  ★ リークフリー規律:
//    ビュー切替（SwapTo）の冒頭で、退場するビューのイベント購読を解除し QueueFree して更地化する。
//    新ビューのサブツリーは AddChild、退場ビューは芋づる解放。永続参照・Tween は持たない。
//
//  ★ 開発憲法①（ASCII 限定）: ノード名・testid はすべて ASCII（ui-root 等）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using ChronicleKnights.UserInterface.Battle;
using ChronicleKnights.UserInterface.Hub;
using ChronicleKnights.UserInterface.Title;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>
/// タイトル → 拠点の無状態遷移を駆動するシーンルータ。現在のビュー 1 つだけを前面に保持し、
/// 切替時に旧ビューを購読解除 + QueueFree して更地化する（リークフリー）。
/// </summary>
public partial class UserInterfaceRoot : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>現在前面に立っているビュー（切替で更地化する対象）。</summary>
    private Control? _currentView;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta(TestIdMetaKey, "ui-root");

        ShowTitle();
    }

    /// <summary>タイトルを前面へ立て、その「Start」遷移要求を購読する。</summary>
    private void ShowTitle()
    {
        var title = new TitleView();
        SwapTo(title);                            // 旧ビューを更地化してから前面へ
        title.StartRequested += OnStartRequested; // 切替後に購読（自己購読の取り違え防止）
    }

    /// <summary>タイトルの「Start」遷移要求を 1 度受け、拠点へ切り替える。</summary>
    private void OnStartRequested() => ShowHub();

    /// <summary>拠点を前面へ立て、その「MARCH」出撃要求を購読する（タイトルは更地化で解放）。</summary>
    private void ShowHub()
    {
        var hub = new HubView();
        SwapTo(hub);                          // 旧ビューを更地化してから前面へ
        hub.BattleRequested += OnBattleRequested; // 切替後に購読（自己購読の取り違え防止）
    }

    /// <summary>拠点の「MARCH」出撃要求を受け、戦場へ切り替える。</summary>
    private void OnBattleRequested() => ShowBattle();

    /// <summary>戦場を前面へ立てる（拠点は SwapTo の更地化で購読解除 + 解放される）。</summary>
    private void ShowBattle() => SwapTo(new BattleView());

    /// <summary>
    /// 旧ビューを購読解除 + QueueFree して更地化し、新ビューをフルレクトで前面へ追加する
    /// （リークフリーの一手に集約）。
    /// </summary>
    private void SwapTo(Control next)
    {
        if (_currentView is not null && GodotObject.IsInstanceValid(_currentView))
        {
            if (_currentView is TitleView outgoingTitle)
            {
                outgoingTitle.StartRequested -= OnStartRequested; // 退場タイトルの購読解除
            }
            else if (_currentView is HubView outgoingHub)
            {
                outgoingHub.BattleRequested -= OnBattleRequested; // 退場拠点の購読解除
            }

            _currentView.QueueFree();
        }

        next.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(next);
        _currentView = next;
    }
}
