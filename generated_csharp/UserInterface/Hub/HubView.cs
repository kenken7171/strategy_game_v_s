// =============================================================================
//  ChronicleKnights — UserInterface/Hub/HubView.cs
// -----------------------------------------------------------------------------
//  拠点画面（無状態 UI）。内政の本陣。タイトルから遷移して最初に立つ旅団の本拠。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    年・婚姻ポイントといったゲーム数理を 1 つもキャッシュしない。表示する値はすべて SoT
//    （ChronicleGlobal）から「その場で」読み直してラベルへ一方通行で流し込む（Push バインド）。
//    SoT が動けば（EconomyChanged / TimelineChanged / StateInitialized）自動で読み直して描き直す。
//
//  ★ 婚姻経済ダッシュボード（脳汁の還流パイプ）:
//    CurrentEconomy の残高（Balance）・累計獲得（Earned）・累計消費（Spent）を常設パネルへ表示。
//    EconomyChanged で値が動いたら JuiceDirector.CountUp で数字をロールアップ（脳汁演出）させる。
//    ロールアップの始点はラベルが今表示している値（= ノードのレンダ状態）を読むため、UI 側に
//    ポイントのキャッシュ変数を持たない（無状態の徹底）。
//
//  ★ リークフリー:
//    シグナル購読は _Ready で張り _ExitTree で確実に解除する。CountUp の Tween は対象ラベル自身へ
//    バインドされ、本ビューの QueueFree で子ごと自動失効する。永続参照キャッシュ・自前 Tween 台帳は持たない。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"Year:" / "Balance:" / "Earned:" / "Spent:" 等）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System.Globalization;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Units;
using ChronicleKnights.UI;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>
/// SoT（ChronicleGlobal）から現在年と婚姻経済（残高・累計獲得・累計消費）をその場で読み直して
/// Push バインドするだけの、状態を持たない拠点ビュー。経済変動は CountUp でロールアップする。
/// </summary>
public partial class HubView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>拠点の背景色（落ち着いた紺）。</summary>
    private static readonly Color BackdropColor = new(0.06f, 0.07f, 0.12f, 1.0f);

    /// <summary>経済ロールアップ（脳汁カウントアップ）の秒数。</summary>
    private const double RollupSeconds = 0.5;

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 表示ノード（SoT を流し込む先。ゲーム変数のキャッシュではない） ───────
    private Label? _yearLabel;
    private Label? _balanceValue;
    private Label? _earnedValue;
    private Label? _spentValue;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "hub-view-root");

        BuildChrome();
        SubscribeSignals();

        // 初回は SoT を直接読んで Push（ロールアップなし。脳汁は以後の変動で焚く）。
        RenderYear();
        RenderEconomyDirect();
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
        column.AddThemeConstantOverride("separation", 16);
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

        // ── 婚姻経済ダッシュボード（残高 / 累計獲得 / 累計消費） ────────────
        var economyPanel = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        economyPanel.AddThemeConstantOverride("separation", 6);
        economyPanel.SetMeta(TestIdMetaKey, "hub-view-economy-panel");
        column.AddChild(economyPanel);

        _balanceValue = AddEconomyRow(economyPanel, "Balance:", "hub-view-balance-value");
        _earnedValue  = AddEconomyRow(economyPanel, "Earned:",  "hub-view-earned-value");
        _spentValue   = AddEconomyRow(economyPanel, "Spent:",   "hub-view-spent-value");

        // ── 商店アクション（兵装購入 / 強化）。押下で Core 経済 API を直接叩く ──
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 12);
        actionRow.SetMeta(TestIdMetaKey, "hub-view-economy-actions");
        economyPanel.AddChild(actionRow);

        var buyButton = new Button { Text = "BUY" };
        buyButton.SetMeta(TestIdMetaKey, "hub-view-buy-button");
        buyButton.Pressed += OnBuyPressed;
        actionRow.AddChild(buyButton);

        var upgradeButton = new Button { Text = "UPGRADE" };
        upgradeButton.SetMeta(TestIdMetaKey, "hub-view-upgrade-button");
        upgradeButton.Pressed += OnUpgradePressed;
        actionRow.AddChild(upgradeButton);

        // ── 予言の赤き警告オーバーレイ（時間の矢 + 章ボス前兆。自前で無状態に SoT を読む） ──
        //    HubView は土台へ載せるだけ。購読・再描画・リークフリー更地化はオーバーレイ自身が司る
        //    （本ビューが QueueFree されれば子として芋づる解放され _ExitTree で購読解除される）。
        var prophecy = new ProphecyTimelineOverlay();
        column.AddChild(prophecy);
    }

    /// <summary>
    /// 「説明（左・伸長）／値（右）」の経済 1 行を組み、値ラベル（ロールアップ対象）を返す。
    /// 値ラベルは数値文字列だけを持ち、その表示テキストが「今表示している値」の唯一の根拠になる。
    /// </summary>
    private Label AddEconomyRow(VBoxContainer panel, string prefixText, string valueTestId)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var prefix = new Label { Text = prefixText };
        prefix.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(prefix);

        var value = new Label
        {
            Text                = "0",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.SetMeta(TestIdMetaKey, valueTestId);
        row.AddChild(value);

        panel.AddChild(row);
        return value;
    }

    // ─── シグナル購読 / 解除（SoT 変更に追従して無状態に描き直す） ──────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.TimelineChanged  += OnTimelineChanged;
        _chronicleGlobal.EconomyChanged   += OnEconomyChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.TimelineChanged  -= OnTimelineChanged;
            _chronicleGlobal.EconomyChanged   -= OnEconomyChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（リーク防止）。
        }
    }

    private void OnTimelineChanged() => RenderYear();

    /// <summary>経済変動: 現在表示値 → SoT の新値へロールアップ（脳汁カウントアップ）。</summary>
    private void OnEconomyChanged() => RollupEconomy();

    /// <summary>新規開始（世界初期化）: ロールアップせず SoT を直接 Push（更地からの再起動）。</summary>
    private void OnStateInitialized()
    {
        RenderYear();
        RenderEconomyDirect();
    }

    // ─── 商店アクション（Core 経済 API を直接叩くだけ・無状態） ────────────
    //   成功すると SoT 側が残高を減算し EconomyChanged を発火するため、拠点画面は
    //   OnEconomyChanged 経由で自動的にロールアップ再描画される（一気通貫の環）。

    private void OnBuyPressed()
    {
        if (_chronicleGlobal is null) return;

        var units = _chronicleGlobal.GetAliveUnits();
        if (units.Count == 0) return; // 対象不在なら no-op（API 側でも安全に弾かれる）。

        // 現役筆頭へ新品 Lv1 装備を購入（成功で残高 −BuyCost → EconomyChanged）。
        _chronicleGlobal.ExecuteBuyEquipment(units[0].Id, ItemId.SwordKnight);
    }

    private void OnUpgradePressed()
    {
        if (_chronicleGlobal is null) return;

        var units = _chronicleGlobal.GetAliveUnits();
        if (units.Count == 0) return;

        // 現役筆頭の装備を 1 段階強化（デフレ物価 BaseUpgradeCost=2 が SoT 経由で適用される）。
        _chronicleGlobal.ExecuteUpgradeEquipment(units[0].Id);
    }

    // ─── 描画（SoT をその場で読み直して Push バインド） ─────────────────────

    private void RenderYear()
    {
        if (_yearLabel is null) return;
        var year = _chronicleGlobal?.CurrentTimeline?.Turn ?? 0;
        _yearLabel.Text = "Year: " + year;
    }

    private void RenderEconomyDirect()
    {
        if (_balanceValue is null || _earnedValue is null || _spentValue is null) return;

        var economy = _chronicleGlobal?.CurrentEconomy;
        if (economy is null) return;

        _balanceValue.Text = FormatValue(economy.CurrentBalance);
        _earnedValue.Text  = FormatValue(economy.TotalEarned);
        _spentValue.Text   = FormatValue(economy.TotalSpent);
    }

    private void RollupEconomy()
    {
        if (_balanceValue is null || _earnedValue is null || _spentValue is null) return;

        var economy = _chronicleGlobal?.CurrentEconomy;
        if (economy is null) return;

        // 始点はラベルが今表示している値（= レンダ状態）を読む。UI に値をキャッシュしない。
        JuiceDirector.CountUp(_balanceValue, ShownValue(_balanceValue), economy.CurrentBalance, FormatValue, RollupSeconds);
        JuiceDirector.CountUp(_earnedValue,  ShownValue(_earnedValue),  economy.TotalEarned,    FormatValue, RollupSeconds);
        JuiceDirector.CountUp(_spentValue,   ShownValue(_spentValue),   economy.TotalSpent,     FormatValue, RollupSeconds);
    }

    /// <summary>整数値を ASCII の数値文字列へ整形する（CountUp の整形デリゲート）。</summary>
    private static string FormatValue(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>ラベルが今表示している整数値を読む（パース不能なら 0）。レンダ状態が唯一の根拠。</summary>
    private static int ShownValue(Label label)
        => int.TryParse(label.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shown) ? shown : 0;
}
