// =============================================================================
//  ChronicleKnights — RestResultOverlay.cs
// -----------------------------------------------------------------------------
//  休息報酬通知オーバーレイ（戦闘を回避して大隊を立て直した年の「成果」の提示）。
//
//  ★ 完全な無状態 UI（設計憲法 ③・単方向データフロー）:
//    本オーバーレイは自前の状態を 1 ミリも持たない。_Ready で
//    ChronicleGlobal.LastRestOutcome（純粋層 RestService.Resolve が確定した不変
//    レコード）を「読むだけ」で描画する。休息の算出・確定は一切しない（それは
//    ChronicleGlobal.ExecuteRest のロック内で既に終わっている）。
//
//  ★ ライフサイクル（BattleSpoilsScreen と同じ案A・消滅副作用の「前」に提示する）:
//    GameDirector が「休息 + 次へ」を捕捉して ExecuteRest() を確定・公開した直後、
//    まだ AdvancePhase を呼ばずに本オーバーレイを最前面へ展開する。プレイヤーが
//    「確認（次代へ）」（rest-result-close-button）を押した瞬間に Confirmed を 1 度だけ
//    発火し、それを購読する GameDirector が初めて AdvancePhase（年送り）を駆動する。
//    これにより、編成画面・戦闘画面を一切経由せず、その場で休息成果を提示してから
//    安全に年代記へ遷移できる。
//
//  ★ クローム日本語の方針（設計憲法 ①）:
//    見出し等のクローム文字列は日本語を直書きする（憲法はクローム日本語を許容）。
//    識別子・testid・ログは ASCII のみ。
//
//  ★ data-testid 規律: 全セクション・閉じるボタンに機械生成 ASCII の testid を付与する。
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.GameFlow;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// ChronicleGlobal.LastRestOutcome を単方向に読み取って描画するだけの、状態を持たない
/// 休息報酬通知モーダル。閉じるボタン押下で <see cref="Confirmed"/> を 1 度だけ発火し、
/// 自身を退場（QueueFree）する。
/// </summary>
public partial class RestResultOverlay : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せるメタキー（Godot ノードメタ）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>モーダル本体の最小サイズ。</summary>
    private static readonly Vector2 BodyMinimumSize = new(460, 220);

    /// <summary>暗幕の色（背後の編成画面を覆い隠すモーダル背景）。</summary>
    private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.72f);

    /// <summary>回復・報酬という前向きな成果の強調色（緑）。</summary>
    private static readonly Color GainHighlightColor = new(0.36f, 0.80f, 0.42f);

    /// <summary>ポイント獲得の「コインがきらめく」一瞬のフラッシュ色（金）。</summary>
    private static readonly Color CoinFlashColor = new(1.0f, 0.86f, 0.32f);

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 確定（次代へ）通知 ───────────────────────────────────────────────

    /// <summary>
    /// プレイヤーが「確認」を押して休息成果を見届けたときに 1 度だけ発火する。
    /// GameDirector がこれを購読し、初めて AdvancePhase（年送り）を駆動する（案A）。
    /// </summary>
    public event Action? Confirmed;

    /// <summary>二重確定（連打・多重発火）を構造的に防ぐ一過性ガード。</summary>
    private bool _confirmed;

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;     // 背後の編成画面への入力を遮断（モーダル）
        SetMeta(TestIdMetaKey, "rest-result-root");

        BuildUI();
    }

    // ─── UI 構築（LastRestOutcome を読むだけ・状態は持たない） ─────────────

    private void BuildUI()
    {
        // 休息成果の単一の真実を読む（未取得時は空成果へフォールバックして落ちない）。
        var outcome = _chronicleGlobal?.LastRestOutcome ?? RestOutcome.None;

        // ── 暗幕（フルレクト・入力遮断） ────────────────────────────
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "rest-result-backdrop");
        AddChild(backdrop);

        // ── 中央寄せラッパ → パネル ─────────────────────────────────
        var centerWrap = new CenterContainer();
        centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerWrap);

        var panel = new PanelContainer();
        panel.SetMeta(TestIdMetaKey, "rest-result-panel");
        centerWrap.AddChild(panel);

        var outer = new VBoxContainer { CustomMinimumSize = BodyMinimumSize };
        outer.AddThemeConstantOverride("separation", 12);
        panel.AddChild(outer);

        // ── タイトル ─────────────────────────────────────────────────
        var title = new Label { Text = "☾ 休息 — 大隊は英気を養った" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.SetMeta(TestIdMetaKey, "rest-result-title");
        outer.AddChild(title);

        // ── 物語る一文（戦闘を回避し、安全に年を越したことの提示）─────
        var narration = new Label
        {
            Text = "戦いを避け、大隊は休息に専念した。"
                   + "傷を癒やし、隊列を立て直し、次なる出撃に備える——この年に倒れた者はいない。",
        };
        narration.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        narration.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        narration.SetMeta(TestIdMetaKey, "rest-result-narration");
        outer.AddChild(narration);

        // ── 成果明細（回復頭数 / 獲得ポイント / 残高）────────────────
        var detail = new VBoxContainer();
        detail.AddThemeConstantOverride("separation", 6);
        detail.SetMeta(TestIdMetaKey, "rest-result-detail");
        outer.AddChild(detail);

        AppendLine(
            detail, "英気を養った大隊員", $"{outcome.RestedUnitCount} 名（全員 万全）",
            "rest-result-recovered-line", "rest-result-recovered-val", GainHighlightColor);

        var pointsValue = AppendLine(
            detail, "休息ボーナス", $"+{outcome.PointsReward} pt",
            "rest-result-points-line", "rest-result-points-val", GainHighlightColor);

        AppendLine(
            detail, "ポイント残高", $"{outcome.BalanceAfter} pt",
            "rest-result-balance-line", "rest-result-balance-val", GainHighlightColor);

        // ── 確認（次代へ）ボタン ────────────────────────────────────
        var closeButton = new Button { Text = "▶ 確認（次代へ）" };
        closeButton.SetMeta(TestIdMetaKey, "rest-result-close-button");
        closeButton.Pressed += OnClosePressed;
        outer.AddChild(closeButton);

        // ── 演出点火: 休息ボーナス > 0 のときだけ「コインのきらめき」を焚く ─────
        IgnitePointsJuice(panel, pointsValue, outcome.PointsReward);
    }

    /// <summary>
    /// 「説明（左・伸長）／値（右）」の 1 行を明細へ足し、機械可読のため行・値の双方に
    /// ASCII testid を付ける。値ラベルを返すのは、報酬行をロールアップ演出へ渡すため。
    /// </summary>
    private Label AppendLine(
        VBoxContainer root, string description, string valueText,
        string lineTestId, string valueTestId, Color valueColor)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        line.SetMeta(TestIdMetaKey, lineTestId);

        var desc = new Label { Text = description };
        desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(desc);

        var value = new Label
        {
            Text                = valueText,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.AddThemeColorOverride("font_color", valueColor);
        value.SetMeta(TestIdMetaKey, valueTestId);
        line.AddChild(value);

        root.AddChild(line);
        return value;
    }

    /// <summary>休息ボーナスのロールアップ表示に使う整形デリゲート。</summary>
    private static string FormatPoints(int value) => $"+{value} pt";

    /// <summary>
    /// 休息ボーナス &gt; 0 のとき「コインのきらめき」を点火する。報酬ラベルを 0 → reward へ
    /// ロールアップし、パネル全体をコイン色へ一瞬フラッシュさせる。
    ///
    /// ★ リークフリー: いずれの Tween も対象ノード（報酬ラベル / パネル）自身へバインドされ、
    ///   本オーバーレイの QueueFree でこれらの子が cascade 解放されると Tween は自動失効し、
    ///   CountUp のコールバックは IsInstanceValid ガードにより安全に no-op となる
    ///   （BattleSpoilsScreen と同じ規律）。自前の状態は一切増設しない。
    /// </summary>
    private void IgnitePointsJuice(PanelContainer panel, Label pointsValue, int reward)
    {
        if (reward <= 0) return;

        JuiceDirector.Flash(panel, CoinFlashColor, 0.55);
        JuiceDirector.CountUp(pointsValue, 0, reward, FormatPoints, 0.85, 0.10);
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    /// <summary>
    /// 「確認（次代へ）」確定。Confirmed を 1 度だけ発火（GameDirector が AdvancePhase を
    /// 駆動）し、オーバーレイは自身を退場させる。二重確定は _confirmed ガードで構造的に防ぐ。
    /// </summary>
    private void OnClosePressed()
    {
        if (_confirmed) return;
        _confirmed = true;

        Confirmed?.Invoke();   // 購読側（GameDirector）が初めて年送りへ前進する
        QueueFree();           // モーダルはフレーム末で除去（前面から退場）
    }
}
