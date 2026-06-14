// =============================================================================
//  ChronicleKnights — UserInterface/Title/TitleView.cs
// -----------------------------------------------------------------------------
//  世界創生の起点。タイトル画面（無状態 UI）。
//
//  ★ 役割（シードインジェクタ）:
//    「Start」押下で、この 100 年史の運命を支配する決定論 PRNG シードを生成し、唯一の SoT
//    （ChronicleGlobal）へインジェクトしてゲームを開始する。シードが決まれば同一の歴史が再現される。
//
//  ★ 前世の完全粛清（絶対防衛規律）:
//    タイトル進入（_Ready）の瞬間に ChronicleGlobal.Reset() をキックし、前世のメモリ（血統・予言・
//    旅団史・経済）を 1 バイトのリークもなく更地化する。SoT が握るのは不変レコード／不変コレクション
//    のみゆえ、Reset の空値再代入で旧参照は解放される。本ビューは Godot ノード以外の状態を持たない。
//
//  ★ 完全な無状態 UI（変数保持の禁止）:
//    ゲーム数理（年・ポイント等）を 1 ミリもキャッシュしない。本ビューが持つのは「Start を 1 度だけ
//    確定する一過性ガード」だけ。確定で StartRequested を 1 度発火し、拠点への遷移は購読側（シーン
//    ルータ）が駆動する（画面側は遷移可否の判断を持たない）。
//
//  ★ 開発憲法①（ASCII 限定・日本語ハードコード禁止）:
//    ノード名・testid・表示文言はすべて ASCII（英数字）。日本語・データ名のハードコードを持たない。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using Godot;

namespace ChronicleKnights.UserInterface.Title;

/// <summary>
/// 決定論シードを生成して ChronicleGlobal へインジェクトする、状態を持たないタイトルビュー。
/// 進入時に前世を粛清（Reset）し、Start 確定で 1 度だけ <see cref="StartRequested"/> を発火する。
/// </summary>
public partial class TitleView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>背景色（深い紺。世界創生の静けさ）。</summary>
    private static readonly Color BackdropColor = new(0.04f, 0.05f, 0.10f, 1.0f);

    private ChronicleGlobal? _chronicleGlobal;

    /// <summary>「Start」を 1 度だけ確定する一過性ガード（多重起動の構造的防止）。</summary>
    private bool _started;

    /// <summary>
    /// 「Start」確定で 1 度だけ発火。購読側（シーンルータ）がこれを受けて拠点へ遷移する。
    /// </summary>
    public event Action? StartRequested;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "title-view-root");

        // ★ 前世の完全粛清: タイトル進入の瞬間に SoT を更地へ戻す（リークフリー）。
        _chronicleGlobal?.Reset();

        BuildUI();
    }

    private void BuildUI()
    {
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "title-view-backdrop");
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 20);
        column.SetMeta(TestIdMetaKey, "title-view-panel");
        center.AddChild(column);

        var logo = new Label
        {
            Text                = "CHRONICLE KNIGHTS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        logo.AddThemeFontSizeOverride("font_size", 48);
        logo.SetMeta(TestIdMetaKey, "title-view-logo");
        column.AddChild(logo);

        var subtitle = new Label
        {
            Text                = "100-year multiverse chronicle",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.SetMeta(TestIdMetaKey, "title-view-subtitle");
        column.AddChild(subtitle);

        var startButton = new Button
        {
            Text              = "Start",
            CustomMinimumSize = new Vector2(220, 56),
        };
        startButton.SetMeta(TestIdMetaKey, "title-view-start-button");
        startButton.Pressed += OnStartPressed;
        column.AddChild(startButton);
    }

    /// <summary>
    /// 「Start」確定。決定論シードを生成して ChronicleGlobal へインジェクトし新規開始、続いて
    /// StartRequested を 1 度だけ発火する。二重確定は _started ガードで構造的に防ぐ。
    /// </summary>
    private void OnStartPressed()
    {
        if (_started) return;
        _started = true;

        // この 100 年史の運命を支配する決定論シードを生成し、唯一の SoT へインジェクトして開始。
        var seed = new Random().Next();
        _chronicleGlobal?.StartNewGame(seed);

        StartRequested?.Invoke(); // 拠点への遷移は購読側（シーンルータ）が駆動する
    }
}
