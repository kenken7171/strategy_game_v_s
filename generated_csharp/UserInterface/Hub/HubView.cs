// =============================================================================
//  ChronicleKnights — UserInterface/Hub/HubView.cs
// -----------------------------------------------------------------------------
//  拠点画面（無状態 UI）。タイトルから遷移して最初に立つ、旅団の本拠。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    本ビューは `int currentYear` のようなゲーム変数を 1 つも保持しない。表示する数理は
//    すべて SoT（ChronicleGlobal）から「その場で」読み直し、ノードのテキストへ一方通行で
//    流し込む（Push バインド）。SoT が動けば（EconomyChanged / TimelineChanged /
//    StateInitialized）自動で読み直して描き直すため、UI 側に真実の写しを持たない。
//
//  ★ リークフリー:
//    シグナル購読は _Ready で張り _ExitTree で確実に解除する。ノードの子は本ビューの
//    QueueFree で芋づる解放される。Tween・永続参照キャッシュは持たない。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"Year: {n}" / "Points: {n}" 等）。データ名や
//    日本語のハードコードを持たない。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using ChronicleKnights.Autoload;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>
/// SoT（ChronicleGlobal）から現在のマスター数理（年・婚姻ポイント）をその場で読み直して
/// ラベルへ Push バインドするだけの、状態を持たない拠点ビュー。
/// </summary>
public partial class HubView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>拠点の背景色（落ち着いた紺）。</summary>
    private static readonly Color BackdropColor = new(0.06f, 0.07f, 0.12f, 1.0f);

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 表示ノード（SoT を流し込む先。ゲーム変数のキャッシュではない） ───────
    private Label? _yearLabel;
    private Label? _pointsLabel;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "hub-view-root");

        BuildChrome();
        SubscribeSignals();
        RenderAll(); // 表示の瞬間に SoT を読み直して Push
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
    }

    // ─── 不変クローム構築 ─────────────────────────────────────────────────

    private void BuildChrome()
    {
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "hub-view-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        column.SetMeta(TestIdMetaKey, "hub-view-panel");
        margin.AddChild(column);

        var header = new Label { Text = "BASE HUB" };
        header.AddThemeFontSizeOverride("font_size", 32);
        header.SetMeta(TestIdMetaKey, "hub-view-header");
        column.AddChild(header);

        _yearLabel = new Label();
        _yearLabel.AddThemeFontSizeOverride("font_size", 20);
        _yearLabel.SetMeta(TestIdMetaKey, "hub-view-year");
        column.AddChild(_yearLabel);

        _pointsLabel = new Label();
        _pointsLabel.AddThemeFontSizeOverride("font_size", 20);
        _pointsLabel.SetMeta(TestIdMetaKey, "hub-view-points");
        column.AddChild(_pointsLabel);
    }

    // ─── シグナル購読 / 解除（SoT 変更に追従して無状態に描き直す） ──────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.TimelineChanged   += OnStateChanged;
        _chronicleGlobal.EconomyChanged    += OnStateChanged;
        _chronicleGlobal.StateInitialized  += OnStateChanged;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.TimelineChanged   -= OnStateChanged;
            _chronicleGlobal.EconomyChanged    -= OnStateChanged;
            _chronicleGlobal.StateInitialized  -= OnStateChanged;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（リーク防止）。
        }
    }

    private void OnStateChanged() => RenderAll();

    // ─── 描画（SoT をその場で読み直して Push バインド） ─────────────────────

    private void RenderAll()
    {
        if (_yearLabel is null || _pointsLabel is null) return;

        // 現在年は予言タイムラインの現ターン、ポイントは経済の現残高（いずれも SoT を読むだけ）。
        var year = _chronicleGlobal?.CurrentTimeline?.Turn ?? 0;
        var points = _chronicleGlobal?.CurrentEconomy.CurrentBalance ?? 0;

        _yearLabel.Text = "Year: " + year;
        _pointsLabel.Text = "Points: " + points;
    }
}
