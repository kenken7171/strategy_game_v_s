// =============================================================================
//  ChronicleKnights — MarriageUI.cs
// -----------------------------------------------------------------------------
//  拠点B: 婚姻・スカウト（家系図運営）画面。
//
//  3 つの主要セクションで構成:
//
//   ┌─ 💞 手動婚姻 ─────────────────────────────────────┐
//   │  父: [選択 ▼]   母: [選択 ▼]                       │
//   │  💞 必要ポイント: 15 pt  / または  💘 タダ結婚成立│
//   │  ✅ 残高十分 / ❌ 残高不足                          │
//   │  [結婚させる]                                       │
//   └────────────────────────────────────────────────────┘
//
//   ┌─ ⚔ 外様スカウト ──────────────────────────────────┐
//   │  血縁関係のない外様ユニットを 3 pt で雇用          │
//   │  [スカウトする (3 pt)]                              │
//   └────────────────────────────────────────────────────┘
//
//   ┌─ 👶 家系図（子供たち） ───────────────────────────┐
//   │  ── 入団待ち (Age ≥ 15) ──                          │
//   │  🎓 Sniper (Age 16)   [0 pt で正式加入]            │
//   │  🎓 Medic  (Age 17)   [0 pt で正式加入]            │
//   │  ── 成長中 (Age < 15) ──                            │
//   │  👶 Tactician (Age 3 / 15)                          │
//   │  👶 Sorcerer (Age 8 / 15)                           │
//   └────────────────────────────────────────────────────┘
//
//  シグナル購読:
//    - EconomyChanged   → 残高 / 見積もり / 実行ボタン活性を再描画
//    - RosterChanged    → 父母セレクタ / 家系図リストを再描画
//    - StateInitialized → 全体再描画
//
//  ★ 「ポイントを消費して雇うのは血縁なしの外様のみ」「子供は 0 pt で合流」
//    という設計憲法に基づき、子供（Age 0 で誕生したユニット）は ChronicleGlobal
//    のロスタに既に含まれているため、本 UI の「正式加入」ボタンは儀式的な確認
//    (HashSet で「承認済み」状態を管理)。
//
//  ★ 外様スカウト機能は ChronicleGlobal.ExecuteScout(cost) を呼び出す。
//    ポイント検証・消費・血縁なしユニット生成・ロスタ追加・シグナル発火までを
//    Autoload 側が一括処理し、UI はシグナル経由で自動再描画される。
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
/// 婚姻・スカウト・家系図運営の総合 UI 画面。
/// </summary>
public partial class MarriageUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>戦闘・婚姻に参加可能となる成人年齢。</summary>
    private const int AdultAge = 15;

    /// <summary>外様スカウトの固定コスト (pt)。</summary>
    private const int ScoutCost = 3;

    /// <summary>家系図「成長中」表示対象の最大年齢 (Age &lt; AdultAge)。</summary>
    private const int MinorMaxAge = AdultAge - 1;

    /// <summary>子の既定寿命（newborn 生成時のデフォルト）。</summary>
    private const int ChildDefaultMaxAge = 60;

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private Label? _balanceLabel;

    // 婚姻セクション
    private OptionButton? _fatherSelect;
    private OptionButton? _motherSelect;
    private Label? _quoteLabel;
    private Button? _marriageExecuteButton;

    // スカウトセクション
    private Button? _scoutButton;
    private Label? _scoutHintLabel;

    // 家系図セクション
    private VBoxContainer? _readyChildrenContainer;
    private VBoxContainer? _minorChildrenContainer;

    // 人事（戦力外通告）セクション
    private VBoxContainer? _dismissListContainer;

    // ─── 内部状態 ─────────────────────────────────────────────────────────

    /// <summary>OptionButton index → Unit.Id のマッピング（父選択用）</summary>
    private readonly List<Guid> _fatherSelectableIds = new();

    /// <summary>OptionButton index → Unit.Id のマッピング（母選択用）</summary>
    private readonly List<Guid> _motherSelectableIds = new();

    /// <summary>
    /// 0 pt 儀式的に「正式加入」済みの子供 ID。UI ローカル状態（ChronicleGlobal
    /// 側のロスタには既に居るため、これは表示フィルタ用）。
    /// </summary>
    private readonly HashSet<Guid> _ceremoniallyEnlisted = new();

    /// <summary>
    /// 戦力外通告の確認待ち対象 ID。解雇は不可逆なため、行内 2 段階確認
    /// （[戦力外通告] → [解雇する]／[やめる]）の「武装」状態をこの 1 件で表す。
    /// null なら確認待ちなし。Roster 再描画をまたいでも保持する（誤爆防止）。
    /// </summary>
    private Guid? _pendingDismissId;

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");
        BuildUI();
        SubscribeSignals();
        RenderAll();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
    }

    // ─── UI 構築 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 20);
        root.SetMeta(TestIdMetaKey, "marriage-root");
        AddChild(root);

        // ─ ヘッダー ─────────────────────────────────────────────
        var header = new HBoxContainer();
        header.SetMeta(TestIdMetaKey, "marriage-header");
        root.AddChild(header);
        var headerTitle = new Label { Text = "💞 婚姻・スカウト・家系図" };
        headerTitle.SetMeta(TestIdMetaKey, "marriage-header-title");
        header.AddChild(headerTitle);
        _balanceLabel = new Label();
        _balanceLabel.SetMeta(TestIdMetaKey, "marriage-balance");
        header.AddChild(_balanceLabel);

        // ─ 婚姻セクション ───────────────────────────────────────
        var marriageSection = new VBoxContainer();
        marriageSection.SetMeta(TestIdMetaKey, "marriage-pairing-section");
        root.AddChild(marriageSection);
        var marriageTitle = new Label { Text = "── 💞 手動婚姻 ──" };
        marriageTitle.SetMeta(TestIdMetaKey, "marriage-pairing-title");
        marriageSection.AddChild(marriageTitle);

        var pairRow = new HBoxContainer();
        pairRow.AddThemeConstantOverride("separation", 12);
        pairRow.SetMeta(TestIdMetaKey, "marriage-pairing-row");
        marriageSection.AddChild(pairRow);

        var fatherLabel = new Label { Text = "父:" };
        fatherLabel.SetMeta(TestIdMetaKey, "marriage-father-label");
        pairRow.AddChild(fatherLabel);
        _fatherSelect = new OptionButton();
        _fatherSelect.SetMeta(TestIdMetaKey, "marriage-father-select");
        _fatherSelect.ItemSelected += OnFatherSelectionChanged;
        pairRow.AddChild(_fatherSelect);

        var motherLabel = new Label { Text = "母:" };
        motherLabel.SetMeta(TestIdMetaKey, "marriage-mother-label");
        pairRow.AddChild(motherLabel);
        _motherSelect = new OptionButton();
        _motherSelect.SetMeta(TestIdMetaKey, "marriage-mother-select");
        _motherSelect.ItemSelected += OnMotherSelectionChanged;
        pairRow.AddChild(_motherSelect);

        _quoteLabel = new Label { Text = "💡 父・母を選択してください" };
        _quoteLabel.SetMeta(TestIdMetaKey, "marriage-quote");
        marriageSection.AddChild(_quoteLabel);

        _marriageExecuteButton = new Button { Text = "💞 結婚させる", Disabled = true };
        _marriageExecuteButton.SetMeta(TestIdMetaKey, "marriage-execute-button");
        _marriageExecuteButton.Pressed += OnMarriageExecutePressed;
        marriageSection.AddChild(_marriageExecuteButton);

        // ─ スカウトセクション ───────────────────────────────────
        var scoutSection = new VBoxContainer();
        scoutSection.SetMeta(TestIdMetaKey, "marriage-scout-section");
        root.AddChild(scoutSection);
        var scoutTitle = new Label { Text = "── ⚔ 外様スカウト ──" };
        scoutTitle.SetMeta(TestIdMetaKey, "marriage-scout-title");
        scoutSection.AddChild(scoutTitle);

        _scoutHintLabel = new Label
        {
            Text = $"血縁関係のない外様を {ScoutCost} pt で雇用",
        };
        _scoutHintLabel.SetMeta(TestIdMetaKey, "marriage-scout-hint");
        scoutSection.AddChild(_scoutHintLabel);

        _scoutButton = new Button { Text = $"⚔ スカウトする ({ScoutCost} pt)" };
        _scoutButton.SetMeta(TestIdMetaKey, "marriage-scout-button");
        _scoutButton.Pressed += OnScoutPressed;
        scoutSection.AddChild(_scoutButton);

        // ─ 家系図セクション ─────────────────────────────────────
        var familySection = new VBoxContainer();
        familySection.SetMeta(TestIdMetaKey, "marriage-family-section");
        root.AddChild(familySection);
        var familyTitle = new Label { Text = "── 👶 家系図（子供たち） ──" };
        familyTitle.SetMeta(TestIdMetaKey, "marriage-family-title");
        familySection.AddChild(familyTitle);

        var readyLabel = new Label { Text = $"🎓 入団待ち (Age ≥ {AdultAge})" };
        readyLabel.SetMeta(TestIdMetaKey, "marriage-family-ready-label");
        familySection.AddChild(readyLabel);
        _readyChildrenContainer = new VBoxContainer();
        _readyChildrenContainer.SetMeta(TestIdMetaKey, "marriage-family-ready-list");
        familySection.AddChild(_readyChildrenContainer);

        var minorLabel = new Label { Text = $"👶 成長中 (Age < {AdultAge})" };
        minorLabel.SetMeta(TestIdMetaKey, "marriage-family-minor-label");
        familySection.AddChild(minorLabel);
        _minorChildrenContainer = new VBoxContainer();
        _minorChildrenContainer.SetMeta(TestIdMetaKey, "marriage-family-minor-list");
        familySection.AddChild(_minorChildrenContainer);

        // ─ 人事（戦力外通告）セクション ─────────────────────────
        // 旅団の新陳代謝をプレイヤーの手に戻す。寿命前の現役を任意に外す手動解雇。
        var dismissSection = new VBoxContainer();
        dismissSection.SetMeta(TestIdMetaKey, "marriage-dismiss-section");
        root.AddChild(dismissSection);
        var dismissTitle = new Label { Text = "── 🛡 人事（戦力外通告） ──" };
        dismissTitle.SetMeta(TestIdMetaKey, "marriage-dismiss-title");
        dismissSection.AddChild(dismissTitle);

        var dismissHint = new Label
        {
            Text = "現役を任意に解雇して席を空ける（不可逆・払い戻しなし）",
        };
        dismissHint.SetMeta(TestIdMetaKey, "marriage-dismiss-hint");
        dismissSection.AddChild(dismissHint);

        _dismissListContainer = new VBoxContainer();
        _dismissListContainer.SetMeta(TestIdMetaKey, "marriage-dismiss-list");
        dismissSection.AddChild(_dismissListContainer);
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.EconomyChanged   += OnEconomyChanged;
        _chronicleGlobal.RosterChanged    += OnRosterChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.EconomyChanged   -= OnEconomyChanged;
            _chronicleGlobal.RosterChanged    -= OnRosterChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnEconomyChanged()
    {
        RenderBalance();
        RenderQuote(); // 残高変動で affordable が変わる可能性
        RenderScoutButton();
    }

    private void OnRosterChanged()
    {
        RenderUnitSelectors();
        RenderQuote();
        RenderChildrenLists();
        RenderDismissList();
    }

    private void OnStateInitialized() => RenderAll();

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        RenderBalance();
        RenderUnitSelectors();
        RenderQuote();
        RenderScoutButton();
        RenderChildrenLists();
        RenderDismissList();
    }

    private void RenderBalance()
    {
        if (_chronicleGlobal is null || _balanceLabel is null) return;
        _balanceLabel.Text = $"💰 残高: {_chronicleGlobal.CurrentEconomy.CurrentBalance} pt";
    }

    private void RenderUnitSelectors()
    {
        if (_chronicleGlobal is null || _fatherSelect is null || _motherSelect is null) return;

        // 現在の選択を保存（再生成後に復元するため）
        var prevFather = TryGetSelectedId(_fatherSelect, _fatherSelectableIds);
        var prevMother = TryGetSelectedId(_motherSelect, _motherSelectableIds);

        _fatherSelect.Clear();
        _motherSelect.Clear();
        _fatherSelectableIds.Clear();
        _motherSelectableIds.Clear();

        foreach (var unit in _chronicleGlobal.GetAliveUnits())
        {
            // 成人のみ婚姻可能
            if (unit.Age < AdultAge) continue;
            var display = FormatUnitDisplay(unit);
            _fatherSelect.AddItem(display);
            _motherSelect.AddItem(display);
            _fatherSelectableIds.Add(unit.Id);
            _motherSelectableIds.Add(unit.Id);
        }

        // 選択を復元
        RestoreSelection(_fatherSelect, _fatherSelectableIds, prevFather);
        RestoreSelection(_motherSelect, _motherSelectableIds, prevMother);
    }

    private void RenderQuote()
    {
        if (_chronicleGlobal is null || _quoteLabel is null || _marriageExecuteButton is null) return;

        var fatherId = TryGetSelectedId(_fatherSelect, _fatherSelectableIds);
        var motherId = TryGetSelectedId(_motherSelect, _motherSelectableIds);

        if (!fatherId.HasValue || !motherId.HasValue)
        {
            _quoteLabel.Text = "💡 父・母を選択してください";
            _marriageExecuteButton.Disabled = true;
            return;
        }

        if (fatherId.Value == motherId.Value)
        {
            _quoteLabel.Text = "❌ 父と母は異なるユニットを選んでください";
            _marriageExecuteButton.Disabled = true;
            return;
        }

        var father = _chronicleGlobal.FindUnit(fatherId.Value);
        var mother = _chronicleGlobal.FindUnit(motherId.Value);
        if (father is null || mother is null)
        {
            _quoteLabel.Text = "❌ 選択されたユニットが見つかりません";
            _marriageExecuteButton.Disabled = true;
            return;
        }

        var quote = MarriageService.QuoteMarriage(_chronicleGlobal.CurrentEconomy, father, mother);
        var costText = quote.IsNaturalMarriage
            ? "💘 タダ結婚（純愛ルート成立！自然婚姻ポイント MAX）"
            : $"💞 必要ポイント: {quote.Cost} pt";
        var affordText = quote.IsAffordable
            ? "✅ 残高十分"
            : "❌ 残高不足";
        _quoteLabel.Text = $"{costText}\n{affordText}";
        _marriageExecuteButton.Disabled = !quote.IsAffordable;
    }

    private void RenderScoutButton()
    {
        if (_chronicleGlobal is null || _scoutButton is null) return;
        _scoutButton.Disabled = !_chronicleGlobal.CurrentEconomy.CanAfford(ScoutCost);
    }

    private void RenderChildrenLists()
    {
        if (_chronicleGlobal is null) return;
        if (_readyChildrenContainer is null || _minorChildrenContainer is null) return;

        // 既存子要素をクリア
        foreach (var c in _readyChildrenContainer.GetChildren()) c.QueueFree();
        foreach (var c in _minorChildrenContainer.GetChildren()) c.QueueFree();

        foreach (var unit in _chronicleGlobal.BattalionRoster)
        {
            if (!unit.IsAlive) continue;

            if (unit.Age < AdultAge)
            {
                // 成長中 (0〜14歳)
                var row = new HBoxContainer();
                row.SetMeta(TestIdMetaKey, $"marriage-family-minor-row-{unit.Id}");
                var minorName = new Label
                {
                    Text = $"👶 {JobName(unit.Job)} (Age {unit.Age} / {AdultAge})",
                };
                minorName.SetMeta(TestIdMetaKey, $"marriage-family-minor-name-{unit.Id}");
                row.AddChild(minorName);
                _minorChildrenContainer.AddChild(row);
            }
            else if (unit.Age <= AdultAge + 2 && !_ceremoniallyEnlisted.Contains(unit.Id))
            {
                // 入団待ち（Age 15〜17 で、まだ正式加入の儀式を経ていない子）
                // ※ Age >= 18 は通常成人扱いで本リストから外す
                var row = new HBoxContainer();
                row.SetMeta(TestIdMetaKey, $"marriage-family-ready-row-{unit.Id}");
                var readyName = new Label
                {
                    Text = $"🎓 {JobName(unit.Job)} (Age {unit.Age})",
                };
                readyName.SetMeta(TestIdMetaKey, $"marriage-family-ready-name-{unit.Id}");
                row.AddChild(readyName);
                var enlistBtn = new Button { Text = "0 pt で正式加入" };
                enlistBtn.SetMeta(TestIdMetaKey, $"marriage-family-enlist-button-{unit.Id}");
                var capturedId = unit.Id;
                enlistBtn.Pressed += () => OnEnlistChildPressed(capturedId);
                row.AddChild(enlistBtn);
                _readyChildrenContainer.AddChild(row);
            }
        }
    }

    /// <summary>
    /// 人事（戦力外通告）セクションのロスタ一覧を、現在の生存者から無状態に再構築する。
    /// 各行は「[戦力外通告]」ボタンを持ち、押下で当該行のみ確認状態（[解雇する]／[やめる]）
    /// へ切り替わる（<see cref="_pendingDismissId"/> による行内 2 段階確認）。
    /// SoT を一切キャッシュせず、毎回 GetAliveUnits を読み直す（ロスタ変更に追従）。
    /// </summary>
    private void RenderDismissList()
    {
        if (_chronicleGlobal is null || _dismissListContainer is null) return;

        // 既存行を破棄してから現在の生存者で組み直す（ゾンビ行を残さない）。
        foreach (var c in _dismissListContainer.GetChildren()) c.QueueFree();

        var alive = _chronicleGlobal.GetAliveUnits();
        if (alive.Count == 0)
        {
            var empty = new Label { Text = "（解雇できる現役がいません）" };
            empty.SetMeta(TestIdMetaKey, "marriage-dismiss-empty");
            _dismissListContainer.AddChild(empty);
            return;
        }

        foreach (var unit in alive)
        {
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SetMeta(TestIdMetaKey, $"marriage-dismiss-row-{capturedId}");

            var name = new Label
            {
                Text = $"🎖 {JobName(unit.Job)} Lv{unit.Level} (Age {unit.Age}) "
                       + _chronicleGlobal.ResolveDisplayName(unit),
            };
            name.SetMeta(TestIdMetaKey, $"marriage-dismiss-name-{capturedId}");
            row.AddChild(name);

            if (_pendingDismissId == capturedId)
            {
                // 武装状態: 確認ラベル + [解雇する] + [やめる]（不可逆操作の誤爆防止）。
                var confirmLabel = new Label { Text = "⚠ 本当に解雇しますか？" };
                confirmLabel.SetMeta(TestIdMetaKey, $"marriage-dismiss-confirm-label-{capturedId}");
                row.AddChild(confirmLabel);

                var confirmBtn = new Button { Text = "解雇する" };
                confirmBtn.SetMeta(TestIdMetaKey, $"marriage-dismiss-confirm-button-{capturedId}");
                confirmBtn.Pressed += () => OnDismissConfirmPressed(capturedId);
                row.AddChild(confirmBtn);

                var cancelBtn = new Button { Text = "やめる" };
                cancelBtn.SetMeta(TestIdMetaKey, $"marriage-dismiss-cancel-button-{capturedId}");
                cancelBtn.Pressed += OnDismissCancelPressed;
                row.AddChild(cancelBtn);
            }
            else
            {
                var armBtn = new Button { Text = "戦力外通告" };
                armBtn.SetMeta(TestIdMetaKey, $"marriage-dismiss-button-{capturedId}");
                armBtn.Pressed += () => OnDismissArmPressed(capturedId);
                row.AddChild(armBtn);
            }

            _dismissListContainer.AddChild(row);
        }
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    private void OnFatherSelectionChanged(long _) => RenderQuote();
    private void OnMotherSelectionChanged(long _) => RenderQuote();

    private void OnMarriageExecutePressed()
    {
        if (_chronicleGlobal is null) return;
        var fatherId = TryGetSelectedId(_fatherSelect, _fatherSelectableIds);
        var motherId = TryGetSelectedId(_motherSelect, _motherSelectableIds);
        if (!fatherId.HasValue || !motherId.HasValue) return;
        if (fatherId.Value == motherId.Value) return;

        // newborn 仕様: 名前キー（FirstNameKey/LastNameKey）は意図的に未指定とし、
        // MarriageService が両親から継承した文化圏で重複しないキーを自動生成する。
        var newborn = new NewbornSpec
        {
            InitialAge   = 0,
            MaxAge       = ChildDefaultMaxAge,
            // FirstNameKey / LastNameKey 未指定 → NameGenerator が文化圏継承で自動生成
            // OverrideJob = null → 父母から 50/50 で乱数継承（MarriageService 既定）
            // InitialEquipment = null → 装備なしで誕生（Affix 継承の余白は将来）
        };

        var result = _chronicleGlobal.ExecuteMarriage(fatherId.Value, motherId.Value, newborn);

        if (result is null)
        {
            GD.Print("[MarriageUI] 💔 婚姻失敗（残高不足／条件不一致）");
        }
        else
        {
            var modeText = result.WasNaturalMarriage ? "💘 純愛タダ結婚" : $"💞 通常婚姻 ({result.CostPaid} pt)";
            GD.Print($"[MarriageUI] {modeText} 成立 / 子誕生 Id={result.Child.Id}");
        }
        // 画面再描画はシグナル経由で自動
    }

    private void OnScoutPressed()
    {
        if (_chronicleGlobal is null) return;

        // ChronicleGlobal.ExecuteScout がポイント検証・消費・外様生成・ロスタ追加・
        // シグナル発火までを一括で行う。残高不足や失敗時は null が返る。
        var recruited = _chronicleGlobal.ExecuteScout(ScoutCost);

        if (recruited is null)
        {
            GD.Print($"[MarriageUI] 💸 スカウト失敗: 残高不足／状態未初期化 (need {ScoutCost} pt)");
            return;
        }

        GD.Print(
            $"[MarriageUI] ⚔ 外様スカウト成立 ({ScoutCost} pt) / " +
            $"{JobName(recruited.Job)} (Age {recruited.Age}) Id={recruited.Id}");
        // 残高・父母セレクタ・家系図の再描画はシグナル経由で自動
    }

    private void OnEnlistChildPressed(Guid childId)
    {
        // 子は既に BattalionRoster に居る（marriage で追加済み）。
        // 本ボタンは「家系の長が正式に大隊員として認める儀式」を示し、
        // UI 側で「儀式済み」状態を管理することで以降のリスト表示から除外する。
        _ceremoniallyEnlisted.Add(childId);
        GD.Print($"[MarriageUI] 🎓 0 pt で正式入団: {childId}");
        RenderChildrenLists();
    }

    // ─── 人事（戦力外通告）ハンドラ ───────────────────────────────────────

    /// <summary>
    /// 解雇ボタン押下: 当該ユニットを確認待ち（武装）状態にして行を組み直す。
    /// 実際の解雇はまだ起きない（次の [解雇する] 押下で確定する）。
    /// </summary>
    private void OnDismissArmPressed(Guid unitId)
    {
        _pendingDismissId = unitId;
        RenderDismissList();
    }

    /// <summary>[やめる] 押下: 確認待ちを解除して通常表示へ戻す（何も外さない）。</summary>
    private void OnDismissCancelPressed()
    {
        _pendingDismissId = null;
        RenderDismissList();
    }

    /// <summary>
    /// [解雇する] 押下: ChronicleGlobal.ExecuteDismiss でロスタから即時に外す。
    /// 成功時はロスタ変更シグナル経由で OnRosterChanged → RenderDismissList が自動再描画
    /// するため、ここでは武装解除だけ行う。失敗（対象不在）時はシグナルが飛ばないので
    /// 手動で再描画して武装状態を畳む。
    /// </summary>
    private void OnDismissConfirmPressed(Guid unitId)
    {
        if (_chronicleGlobal is null) return;

        // 確認待ちは結果に関わらず解除（成功なら行ごと消え、失敗なら通常表示に戻る）。
        _pendingDismissId = null;

        var dismissed = _chronicleGlobal.ExecuteDismiss(unitId);
        if (dismissed is null)
        {
            GD.Print($"[MarriageUI] 🛡 戦力外通告 失敗: 対象不在／未初期化 Id={unitId}");
            RenderDismissList(); // 失敗時はシグナル無し → 手動で武装解除を反映
            return;
        }

        GD.Print(
            $"[MarriageUI] 🛡 戦力外通告 成立 / {JobName(dismissed.Job)} " +
            $"(Age {dismissed.Age}) Id={dismissed.Id}");
        // 残高・父母セレクタ・家系図・解雇一覧の再描画はシグナル経由で自動。
    }

    // ─── ヘルパー ─────────────────────────────────────────────────────────

    private static Guid? TryGetSelectedId(OptionButton? select, IReadOnlyList<Guid> ids)
    {
        if (select is null) return null;
        var idx = select.Selected;
        if (idx < 0 || idx >= ids.Count) return null;
        return ids[idx];
    }

    private static void RestoreSelection(OptionButton select, IReadOnlyList<Guid> ids, Guid? targetId)
    {
        if (!targetId.HasValue) return;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == targetId.Value)
            {
                select.Selected = i;
                return;
            }
        }
    }

    private string FormatUnitDisplay(Unit unit)
    {
        var equip = unit.MainEquipment is null
            ? "—"
            : $"{ItemName(unit.MainEquipment.ItemId)} Lv{unit.MainEquipment.Level}";
        return $"{JobName(unit.Job)} Lv{unit.Level} (Age {unit.Age}) / 装備: {equip}";
    }

    // ─── ローカライゼーション ─────────────────────────────────────────────
    // ジョブ名・アイテム名の表示テキストは ChronicleGlobal.ResolveJobName /
    // ResolveItemName（内部で純粋層 MasterDataNameResolver が localization_ja.json の
    // jobs.{JobId}.name / items.{ItemId}.name を引く）に委譲する。本ファイルには
    // 日本語・絵文字を一切ハードコードしない（設計憲法 ①）。Autoload 未取得時は
    // enum 名（ToString）へフォールバックして画面を落とさない。

    private string JobName(JobId job)
        => _chronicleGlobal?.ResolveJobName(job) ?? job.ToString();

    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();
}
