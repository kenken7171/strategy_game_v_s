// =============================================================================
//  ChronicleKnights — LastHitCeremonyScreen.cs
// -----------------------------------------------------------------------------
//  とどめの儀式（ラストヒット）モーダル。Battle フェーズ三段フローの第 2 段。
//
//  ★ 三段フローでの位置（戦闘 → ★とどめ★ → 決算）:
//    ① ターン制戦闘（BattleUI）が決着 → EndBattle で戦闘後ロスタを正本化。
//    ② 本画面が前面展開し、生存者から「とどめを取った 1 名」を必ず選ばせ、
//       ChronicleGlobal.ResolveLastHit(unitId) で昇級／装備進化／Lv5 破壊／
//       強欲（CoinGreed）強奪を解決し、冷徹な演出でプレイヤーに突きつける。
//    ③ プレイヤーが「戦果へ」を押すと Confirmed を 1 度だけ発火。購読する BattleUI が
//       FinalizeBattleSpoils（統合台帳の確定）→ 戦果決算スクリーンへと進める。
//
//  ★ 「必ず 1 名選択」の規律（旅団長＝ユーザー確定仕様）:
//    生存かつ成人（Age >= BattleEligibleAge）のユニットが 1 名でも居れば、選択を
//    スキップする手段は用意しない。とどめを刻む（resolve）まで「戦果へ」は出さない。
//    OptionButton は先頭項目を自動選択するため、候補が居る限り常に有効な 1 名が選ばれる。
//
//  ★ 敗北・全滅で候補が 0 名の縁ケース:
//    とどめを取れる者が居ない（理論上の全滅・子供のみ生存等）ときは儀式を成立させ
//    られないため、その旨を提示し「戦果へ」だけを出して優雅に次段へ送る（決算は
//    戦死を含めて提示されるので、ここで握り潰さない）。
//
//  ★ 日本語ハードコード方針（設計憲法 ①）:
//    ジョブ名・アイテム名・ユニット表示名という「データ名」は一切ハードコードせず、
//    ChronicleGlobal.ResolveDisplayName / ResolveJobName / ResolveItemName（内部で
//    localization の ASCII enum キーを引く）に委譲する。見出し・演出のクローム文字列は
//    既存 UI（BattleSpoilsScreen 等）と同じ方針で日本語を直書きする（憲法はクローム
//    日本語を許容）。Autoload 未取得時は enum 名・生キーへフォールバックして落ちない。
//
//  ★ data-testid 規律:
//    退役する BattleResultUI が testid を 0 個しか持たなかった負債を本画面で返済する。
//    全セクション・セレクタ・ボタン・演出ラベルに ASCII の battle-lasthit-* を漏れなく付与。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 生存者から「とどめを取った 1 名」を必ず選ばせ、ChronicleGlobal.ResolveLastHit で
/// 解決して冷徹な演出を見せるモーダル。プレイヤーが「戦果へ」を押すと
/// <see cref="Confirmed"/> を 1 度だけ発火し、自身を退場（QueueFree）する。
/// </summary>
public partial class LastHitCeremonyScreen : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せるメタキー（Godot ノードメタ）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>戦闘参加可能となる成人年齢（とどめ候補のフィルタに使用）。</summary>
    private const int BattleEligibleAge = 15;

    /// <summary>モーダル本体の最小サイズ。</summary>
    private static readonly Vector2 BodyMinimumSize = new(520, 280);

    /// <summary>暗幕の色（背後の戦闘画面を覆い隠すモーダル背景）。</summary>
    private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.72f);

    /// <summary>結果メインラベルの演出アニメーション秒数（往復片道）。</summary>
    private const double ResultPunchDurationSec = 0.15;

    /// <summary>結果メインラベルの演出スケール最大倍率。</summary>
    private const float ResultPunchScalePeak = 1.3f;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 確定（戦果へ）通知 ───────────────────────────────────────────────

    /// <summary>
    /// プレイヤーが「戦果へ」を押して儀式を見届けたときに 1 度だけ発火する。
    /// BattleUI がこれを購読し、FinalizeBattleSpoils → 戦果決算スクリーンへ進める。
    /// </summary>
    public event Action? Confirmed;

    /// <summary>二重確定（連打・多重発火）を構造的に防ぐ一過性ガード。</summary>
    private bool _confirmed;

    /// <summary>とどめが既に解決済みかを示すガード（二重 ResolveLastHit を防ぐ）。</summary>
    private bool _resolved;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private VBoxContainer? _selectionBox;
    private OptionButton? _unitSelector;
    private Button? _resolveButton;
    private Label? _noSurvivorsLabel;
    private VBoxContainer? _resultBox;
    private Label? _resultMainLabel;
    private Label? _resultDetailLabel;
    private Button? _continueButton;

    // ─── 内部状態（OptionButton index → Unit.Id の対応のみ） ──────────────

    private readonly List<Guid> _selectableUnitIds = new();

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;     // 背後の戦闘画面への入力を遮断（モーダル）
        SetMeta(TestIdMetaKey, "battle-lasthit-root");

        BuildUI();
        RenderInitialState();
    }

    // ─── UI 構築 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── 暗幕（フルレクト・入力遮断） ────────────────────────────
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "battle-lasthit-backdrop");
        AddChild(backdrop);

        // ── 中央寄せラッパ → パネル ─────────────────────────────────
        var centerWrap = new CenterContainer();
        centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerWrap);

        var panel = new PanelContainer();
        panel.SetMeta(TestIdMetaKey, "battle-lasthit-panel");
        centerWrap.AddChild(panel);

        var outer = new VBoxContainer { CustomMinimumSize = BodyMinimumSize };
        outer.AddThemeConstantOverride("separation", 12);
        panel.AddChild(outer);

        // ── タイトル + 案内 ─────────────────────────────────────────
        var title = new Label { Text = "🔥 とどめの儀式 / ラストヒット" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.SetMeta(TestIdMetaKey, "battle-lasthit-title");
        outer.AddChild(title);

        var subtitle = new Label { Text = "とどめを取った 1 名に栄光を — 必ず選べ" };
        subtitle.SetMeta(TestIdMetaKey, "battle-lasthit-subtitle");
        outer.AddChild(subtitle);

        // ── 選択セクション（候補が居るとき） ────────────────────────
        _selectionBox = new VBoxContainer();
        _selectionBox.AddThemeConstantOverride("separation", 8);
        _selectionBox.SetMeta(TestIdMetaKey, "battle-lasthit-selection");
        outer.AddChild(_selectionBox);

        var selectorRow = new HBoxContainer();
        selectorRow.AddThemeConstantOverride("separation", 8);
        selectorRow.SetMeta(TestIdMetaKey, "battle-lasthit-selector-row");
        _selectionBox.AddChild(selectorRow);

        selectorRow.AddChild(new Label { Text = "とどめを取ったユニット:" });
        _unitSelector = new OptionButton();
        _unitSelector.SetMeta(TestIdMetaKey, "battle-lasthit-selector");
        selectorRow.AddChild(_unitSelector);

        _resolveButton = new Button { Text = "🔥 確定: とどめを刻む" };
        _resolveButton.SetMeta(TestIdMetaKey, "battle-lasthit-resolve-button");
        _resolveButton.Pressed += OnResolvePressed;
        _selectionBox.AddChild(_resolveButton);

        // ── 候補なし通知（敗北・全滅の縁ケース） ────────────────────
        _noSurvivorsLabel = new Label { Text = "とどめを取れる者は居ない — 儀式は成立しない" };
        _noSurvivorsLabel.SetMeta(TestIdMetaKey, "battle-lasthit-no-survivors");
        outer.AddChild(_noSurvivorsLabel);

        // ── 結果演出セクション（解決後に可視化） ────────────────────
        _resultBox = new VBoxContainer();
        _resultBox.AddThemeConstantOverride("separation", 6);
        _resultBox.SetMeta(TestIdMetaKey, "battle-lasthit-result");
        outer.AddChild(_resultBox);

        _resultMainLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _resultMainLabel.AddThemeFontSizeOverride("font_size", 22);
        _resultMainLabel.SetMeta(TestIdMetaKey, "battle-lasthit-result-main");
        _resultBox.AddChild(_resultMainLabel);

        _resultDetailLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _resultDetailLabel.SetMeta(TestIdMetaKey, "battle-lasthit-result-detail");
        _resultBox.AddChild(_resultDetailLabel);

        // ── 戦果へ（Confirmed 発火）ボタン ──────────────────────────
        _continueButton = new Button { Text = "▶ 戦果へ" };
        _continueButton.SetMeta(TestIdMetaKey, "battle-lasthit-continue-button");
        _continueButton.Pressed += OnContinuePressed;
        outer.AddChild(_continueButton);
    }

    /// <summary>
    /// 初期表示を決める。候補（生存・成人）が 1 名でも居れば選択フェーズを出し、
    /// 「戦果へ」はとどめ解決まで伏せる（必ず 1 名選択の規律）。候補が 0 名なら
    /// 儀式不成立として通知だけ出し、「戦果へ」を即時に出す（敗北・全滅の縁ケース）。
    /// </summary>
    private void RenderInitialState()
    {
        PopulateSelector();
        bool hasCandidate = _selectableUnitIds.Count > 0;

        if (_selectionBox is not null) _selectionBox.Visible = hasCandidate;
        if (_noSurvivorsLabel is not null) _noSurvivorsLabel.Visible = !hasCandidate;
        if (_resultBox is not null) _resultBox.Visible = false;
        // 候補が居るうちはとどめ解決まで「戦果へ」を伏せる。居なければ即時に出す。
        if (_continueButton is not null) _continueButton.Visible = !hasCandidate;
    }

    /// <summary>生存かつ成人のユニットだけをセレクタへ充填する。</summary>
    private void PopulateSelector()
    {
        if (_chronicleGlobal is null || _unitSelector is null) return;

        _unitSelector.Clear();
        _selectableUnitIds.Clear();

        foreach (var unit in _chronicleGlobal.BattalionRoster)
        {
            if (!unit.IsAlive) continue;                 // 戦死者はとどめを取れない
            if (unit.Age < BattleEligibleAge) continue;  // 子供は戦闘に出ない
            _unitSelector.AddItem(FormatUnitDisplay(unit));
            _selectableUnitIds.Add(unit.Id);
        }
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    /// <summary>
    /// 「とどめを刻む」確定。選択された 1 名で ChronicleGlobal.ResolveLastHit を実行し、
    /// 結果を演出して、選択フェーズを閉じ結果フェーズと「戦果へ」を可視化する。
    /// 二重解決は _resolved ガードで防ぐ。
    /// </summary>
    private void OnResolvePressed()
    {
        if (_resolved) return;
        if (_chronicleGlobal is null || _unitSelector is null) return;
        if (_resultMainLabel is null || _resultDetailLabel is null) return;

        int selectedIndex = _unitSelector.Selected;
        if (selectedIndex < 0 || selectedIndex >= _selectableUnitIds.Count)
        {
            // 候補が居るのに未選択は通常起きない（先頭が自動選択される）が、念のため促す。
            _resultMainLabel.Text = "ユニットを選択してください";
            _resultDetailLabel.Text = "";
            ShowResultPhase();
            return;
        }

        var unitId = _selectableUnitIds[selectedIndex];

        // 解決前のスナップショット（演出の前後比較用）。
        var preUnit = _chronicleGlobal.FindUnit(unitId);
        var preEquipment = preUnit?.MainEquipment;

        var result = _chronicleGlobal.ResolveLastHit(unitId);
        if (result is null)
        {
            _resultMainLabel.Text = "解決失敗（ユニット不在 / 未初期化）";
            _resultDetailLabel.Text = "";
            ShowResultPhase();
            return;
        }

        _resolved = true;
        DisplayResult(result, preUnit, preEquipment);
        ShowResultPhase();
    }

    /// <summary>選択フェーズを閉じ、結果演出と「戦果へ」を前面化する。</summary>
    private void ShowResultPhase()
    {
        if (_selectionBox is not null) _selectionBox.Visible = false;
        if (_noSurvivorsLabel is not null) _noSurvivorsLabel.Visible = false;
        if (_resultBox is not null) _resultBox.Visible = true;
        if (_continueButton is not null) _continueButton.Visible = true;
    }

    /// <summary>
    /// 「戦果へ」確定。Confirmed を 1 度だけ発火（BattleUI が決算へ進める）し、
    /// モーダルは自身を退場させる。二重確定は _confirmed ガードで構造的に防ぐ。
    /// </summary>
    private void OnContinuePressed()
    {
        if (_confirmed) return;
        _confirmed = true;

        Confirmed?.Invoke();   // 購読側（BattleUI）が FinalizeBattleSpoils → 決算へ進める
        QueueFree();           // モーダルはフレーム末で除去（前面から退場）
    }

    // ─── 結果演出（退役する BattleResultUI から移設） ─────────────────────

    /// <summary>
    /// LastHitResult を解析し、4 つのシナリオ（ユニット成長 / 装備強化 /
    /// Lv5 破壊（通常 or 強欲）/ Lv5 生存）の対応テキストをメイン・詳細ラベルへ反映する。
    /// </summary>
    private void DisplayResult(LastHitResult result, Unit? preUnit, Equipment? preEquipment)
    {
        if (_resultMainLabel is null || _resultDetailLabel is null) return;

        var mainLines = new List<string>();
        var detailLines = new List<string>();

        // ── 1. ユニット成長 ──────────────────────────────────────
        if (result.LevelOverflow)
        {
            mainLines.Add("💪 ユニットは既に Lv3 — 経験値は無駄に流れた...");
        }
        else if (preUnit is not null && result.NewUnit.Level > preUnit.Level)
        {
            mainLines.Add($"⬆ ユニット Lv{preUnit.Level} → Lv{result.NewUnit.Level} に成長！");
        }

        // ── 2. 装備の進化 / 破壊 / 強欲 ─────────────────────────
        if (preEquipment is not null)
        {
            var itemDisplay = ItemName(preEquipment.ItemId);

            if (result.ItemDestroyed)
            {
                if (result.GreedPointsStolen > 0)
                {
                    // CoinGreed Lv5: 100% 破壊 + ポイント強奪（脳汁演出！）
                    mainLines.Add($"🪙💥 {itemDisplay} がパリンと砕け散った...！");
                    mainLines.Add($"✨ 特殊効果により大隊金庫に +{result.GreedPointsStolen} pt を強奪！");
                    detailLines.Add("(古銭は完全ロスト、復元不可。だが強奪は成功した — ヒリつくぜ)");
                }
                else
                {
                    // 通常装備 Lv5: 50% 破壊
                    mainLines.Add($"💥 {itemDisplay} (Lv5) がパリンと砕け散った...！");
                    detailLines.Add("(50% コイントスに敗北。神器は完全ロスト)");
                }
            }
            else if (result.ItemLevelUpTriggered)
            {
                // Lv1〜4: 確定 +1
                var newLevel = result.NewUnit.MainEquipment?.Level ?? preEquipment.Level;
                mainLines.Add($"⬆ {itemDisplay} Lv{preEquipment.Level} → Lv{newLevel} に進化！");
            }
            else if (preEquipment.Level >= Equipment.MaxEquipmentLevel)
            {
                // Lv5 で itemLevelUpTriggered=false かつ itemDestroyed=false
                // → 50% コイントスを乗り切ったケース
                mainLines.Add($"🍀 {itemDisplay} (Lv5) は奇跡的に生存！");
                detailLines.Add("(50% コイントスを乗り切った — 神器の意志は強い)");
            }
        }

        // ── 何も起こらなかった場合 ─────────────────────────────
        if (mainLines.Count == 0)
        {
            mainLines.Add("(変化なし — トドメは取ったが成長要素なし)");
        }

        _resultMainLabel.Text   = string.Join("\n", mainLines);
        _resultDetailLabel.Text = string.Join("\n", detailLines);

        PunchAnimateMainLabel();
    }

    /// <summary>
    /// 結果メインラベルにパンチ感のあるスケールアニメーションを与える脳汁演出。
    /// Godot ランタイム外（テスト環境）では CreateTween が例外を投げる可能性が
    /// あるため、try/catch で完全に隔離する。本モーダルは「戦果へ」で QueueFree される
    /// 一過性ノードなので、単発 Tween がゾンビ化することはない（ループしない）。
    /// </summary>
    private void PunchAnimateMainLabel()
    {
        if (_resultMainLabel is null) return;
        try
        {
            var tween = CreateTween();
            tween.TweenProperty(
                _resultMainLabel,
                "scale",
                new Vector2(ResultPunchScalePeak, ResultPunchScalePeak),
                ResultPunchDurationSec);
            tween.TweenProperty(
                _resultMainLabel,
                "scale",
                Vector2.One,
                ResultPunchDurationSec);
        }
        catch
        {
            // Godot 非依存テスト環境では演出をスキップ
        }
    }

    // ─── ローカライゼーション（データ名は必ずリゾルバ経由） ────────────────

    private string FormatUnitDisplay(Unit unit)
    {
        var equip = unit.MainEquipment is null
            ? "[装備なし]"
            : $"[{ItemName(unit.MainEquipment.ItemId)} Lv{unit.MainEquipment.Level}]";
        return $"{DisplayName(unit)} [{JobName(unit.Job)}] Lv{unit.Level} (Age {unit.Age}) {equip}";
    }

    private string DisplayName(Unit unit)
        => _chronicleGlobal?.ResolveDisplayName(unit) ?? unit.FirstNameKey;

    private string JobName(JobId job)
        => _chronicleGlobal?.ResolveJobName(job) ?? job.ToString();

    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();
}
