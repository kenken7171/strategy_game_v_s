// =============================================================================
//  ChronicleKnights — UserInterface/Settlement/SettlementView.cs
// -----------------------------------------------------------------------------
//  決算画面（無状態 UI）。戦場から拠点への直帰ルートへ割り込み、戦果を脳汁で受け取り、
//  とどめの儀式を執り行い、歴史を刻む大戦の終局。本作のカタルシスの頂点。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    戦果やフラグ（ゲーム数理）を 1 つもキャッシュしない。表示する値はすべて SoT
//    （ChronicleGlobal.LastBattleSpoils / GetAliveUnits）から「その場で」読み直す。唯一の保持は
//    とどめの二重解決を防ぐ UI 相互作用ラッチ（_lastHitResolved。TitleView._started と同種）。
//
//  ★ 三段の決算（戦闘 → とどめ → 統合台帳 → 経済還流）:
//    戦闘決着（EndBattle 済み）を受けて生存者から「とどめを取った 1 名」を必須選択させ、
//    ResolveLastHit → FinalizeBattleSpoils（統合台帳確定）→ ApplyBattleSpoils（婚姻ポイントを
//    PointsEconomy へ非破壊還流）を一気通貫で執り行う。獲得ポイントは確定済み LastBattleSpoils から
//    settlement-view-spoils-value へ一方通行で Push する。
//
//  ★ リークフリー（_settlementNodes 台帳方式・既存実証済みの規律）:
//    動的生成したとどめカードはすべて _settlementNodes 台帳へ記録し、再描画の冒頭と退場（_ExitTree）で
//    例外なく一括 QueueFree して更地化する。永続参照・自前 Tween 台帳は持たない。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"SETTLEMENT:"/"LAST HIT:"/"ACQUIRED POINTS:" 等）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using Godot;

namespace ChronicleKnights.UserInterface.Settlement;

/// <summary>
/// SoT（LastBattleSpoils / GetAliveUnits）から戦果ととどめ候補をその場で読み直して Push バインドし、
/// とどめの必須選択で統合台帳を確定・経済へ還流するだけの、状態を持たない決算ビュー。
/// </summary>
public partial class SettlementView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>決算の背景色（荘厳な紺）。</summary>
    private static readonly Color BackdropColor = new(0.05f, 0.06f, 0.09f, 1.0f);

    /// <summary>
    /// 「ACCEPT HISTORY」押下で歴史を受領し、拠点（次なる年）へ還流することを上位ルータへ知らせる。
    /// 戦果の SoT 確定（ResolveLastHit → Finalize → ApplyBattleSpoils）は本ビューが済ませ、遷移だけを委譲する。
    /// </summary>
    public event Action? SettlementAccepted;

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 表示ノード（SoT を流し込む先。戦果のキャッシュではない） ─────────────
    private Label? _spoilsValue;
    private VBoxContainer? _lastHitContainer;
    private Button? _acceptButton;

    /// <summary>動的生成したとどめカードの台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _settlementNodes = new();

    /// <summary>とどめの二重解決を防ぐ UI 相互作用ラッチ（ゲーム状態のキャッシュではない）。</summary>
    private bool _lastHitResolved;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "settlement-view-root");

        BuildChrome();
        RenderAll();
    }

    public override void _ExitTree()
    {
        ClearSettlementNodes();
    }

    // ─── 不変クローム構築 ─────────────────────────────────────────────────

    private void BuildChrome()
    {
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "settlement-view-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        column.SetMeta(TestIdMetaKey, "settlement-view-panel");
        margin.AddChild(column);

        var header = new Label { Text = "SETTLEMENT:" };
        header.AddThemeFontSizeOverride("font_size", 32);
        header.SetMeta(TestIdMetaKey, "settlement-view-header");
        column.AddChild(header);

        // 戦果（確定済み統合台帳の獲得ポイント）。
        _spoilsValue = new Label();
        _spoilsValue.AddThemeFontSizeOverride("font_size", 20);
        _spoilsValue.SetMeta(TestIdMetaKey, "settlement-view-spoils-value");
        column.AddChild(_spoilsValue);

        // とどめの儀式（生存者から 1 名を必須選択）。
        var lastHitHeader = new Label { Text = "LAST HIT:" };
        lastHitHeader.SetMeta(TestIdMetaKey, "settlement-view-lasthit-header");
        column.AddChild(lastHitHeader);

        _lastHitContainer = new VBoxContainer();
        _lastHitContainer.AddThemeConstantOverride("separation", 4);
        _lastHitContainer.SetMeta(TestIdMetaKey, "settlement-view-lasthit-container");
        column.AddChild(_lastHitContainer);

        // 歴史受領（とどめ確定まで無効。押下で次なる年の拠点へ還流）。
        _acceptButton = new Button { Text = "ACCEPT HISTORY", Disabled = true };
        _acceptButton.SetMeta(TestIdMetaKey, "settlement-view-accept-button");
        _acceptButton.Pressed += OnAcceptPressed;
        column.AddChild(_acceptButton);
    }

    // ─── 描画（SoT をその場で読み直して Push バインド） ─────────────────────

    private void RenderAll()
    {
        ClearSettlementNodes(); // 何より先に更地化（再描画でカードが累積しない）

        if (_spoilsValue is null || _lastHitContainer is null || _acceptButton is null) return;

        // 戦果: とどめ確定前は "--"、確定後は LastBattleSpoils の獲得ポイント。
        _spoilsValue.Text = _lastHitResolved
            ? "ACQUIRED POINTS: " + EarnedPoints()
            : "ACQUIRED POINTS: --";

        // 歴史受領はとどめ確定後にのみ解禁（必須選択の強制）。
        _acceptButton.Disabled = !_lastHitResolved;

        if (_lastHitResolved) return; // 確定後はとどめカードを出さない。

        var survivors = _chronicleGlobal?.GetAliveUnits();
        if (survivors is null) return;

        foreach (var unit in survivors)
        {
            var unitId = unit.Id; // ループ毎の確定キャプチャ。
            var card = new Button { Text = unit.Job.ToString() };
            card.SetMeta(TestIdMetaKey, $"settlement-view-lasthit-card-{unitId}");
            card.Pressed += () => OnLastHitChosen(unitId);
            _lastHitContainer.AddChild(card);
            _settlementNodes.Add(card); // 台帳へ登録（更地化で一括解放）
        }
    }

    /// <summary>確定済み統合台帳の獲得婚姻ポイント（純粋算出・常に 0 以上）。</summary>
    private int EarnedPoints()
        => _chronicleGlobal?.LastBattleSpoils.CalculateEarnedMarriagePoints() ?? 0;

    // ─── とどめの儀式 → 統合台帳確定 → 経済還流（一気通貫・SoT を叩くだけ） ───

    private void OnLastHitChosen(Guid unitId)
    {
        if (_chronicleGlobal is null || _lastHitResolved) return;

        _chronicleGlobal.ResolveLastHit(unitId);              // とどめの成長/破壊/強奪を刻む。
        var spoils = _chronicleGlobal.FinalizeBattleSpoils(); // 統合台帳（ターン成長 ＋ とどめ）を確定。
        _chronicleGlobal.ApplyBattleSpoils(spoils);           // 婚姻ポイントを経済へ非破壊還流（EconomyChanged）。

        _lastHitResolved = true;
        RenderAll(); // 戦果表示 + ACCEPT 解禁 + とどめカード撤去。
    }

    /// <summary>歴史を受領し、拠点（次なる年）へ還流する（遷移は購読側へ委譲）。</summary>
    private void OnAcceptPressed() => SettlementAccepted?.Invoke();

    // ─── 更地化（台帳の全とどめカードを一括解放） ────────────────────────────

    private void ClearSettlementNodes()
    {
        foreach (var node in _settlementNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _settlementNodes.Clear();
    }
}
