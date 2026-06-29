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
//   │  ── 入団待ち ──                                     │
//   │  🎓 Sniper 16歳   [0 pt で正式加入]                │
//   │  🎓 Medic  17歳   [0 pt で正式加入]                │
//   │  ── 成長中 ──                                       │
//   │  👶 Tactician 3歳                                   │
//   │  👶 Sorcerer 8歳                                    │
//   └────────────────────────────────────────────────────┘
//
//  シグナル購読:
//    - EconomyChanged   → 見積もり / 実行ボタン活性を再描画（残高表示は固定ヘッダへ集約）
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
using ChronicleKnights.Core.GameFlow;        // PlannedAction (今年の行動: 出撃 / 休息)
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Shop;
using ChronicleKnights.Core.Units;
using ChronicleKnights.UserInterface;        // JobTextureLibrary（ジョブ立ち絵アイコン）
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

    /// <summary>子の既定寿命（newborn 生成時のデフォルト）。</summary>
    private const int ChildDefaultMaxAge = 60;

    /// <summary>右タブナビのボタン横幅（px）。固定幅で左の内容と分ける。</summary>
    private const int TabNavWidthPx = 180;

    /// <summary>人事フェーズの 4 タブ（右ナビで切り替え・左にアクティブタブの内容を出す）。</summary>
    private enum GuildTab { UnitList, Scout, Item, Marriage }

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>一覧・プルダウンに出すジョブ立ち絵アイコンの一辺サイズ（px）。立ち絵は縦長で
    /// 巨大なため、小さく制限しないと文字が見えなくなる（OptionButton は icon_max_width で制限）。</summary>
    private const int UnitIconSize = 28;

    /// <summary>ユニットリストタブの立ち絵アイコンの一辺サイズ（px）。1 行を大きく見やすくするため
    /// 共有の小アイコン（<see cref="UnitIconSize"/>）より大きくする（一度に見える件数は自然と減る）。</summary>
    private const int UnitListIconSize = 96;

    // ─── 意思表示イベント（オーバーレイのマウントは購読側 = GameDirector が引く） ──

    /// <summary>
    /// ユニット詳細オーバーレイを開く意思表示。ユニットリストタブの各行 ［詳細］押下で、購読側
    /// （GameDirector）が UnitDetailOverlay を最前面へマウントする（FormationUI と同じ窓口）。
    /// 家系図・戦力外通告（解雇）はその詳細オーバーレイ内から行う（per-unit アクションの集約先）。
    /// </summary>
    public event Action<Guid>? UnitInspectRequested;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────


    // タブ基盤（右ナビ＋4パネル。アクティブのみ Visible）
    private GuildTab _activeTab = GuildTab.UnitList;
    private readonly Dictionary<GuildTab, Button> _tabButtons = new();
    private readonly Dictionary<GuildTab, Control> _tabPanels = new();

    // 今年の行動（出撃 / 休息）の提示 — 編成より上流のこの拠点フェーズで確定済み。全タブ共通で上部表示。
    private VBoxContainer? _actionContainer;

    // ユニットリストタブ
    private VBoxContainer? _unitListContainer;

    // スカウトタブ（候補プールを並べて選んで採用）
    private VBoxContainer? _scoutCandidatesContainer;

    // 婚姻セクション
    private OptionButton? _fatherSelect;
    private OptionButton? _motherSelect;
    private Label? _quoteLabel;
    private Button? _marriageExecuteButton;

    // 家系図セクション
    private VBoxContainer? _readyChildrenContainer;
    private VBoxContainer? _minorChildrenContainer;

    // 旅団兵器廠（商店・強化）セクション
    private VBoxContainer? _shopListContainer;

    // 持ち物（装備の付け替え）セクション
    private VBoxContainer? _inventoryListContainer;

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
    /// 装備強化の確認待ち対象 ID。購入と同じく行内 2 段階確認（[強化] → [強化する]／[やめる]）の
    /// 「武装」状態をこの 1 件で表す。null なら確認待ちなし。Roster 再描画をまたいでも保持する。
    /// </summary>
    private Guid? _pendingUpgradeId;

    /// <summary>
    /// 装備購入の確認待ち対象 ID。武装すると当該行に 5 大マスターの購入ボタンが展開される
    /// （[購入] → [剣]/[弓]/…/[やめる]）。null なら確認待ちなし。強化と相互排他に保つ。
    /// </summary>
    private Guid? _pendingBuyId;

    /// <summary>
    /// 持ち物からの装備で、装備先ユニットを選ぶ確認待ち対象の装備個体 ID。武装すると当該
    /// 持ち物行に生存者ユニットのボタン群（[装備先] → [○○]/…/[やめる]）が展開される。
    /// null なら確認待ちなし。Inventory/Roster 再描画をまたいでも保持する。
    /// </summary>
    private Guid? _pendingEquipItemId;

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
        // 画面全体を左右に分割: 左＝アクティブタブの内容（縦スクロール）／右＝タブナビ。
        var split = new HBoxContainer();
        split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        split.SetMeta(TestIdMetaKey, "guild-split");
        AddChild(split);

        // ── 左: タブ内容（縦スクロール。横は無効化し子幅を伸張） ──
        var leftScroll = new ScrollContainer();
        leftScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        leftScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        leftScroll.SetMeta(TestIdMetaKey, "guild-content-scroll");
        split.AddChild(leftScroll);

        var content = new VBoxContainer();
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddThemeConstantOverride("separation", 12);
        content.SetMeta(TestIdMetaKey, "guild-content");
        leftScroll.AddChild(content);

        // 今年の行動（出撃 / 休息）の提示は全タブ共通で内容の最上段に出す（行動はこの上流で確定済み）。
        _actionContainer = new VBoxContainer();
        _actionContainer.AddThemeConstantOverride("separation", 4);
        _actionContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _actionContainer.SetMeta(TestIdMetaKey, "marriage-action");
        content.AddChild(_actionContainer);

        // 4 タブパネル（アクティブのみ Visible）。
        _tabPanels[GuildTab.UnitList] = BuildUnitListPanel();
        _tabPanels[GuildTab.Scout]    = BuildScoutPanel();
        _tabPanels[GuildTab.Item]     = BuildItemPanel();
        _tabPanels[GuildTab.Marriage] = BuildMarriagePanel();
        foreach (var panel in _tabPanels.Values) content.AddChild(panel);

        // ── 右: タブナビ ──
        split.AddChild(BuildTabNav());

        ApplyActiveTab();
    }

    // ─── タブナビ・パネル ─────────────────────────────────────────────────

    private VBoxContainer BuildTabNav()
    {
        var nav = new VBoxContainer();
        nav.CustomMinimumSize = new Vector2(TabNavWidthPx, 0);
        nav.AddThemeConstantOverride("separation", 8);
        nav.SetMeta(TestIdMetaKey, "guild-tab-nav");

        AddTabButton(nav, GuildTab.UnitList, "👥 ユニットリスト");
        AddTabButton(nav, GuildTab.Scout,    "⚔ スカウト");
        AddTabButton(nav, GuildTab.Item,     "🎒 アイテム");
        AddTabButton(nav, GuildTab.Marriage, "💞 結婚");
        return nav;
    }

    private void AddTabButton(VBoxContainer nav, GuildTab tab, string label)
    {
        var btn = new Button { Text = label };
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.SetMeta(TestIdMetaKey, $"guild-tab-button-{tab.ToString().ToLowerInvariant()}");
        btn.Pressed += () => OnTabPressed(tab);
        _tabButtons[tab] = btn;
        nav.AddChild(btn);
    }

    private void OnTabPressed(GuildTab tab)
    {
        _activeTab = tab;
        ApplyActiveTab();
    }

    /// <summary>アクティブタブのパネルだけを表示し、ナビのアクティブボタンを金色で強調する。</summary>
    private void ApplyActiveTab()
    {
        foreach (var (tab, panel) in _tabPanels) panel.Visible = tab == _activeTab;
        foreach (var (tab, btn) in _tabButtons)
        {
            btn.Modulate = tab == _activeTab
                ? new Color(1.0f, 0.9f, 0.45f)        // アクティブ＝金
                : new Color(1.0f, 1.0f, 1.0f, 0.7f);  // 非アクティブ＝淡色
        }
    }

    // ── タブ1: ユニットリスト（各行 ［詳細］。解雇・家系図は当面併設＝Phase 3 で詳細へ移設） ──
    private Control BuildUnitListPanel()
    {
        var panel = new VBoxContainer();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 10);
        panel.SetMeta(TestIdMetaKey, "guild-tab-unit-list");

        var title = new Label { Text = "── 👥 ユニットリスト ──" };
        title.SetMeta(TestIdMetaKey, "guild-unit-list-title");
        panel.AddChild(title);

        _unitListContainer = new VBoxContainer();
        _unitListContainer.AddThemeConstantOverride("separation", 12);
        _unitListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _unitListContainer.SetMeta(TestIdMetaKey, "guild-unit-list");
        panel.AddChild(_unitListContainer);

        var hint = new Label { Text = "各行の ［詳細］ から ステータス・家系図・戦力外通告（解雇）を行えます。" };
        hint.SetMeta(TestIdMetaKey, "guild-unit-list-hint");
        panel.AddChild(hint);

        return panel;
    }

    // ── タブ2: スカウト（候補プールから選んで採用） ──
    private Control BuildScoutPanel()
    {
        var panel = new VBoxContainer();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 10);
        panel.SetMeta(TestIdMetaKey, "guild-tab-scout");

        var title = new Label { Text = "── ⚔ スカウト（候補から採用） ──" };
        title.SetMeta(TestIdMetaKey, "guild-scout-title");
        panel.AddChild(title);

        var hint = new Label
        {
            Text = "血縁なしの外様候補。コストは強さ（総合値）に連動。選んで採用する（世代ごとに更新）。",
        };
        hint.SetMeta(TestIdMetaKey, "guild-scout-hint");
        panel.AddChild(hint);

        _scoutCandidatesContainer = new VBoxContainer();
        _scoutCandidatesContainer.AddThemeConstantOverride("separation", 6);
        _scoutCandidatesContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scoutCandidatesContainer.SetMeta(TestIdMetaKey, "guild-scout-list");
        panel.AddChild(_scoutCandidatesContainer);

        return panel;
    }

    // ── タブ3: アイテム（兵器廠 購入・強化 ＋ 持ち物 装備させる） ──
    private Control BuildItemPanel()
    {
        var panel = new VBoxContainer();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 10);
        panel.SetMeta(TestIdMetaKey, "guild-tab-item");

        var shopTitle = new Label { Text = "── 🛡️ 兵器廠（購入・強化） ──" };
        shopTitle.SetMeta(TestIdMetaKey, "shop-title");
        panel.AddChild(shopTitle);
        var shopHint = new Label
        {
            Text = $"装備の購入は {ShopService.BuyCost} pt 固定 / 強化は現Lvに比例（Lv1→2 で {ShopService.UpgradeCostFor(1)} pt）",
        };
        shopHint.SetMeta(TestIdMetaKey, "shop-hint");
        panel.AddChild(shopHint);
        _shopListContainer = new VBoxContainer();
        _shopListContainer.SetMeta(TestIdMetaKey, "shop-list");
        panel.AddChild(_shopListContainer);

        var inventoryTitle = new Label { Text = "── 🎒 持ち物（装備させる・付け替え） ──" };
        inventoryTitle.SetMeta(TestIdMetaKey, "inventory-title");
        panel.AddChild(inventoryTitle);
        var inventoryHint = new Label
        {
            Text = "ドロップ等で得た装備は持ち物に貯まる。外しても消えず持ち物へ戻る（無償・付け替え自由）。",
        };
        inventoryHint.SetMeta(TestIdMetaKey, "inventory-hint");
        panel.AddChild(inventoryHint);
        _inventoryListContainer = new VBoxContainer();
        _inventoryListContainer.SetMeta(TestIdMetaKey, "inventory-list");
        panel.AddChild(_inventoryListContainer);

        return panel;
    }

    // ── タブ4: 結婚（父母選択 → 結婚 ＋ 子供たち） ──
    private Control BuildMarriagePanel()
    {
        var panel = new VBoxContainer();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeConstantOverride("separation", 10);
        panel.SetMeta(TestIdMetaKey, "guild-tab-marriage");

        var marriageTitle = new Label { Text = "── 💞 手動婚姻 ──" };
        marriageTitle.SetMeta(TestIdMetaKey, "marriage-pairing-title");
        panel.AddChild(marriageTitle);

        var pairRow = new HBoxContainer();
        pairRow.AddThemeConstantOverride("separation", 12);
        pairRow.SetMeta(TestIdMetaKey, "marriage-pairing-row");
        panel.AddChild(pairRow);

        var fatherLabel = new Label { Text = "父:" };
        fatherLabel.SetMeta(TestIdMetaKey, "marriage-father-label");
        pairRow.AddChild(fatherLabel);
        _fatherSelect = new OptionButton();
        _fatherSelect.SetMeta(TestIdMetaKey, "marriage-father-select");
        // 立ち絵アイコンは縦長で巨大なため、ドロップダウンのアイコン幅を小さく制限する。
        _fatherSelect.AddThemeConstantOverride("icon_max_width", UnitIconSize);
        _fatherSelect.ItemSelected += OnFatherSelectionChanged;
        pairRow.AddChild(_fatherSelect);

        var motherLabel = new Label { Text = "母:" };
        motherLabel.SetMeta(TestIdMetaKey, "marriage-mother-label");
        pairRow.AddChild(motherLabel);
        _motherSelect = new OptionButton();
        _motherSelect.SetMeta(TestIdMetaKey, "marriage-mother-select");
        _motherSelect.AddThemeConstantOverride("icon_max_width", UnitIconSize);
        _motherSelect.ItemSelected += OnMotherSelectionChanged;
        pairRow.AddChild(_motherSelect);

        _quoteLabel = new Label { Text = "💡 父・母を選択してください" };
        _quoteLabel.SetMeta(TestIdMetaKey, "marriage-quote");
        panel.AddChild(_quoteLabel);

        _marriageExecuteButton = new Button { Text = "💞 結婚させる", Disabled = true };
        _marriageExecuteButton.SetMeta(TestIdMetaKey, "marriage-execute-button");
        _marriageExecuteButton.Pressed += OnMarriageExecutePressed;
        panel.AddChild(_marriageExecuteButton);

        var familyTitle = new Label { Text = "── 👶 家系図（子供たち） ──" };
        familyTitle.SetMeta(TestIdMetaKey, "marriage-family-title");
        panel.AddChild(familyTitle);

        var readyLabel = new Label { Text = "🎓 入団待ち" };
        readyLabel.SetMeta(TestIdMetaKey, "marriage-family-ready-label");
        panel.AddChild(readyLabel);
        _readyChildrenContainer = new VBoxContainer();
        _readyChildrenContainer.SetMeta(TestIdMetaKey, "marriage-family-ready-list");
        panel.AddChild(_readyChildrenContainer);

        var minorLabel = new Label { Text = "👶 成長中" };
        minorLabel.SetMeta(TestIdMetaKey, "marriage-family-minor-label");
        panel.AddChild(minorLabel);
        _minorChildrenContainer = new VBoxContainer();
        _minorChildrenContainer.SetMeta(TestIdMetaKey, "marriage-family-minor-list");
        panel.AddChild(_minorChildrenContainer);

        return panel;
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.EconomyChanged   += OnEconomyChanged;
        _chronicleGlobal.RosterChanged    += OnRosterChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
        // 今年の行動トグルの選択強調を SoT 同期で再描画する（SetPlannedAction は FormationChanged を発火）。
        _chronicleGlobal.FormationChanged += OnFormationChanged;
        // 持ち物の増減（ドロップ取得・付け替え）で持ち物パネルを再描画する。
        _chronicleGlobal.InventoryChanged += OnInventoryChanged;
        // スカウト候補プールの変化（採用・世代更新）でスカウトタブを再描画する。
        _chronicleGlobal.ScoutCandidatesChanged += OnScoutCandidatesChanged;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.EconomyChanged   -= OnEconomyChanged;
            _chronicleGlobal.RosterChanged    -= OnRosterChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
            _chronicleGlobal.FormationChanged -= OnFormationChanged;
            _chronicleGlobal.InventoryChanged -= OnInventoryChanged;
            _chronicleGlobal.ScoutCandidatesChanged -= OnScoutCandidatesChanged;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnEconomyChanged()
    {
        // 残高そのものは GameDirector の固定ヘッダが表示。ここでは残高依存の見積り・活性のみ更新。
        RenderQuote(); // 残高変動で affordable が変わる可能性
        RenderScoutCandidates(); // 残高変動で各候補の採用ボタン活性が変わる
        RenderShop();  // 残高変動で購入・強化ボタンの活性が変わる
    }

    private void OnRosterChanged()
    {
        RenderUnitList();
        RenderUnitSelectors();
        RenderQuote();
        RenderChildrenLists();
        RenderShop();
        RenderInventory();
    }

    private void OnStateInitialized() => RenderAll();

    /// <summary>持ち物が増減した（ドロップ取得・付け替え）ときのハンドラ。持ち物パネルだけを狙い撃ち再描画。</summary>
    private void OnInventoryChanged() => RenderInventory();

    /// <summary>スカウト候補プールが変化した（採用・世代更新）ときのハンドラ。候補リストを再描画。</summary>
    private void OnScoutCandidatesChanged() => RenderScoutCandidates();

    /// <summary>
    /// 今年の行動が変わった（SetPlannedAction が FormationChanged を発火）ときのハンドラ。
    /// 行動トグルの選択強調のみを再描画する（婚姻の父母選択などを巻き込まない狙い撃ち）。
    /// </summary>
    private void OnFormationChanged() => RenderActionChoice();

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        RenderActionChoice();
        RenderUnitList();
        RenderScoutCandidates();
        RenderUnitSelectors();
        RenderQuote();
        RenderChildrenLists();
        RenderShop();
        RenderInventory();
    }

    /// <summary>
    /// 今年の行動を「提示するだけ」の表示ブロックを描く（行動トグルは撤去）。戦う/休むは年代記の予言が
    /// 既に確定している（ActionPhaseRouter.ActionForProphecy: Battle のみ出撃、それ以外は休息）ため、
    /// 拠点では選び直させない——矛盾する選択肢（休息の年に⚔出撃 等）を一切出さない。表示は
    /// ChronicleGlobal.CurrentAction（単一 SoT）を読み直して金字で示すだけ。GameDirector の「次へ」が
    /// この確定行動を見て分岐する: 出撃 → 大隊編成へ入場 / 休息 → 編成・戦闘を経由せず休息報酬へ直行。
    /// </summary>
    private void RenderActionChoice()
    {
        if (_chronicleGlobal is null || _actionContainer is null) return;

        foreach (var child in _actionContainer.GetChildren())
        {
            child.QueueFree();
        }

        var current = _chronicleGlobal.CurrentAction;
        var isBossYear = _chronicleGlobal.IsCurrentYearEpochBossYear();

        // 章ボス年は出撃必至（予言が休息でも強制 March）。その理由をプレイヤーへ明示し、
        // 無言の強制戦闘を「バグ」に見せない。通常年は確定した出撃/休息をそのまま示す。
        string captionText;
        if (isBossYear)
        {
            captionText = "⚠ 章ボス出現の年！出撃必至 ▶「次へ」で大隊編成へ入場（章ボスと決戦）";
        }
        else if (current == PlannedAction.March)
        {
            captionText = "⚔ 出撃の年 ▶「次へ」で大隊編成へ入場（敵と交戦）";
        }
        else
        {
            captionText = "☾ 休息の年 ▶「次へ」で編成・戦闘を回避し安全に年を送る（休息報酬へ）";
        }

        var caption = new Label { Text = captionText };
        caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        caption.AddThemeColorOverride("font_color", Colors.Gold);
        caption.SetMeta(TestIdMetaKey, "marriage-action-caption");
        _actionContainer.AddChild(caption);
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
            var icon = MakeUnitIconThumbnail(unit); // 両辺を抑えたサムネイル（高さも他リストと同等）
            // 性別で振り分ける: 父ドロップダウンは男性のみ、母ドロップダウンは女性のみ。
            // これにより同性ペア・性別逆転ペアを UI 段階で選択不能にする（婚姻=男女ペア）。
            if (unit.Gender == Gender.Male)
            {
                if (icon is not null) _fatherSelect.AddIconItem(icon, display);
                else _fatherSelect.AddItem(display);
                _fatherSelectableIds.Add(unit.Id);
            }
            else
            {
                if (icon is not null) _motherSelect.AddIconItem(icon, display);
                else _motherSelect.AddItem(display);
                _motherSelectableIds.Add(unit.Id);
            }
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

        // 念のための防御線: ドロップダウンは性別分離済みだが、婚姻は男女ペア限定であることを再確認。
        if (!MarriageService.AreOppositeGenders(father, mother))
        {
            _quoteLabel.Text = "❌ 婚姻は男女ペア（父=男性・母=女性）でのみ成立します";
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

    /// <summary>
    /// ユニットリストタブの旅団員一覧を、毎回 GetAliveUnits を読み直して無状態に再構築する。
    /// 各行＝立ち絵 ＋ 種別/Lv/年齢/氏名 ＋ パラメータ ＋ ［詳細］。詳細押下で UnitInspectRequested。
    /// </summary>
    private void RenderUnitList()
    {
        if (_chronicleGlobal is null || _unitListContainer is null) return;

        foreach (var c in _unitListContainer.GetChildren()) c.QueueFree();

        var alive = _chronicleGlobal.GetAliveUnits();
        if (alive.Count == 0)
        {
            var empty = new Label { Text = "（旅団員がいません）" };
            empty.SetMeta(TestIdMetaKey, "guild-unit-list-empty");
            _unitListContainer.AddChild(empty);
            return;
        }

        foreach (var unit in alive)
        {
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.SetMeta(TestIdMetaKey, $"guild-unit-row-{capturedId}");

            // ユニットリストは見やすさ優先で大きめの立ち絵を使う（共有の小アイコンとは別サイズ）。
            var icon = MakeUnitListIcon(unit);
            if (icon is not null) row.AddChild(icon);

            var info = new Label
            {
                Text = $"{JobName(unit.Job)} Lv{unit.Level} (Age {unit.Age}) {_chronicleGlobal.ResolveDisplayName(unit)}\n"
                       + $"パラメータ: {UnitParamLine(unit.Job)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            info.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            info.AddThemeFontSizeOverride("font_size", 18); // 1 行を大きく＝見やすく
            row.AddChild(info);

            var detailBtn = new Button { Text = "詳細" };
            detailBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            detailBtn.SetMeta(TestIdMetaKey, $"guild-unit-detail-button-{capturedId}");
            detailBtn.Pressed += () => OnUnitDetailsPressed(capturedId);
            row.AddChild(detailBtn);

            _unitListContainer.AddChild(row);
        }
    }

    /// <summary>
    /// スカウトタブの候補プール（<see cref="ChronicleGlobal.ScoutCandidates"/>）を無状態に再構築する。
    /// 各行＝立ち絵 ＋ 種別/年齢/氏名 ＋ パラメータ ＋ ［スカウト (cost pt)］（残高不足は Disabled）。
    /// </summary>
    private void RenderScoutCandidates()
    {
        if (_chronicleGlobal is null || _scoutCandidatesContainer is null) return;

        foreach (var c in _scoutCandidatesContainer.GetChildren()) c.QueueFree();

        var candidates = _chronicleGlobal.ScoutCandidates;
        if (candidates.Count == 0)
        {
            var empty = new Label { Text = "（スカウト候補がいません）" };
            empty.SetMeta(TestIdMetaKey, "guild-scout-empty");
            _scoutCandidatesContainer.AddChild(empty);
            return;
        }

        var economy = _chronicleGlobal.CurrentEconomy;
        foreach (var cand in candidates)
        {
            var unit = cand.Unit;
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.SetMeta(TestIdMetaKey, $"guild-scout-row-{capturedId}");

            var icon = MakeUnitIcon(unit);
            if (icon is not null) row.AddChild(icon);

            var info = new Label
            {
                Text = $"{JobName(unit.Job)} (Age {unit.Age}) {_chronicleGlobal.ResolveDisplayName(unit)}\n"
                       + $"パラメータ: {UnitParamLine(unit.Job)}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(info);

            var canAfford = economy.CanAfford(cand.Cost);
            var scoutBtn = new Button { Text = $"スカウト ({cand.Cost} pt)", Disabled = !canAfford };
            scoutBtn.SetMeta(TestIdMetaKey, $"guild-scout-button-{capturedId}");
            scoutBtn.Pressed += () => OnScoutCandidatePressed(capturedId);
            row.AddChild(scoutBtn);

            _scoutCandidatesContainer.AddChild(row);
        }
    }

    /// <summary>ジョブの素ステ（HP/前/後/速）＋総合値を 1 行へ。数値 SoT は JobMaster のみ。</summary>
    private static string UnitParamLine(JobId job)
    {
        var s = JobMaster.All[job].Stats;
        var rating = JobMaster.TargetRating[job];
        return $"HP{s.MaxHp} 前{s.FrontAttack} 後{s.RearAttack} 速{s.Speed}（総合{rating}）";
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
                var minorIcon = MakeUnitIcon(unit);
                if (minorIcon is not null) row.AddChild(minorIcon);
                var minorName = new Label
                {
                    Text = $"👶 {JobName(unit.Job)} {unit.Age}歳",
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
                var readyIcon = MakeUnitIcon(unit);
                if (readyIcon is not null) row.AddChild(readyIcon);
                var readyName = new Label
                {
                    Text = $"🎓 {JobName(unit.Job)} {unit.Age}歳",
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
    /// 旅団兵器廠（商店・強化）セクションを、現在の生存者から無状態に再構築する。
    ///
    /// 各行の出し分け（装備の有無・上限・確認待ちで分岐）:
    ///   - 装備なし     → [購入]（武装で 5 大マスターの購入ボタン群＋[やめる]を展開）。
    ///   - 装備あり・未上限 → [強化 (cost pt)]（武装で [強化する]／[やめる]）。
    ///   - 装備あり・上限   → 「Lv5 最大」ラベル（これ以上は強化不可）。
    ///
    /// 残高不足の実行ボタンは Disabled にする（押下不能で誤操作を防ぐ）。コストの SoT は
    /// すべて <see cref="ShopService"/>（BuyCost / UpgradeCostFor）に委ねる（UI で式を持たない）。
    /// SoT を一切キャッシュせず毎回 GetAliveUnits を読み直す（ロスタ／残高変更に追従）。
    /// </summary>
    private void RenderShop()
    {
        if (_chronicleGlobal is null || _shopListContainer is null) return;

        // 既存行を破棄してから現在の生存者で組み直す（ゾンビ行を残さない）。
        foreach (var c in _shopListContainer.GetChildren()) c.QueueFree();

        var alive = _chronicleGlobal.GetAliveUnits();
        if (alive.Count == 0)
        {
            var empty = new Label { Text = "（装備を購入・強化できる現役がいません）" };
            empty.SetMeta(TestIdMetaKey, "shop-empty");
            _shopListContainer.AddChild(empty);
            return;
        }

        var economy = _chronicleGlobal.CurrentEconomy;

        foreach (var unit in alive)
        {
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SetMeta(TestIdMetaKey, $"shop-row-{capturedId}");

            var shopIcon = MakeUnitIcon(unit);
            if (shopIcon is not null) row.AddChild(shopIcon);

            var name = new Label
            {
                Text = $"{JobName(unit.Job)} Lv{unit.Level} (Age {unit.Age}) "
                       + _chronicleGlobal.ResolveDisplayName(unit),
            };
            name.SetMeta(TestIdMetaKey, $"shop-unit-name-{capturedId}");
            row.AddChild(name);

            var equip = unit.MainEquipment;
            var equipLabel = new Label
            {
                Text = equip is null
                    ? "装備: —"
                    : $"装備: {ItemName(equip.ItemId)} Lv{equip.Level}",
            };
            equipLabel.SetMeta(TestIdMetaKey, $"shop-equip-label-{capturedId}");
            row.AddChild(equipLabel);

            if (equip is null)
            {
                RenderShopBuyControls(row, capturedId, economy);
            }
            else
            {
                RenderShopUpgradeControls(row, capturedId, equip, economy);
            }

            _shopListContainer.AddChild(row);
        }
    }

    /// <summary>
    /// 装備なしユニット行へ「購入」操作を組み立てる。武装状態（<see cref="_pendingBuyId"/>）なら
    /// 5 大マスターの購入ボタン群＋[やめる]を、未武装なら [購入] 1 個を出す（残高不足は Disabled）。
    /// </summary>
    private void RenderShopBuyControls(HBoxContainer row, Guid unitId, PointsEconomy economy)
    {
        var canAfford = economy.CanAfford(ShopService.BuyCost);

        if (_pendingBuyId == unitId)
        {
            // 武装状態: 5 大マスターの各購入ボタン（押下＝即実行）＋[やめる]。
            foreach (var item in Enum.GetValues<ItemId>())
            {
                var capturedItem = item;
                var itemBtn = new Button { Text = ItemName(item), Disabled = !canAfford };
                itemBtn.SetMeta(TestIdMetaKey, $"shop-buy-item-button-{unitId}-{item}");
                itemBtn.Pressed += () => OnBuyItemPressed(unitId, capturedItem);
                row.AddChild(itemBtn);
            }

            var cancelBtn = new Button { Text = "やめる" };
            cancelBtn.SetMeta(TestIdMetaKey, $"shop-buy-cancel-button-{unitId}");
            cancelBtn.Pressed += OnShopCancelPressed;
            row.AddChild(cancelBtn);
        }
        else
        {
            var buyBtn = new Button
            {
                Text = $"購入 ({ShopService.BuyCost} pt)",
                Disabled = !canAfford,
            };
            buyBtn.SetMeta(TestIdMetaKey, $"shop-buy-button-{unitId}");
            buyBtn.Pressed += () => OnBuyArmPressed(unitId);
            row.AddChild(buyBtn);
        }
    }

    /// <summary>
    /// 装備ありユニット行へ「強化」操作を組み立てる。上限 (Lv5) は強化不可ラベルのみ。
    /// 未上限は武装状態（<see cref="_pendingUpgradeId"/>）で [強化する]／[やめる]、未武装で [強化 (cost pt)]。
    /// </summary>
    private void RenderShopUpgradeControls(
        HBoxContainer row, Guid unitId, Equipment equip, PointsEconomy economy)
    {
        if (equip.IsAtMaxLevel)
        {
            var maxLabel = new Label { Text = "★ Lv5 最大（強化済）" };
            maxLabel.SetMeta(TestIdMetaKey, $"shop-upgrade-max-label-{unitId}");
            row.AddChild(maxLabel);
            return;
        }

        var cost = ShopService.UpgradeCostFor(equip.Level);
        var canAfford = economy.CanAfford(cost);

        if (_pendingUpgradeId == unitId)
        {
            var confirmLabel = new Label { Text = $"⚠ Lv{equip.Level}→{equip.Level + 1} に強化しますか？ ({cost} pt)" };
            confirmLabel.SetMeta(TestIdMetaKey, $"shop-upgrade-confirm-label-{unitId}");
            row.AddChild(confirmLabel);

            var confirmBtn = new Button { Text = "強化する", Disabled = !canAfford };
            confirmBtn.SetMeta(TestIdMetaKey, $"shop-upgrade-confirm-button-{unitId}");
            confirmBtn.Pressed += () => OnUpgradeConfirmPressed(unitId);
            row.AddChild(confirmBtn);

            var cancelBtn = new Button { Text = "やめる" };
            cancelBtn.SetMeta(TestIdMetaKey, $"shop-upgrade-cancel-button-{unitId}");
            cancelBtn.Pressed += OnShopCancelPressed;
            row.AddChild(cancelBtn);
        }
        else
        {
            var upgradeBtn = new Button
            {
                Text = $"強化 ({cost} pt)",
                Disabled = !canAfford,
            };
            upgradeBtn.SetMeta(TestIdMetaKey, $"shop-upgrade-button-{unitId}");
            upgradeBtn.Pressed += () => OnUpgradeArmPressed(unitId);
            row.AddChild(upgradeBtn);
        }
    }

    // ─── 持ち物（装備の付け替え）の描画 ───────────────────────────────────

    /// <summary>
    /// 持ち物セクションを、現在の生存者と旅団の持ち物から無状態に再構築する。
    /// 上段「装備中（外す）」: 装備持ちの生存者を [外す] で持ち物へ戻す。
    /// 下段「持ち物」: 各装備を [装備する] → 装備先ユニットを選んで装着（2 段階）。
    /// すべて無償・非破壊（外した装備は消えず持ち物へ）。SoT をキャッシュせず毎回読み直す。
    /// </summary>
    private void RenderInventory()
    {
        if (_chronicleGlobal is null || _inventoryListContainer is null) return;

        foreach (var c in _inventoryListContainer.GetChildren()) c.QueueFree();

        var alive = _chronicleGlobal.GetAliveUnits();
        var inventory = _chronicleGlobal.BrigadeInventory;

        // ── 上段: 装備中の生存者（外して持ち物へ） ──
        var equippedHeader = new Label { Text = "装備中（外して持ち物へ）:" };
        equippedHeader.SetMeta(TestIdMetaKey, "inventory-equipped-header");
        _inventoryListContainer.AddChild(equippedHeader);

        var anyEquipped = false;
        foreach (var unit in alive)
        {
            if (unit.MainEquipment is not { } equip) continue;
            anyEquipped = true;
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SetMeta(TestIdMetaKey, $"inventory-equipped-row-{capturedId}");

            var icon = MakeUnitIcon(unit);
            if (icon is not null) row.AddChild(icon);

            var label = new Label
            {
                Text = $"{JobName(unit.Job)} {_chronicleGlobal.ResolveDisplayName(unit)} — {EquipmentSummary(equip)}",
            };
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);

            var unequipBtn = new Button { Text = "外す → 持ち物" };
            unequipBtn.SetMeta(TestIdMetaKey, $"inventory-unequip-button-{capturedId}");
            unequipBtn.Pressed += () => OnUnequipToInventoryPressed(capturedId);
            row.AddChild(unequipBtn);

            _inventoryListContainer.AddChild(row);
        }
        if (!anyEquipped)
        {
            var none = new Label { Text = "（装備中の隊員はいません）" };
            none.SetMeta(TestIdMetaKey, "inventory-equipped-empty");
            _inventoryListContainer.AddChild(none);
        }

        // ── 下段: 持ち物（装備先を選んで装着） ──
        var stockHeader = new Label { Text = "持ち物（選んで装備）:" };
        stockHeader.SetMeta(TestIdMetaKey, "inventory-stock-header");
        _inventoryListContainer.AddChild(stockHeader);

        if (inventory.Count == 0)
        {
            var empty = new Label { Text = "（持ち物は空。ドロップや取り外しで貯まります）" };
            empty.SetMeta(TestIdMetaKey, "inventory-stock-empty");
            _inventoryListContainer.AddChild(empty);
            return;
        }

        foreach (var item in inventory)
        {
            var capturedItemId = item.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SetMeta(TestIdMetaKey, $"inventory-stock-row-{capturedItemId}");

            var label = new Label { Text = EquipmentSummary(item) };
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);

            if (_pendingEquipItemId == capturedItemId)
            {
                // 装備先選択状態: プルダウン（ジョブアイコン付き）で 1 名を選び [装備] で確定。
                // 横並びボタンだと 9 名で端が見切れたため OptionButton 化（端のユニットも選べる）。
                var targetSelect = new OptionButton();
                targetSelect.SetMeta(TestIdMetaKey, $"inventory-equip-target-select-{capturedItemId}");
                targetSelect.AddThemeConstantOverride("icon_max_width", UnitIconSize);
                var targetIds = new List<Guid>();
                foreach (var unit in alive)
                {
                    var optLabel = $"{JobName(unit.Job)} {_chronicleGlobal.ResolveDisplayName(unit)}";
                    var icon = MakeUnitIconThumbnail(unit); // 両辺を抑えたサムネイル
                    if (icon is not null) targetSelect.AddIconItem(icon, optLabel);
                    else targetSelect.AddItem(optLabel);
                    targetIds.Add(unit.Id);
                }
                row.AddChild(targetSelect);

                var confirm = new Button { Text = "装備" };
                confirm.SetMeta(TestIdMetaKey, $"inventory-equip-confirm-{capturedItemId}");
                confirm.Pressed += () =>
                {
                    var sel = targetSelect.Selected;
                    if (sel >= 0 && sel < targetIds.Count)
                    {
                        OnEquipFromInventoryPressed(capturedItemId, targetIds[sel]);
                    }
                };
                row.AddChild(confirm);

                var cancel = new Button { Text = "やめる" };
                cancel.SetMeta(TestIdMetaKey, $"inventory-equip-cancel-{capturedItemId}");
                cancel.Pressed += OnEquipFromInventoryCancelPressed;
                row.AddChild(cancel);
            }
            else
            {
                var equipBtn = new Button { Text = "装備する", Disabled = alive.Count == 0 };
                equipBtn.SetMeta(TestIdMetaKey, $"inventory-equip-button-{capturedItemId}");
                equipBtn.Pressed += () => OnEquipArmPressed(capturedItemId);
                row.AddChild(equipBtn);
            }

            _inventoryListContainer.AddChild(row);
        }
    }

    /// <summary>
    /// ユニットのジョブ立ち絵を小さなアイコン（TextureRect）として作る。全ユニットは
    /// ジョブ×性別の 16 アセットのいずれかへ対応するため必ず引ける（資産欠落時のみ null）。
    /// </summary>
    private static TextureRect? MakeUnitIcon(Unit unit)
    {
        var tex = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (tex is null) return null;
        return new TextureRect
        {
            Texture           = tex,
            CustomMinimumSize  = new Vector2(UnitIconSize, UnitIconSize),
            StretchMode        = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode         = TextureRect.ExpandModeEnum.IgnoreSize,
        };
    }

    /// <summary>
    /// ユニットリスト行用の大きめ立ち絵アイコン（<see cref="UnitListIconSize"/> 角）。見やすさ優先で
    /// 共有の小アイコンより大きくする。資産欠落時のみ null。
    /// </summary>
    private static TextureRect? MakeUnitListIcon(Unit unit)
    {
        var tex = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (tex is null) return null;
        return new TextureRect
        {
            Texture           = tex,
            CustomMinimumSize  = new Vector2(UnitListIconSize, UnitListIconSize),
            StretchMode        = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode         = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsVertical  = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>(job, gender) → 縮小済みサムネイルのキャッシュ（再描画ごとの再生成を避ける）。</summary>
    private static readonly Dictionary<(int Job, int Gender), Texture2D> _iconThumbnailCache = new();

    /// <summary>
    /// OptionButton 項目アイコン用に、ジョブ立ち絵を UnitIconSize 角の枠へ収めた
    /// サムネイル（ImageTexture）を返す。OptionButton は <c>icon_max_width</c> で幅しか
    /// 制限できず、縦長の立ち絵は高さが膨らむため、CPU 側で両辺を縮めて高さも抑える
    /// （リスト側の TextureRect と同等サイズに揃える）。(job, gender) でキャッシュする。
    /// </summary>
    private static Texture2D? MakeUnitIconThumbnail(Unit unit)
    {
        var key = ((int)unit.Job, (int)unit.Gender);
        if (_iconThumbnailCache.TryGetValue(key, out var cached)) return cached;

        var tex = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (tex is null) return null;

        var image = tex.GetImage();
        if (image is null) return tex; // 画像が取得できない環境では原寸へフォールバック

        var srcW = image.GetWidth();
        var srcH = image.GetHeight();
        if (srcW <= 0 || srcH <= 0) return tex;

        // 両辺が UnitIconSize 以下に収まる倍率（アスペクト比保持）。
        var scale = Mathf.Min((float)UnitIconSize / srcW, (float)UnitIconSize / srcH);
        var dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        var dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

        image.Resize(dstW, dstH, Image.Interpolation.Bilinear);
        var thumb = ImageTexture.CreateFromImage(image);
        _iconThumbnailCache[key] = thumb;
        return thumb;
    }

    /// <summary>装備 1 個の表示文（種別名 Lv ＋ Affix 名の連結）。コード側に日本語は持たない。</summary>
    private string EquipmentSummary(Equipment equip)
    {
        var text = $"{ItemName(equip.ItemId)} Lv{equip.Level}";
        if (equip.HasAnyAffix)
        {
            var affixes = new List<string>();
            foreach (var key in equip.AffixKeys)
            {
                affixes.Add(_chronicleGlobal?.ResolveAffixName(key) ?? key);
            }
            text += $" 〈{string.Join(" / ", affixes)}〉";
        }
        return text;
    }

    private void OnUnequipToInventoryPressed(Guid unitId)
    {
        // 取り外しは InventoryChanged / RosterChanged を発火 → 自動で持ち物・商店が再描画される。
        _chronicleGlobal?.UnequipToInventory(unitId);
    }

    private void OnEquipArmPressed(Guid equipmentId)
    {
        _pendingEquipItemId = (_pendingEquipItemId == equipmentId) ? null : equipmentId;
        RenderInventory();
    }

    private void OnEquipFromInventoryPressed(Guid equipmentId, Guid unitId)
    {
        _pendingEquipItemId = null;
        // 装着は InventoryChanged / RosterChanged を発火 → 自動再描画。失敗時は手動で持ち物を戻す。
        if (_chronicleGlobal?.EquipFromInventory(unitId, equipmentId) is null)
        {
            RenderInventory();
        }
    }

    private void OnEquipFromInventoryCancelPressed()
    {
        _pendingEquipItemId = null;
        RenderInventory();
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

    /// <summary>
    /// スカウト候補行の ［スカウト］ 押下ハンドラ。ChronicleGlobal.RecruitScoutCandidate が
    /// 残高検証・消費・ロスタ追加・候補除去・シグナル発火を一括で行う。失敗時は null。
    /// </summary>
    private void OnScoutCandidatePressed(Guid candidateId)
    {
        if (_chronicleGlobal is null) return;

        var recruited = _chronicleGlobal.RecruitScoutCandidate(candidateId);
        if (recruited is null)
        {
            GD.Print($"[MarriageUI] 💸 スカウト失敗: 残高不足／候補不在 Id={candidateId}");
            return;
        }

        GD.Print(
            $"[MarriageUI] ⚔ スカウト成立 / {JobName(recruited.Job)} (Age {recruited.Age}) Id={recruited.Id}");
        // 残高・各リストの再描画は EconomyChanged/RosterChanged/ScoutCandidatesChanged シグナル経由で自動。
    }

    /// <summary>
    /// ユニットリスト行の ［詳細］ 押下ハンドラ。当該ユニットの詳細オーバーレイのマウント意思
    /// （<see cref="UnitInspectRequested"/>）を発火する（オーバーレイの生死は GameDirector が握る）。
    /// </summary>
    private void OnUnitDetailsPressed(Guid unitId) => UnitInspectRequested?.Invoke(unitId);

    private void OnEnlistChildPressed(Guid childId)
    {
        // 子は既に BattalionRoster に居る（marriage で追加済み）。
        // 本ボタンは「家系の長が正式に大隊員として認める儀式」を示し、
        // UI 側で「儀式済み」状態を管理することで以降のリスト表示から除外する。
        _ceremoniallyEnlisted.Add(childId);
        GD.Print($"[MarriageUI] 🎓 0 pt で正式入団: {childId}");
        RenderChildrenLists();
    }

    // ─── 旅団兵器廠（商店・強化）ハンドラ ─────────────────────────────────

    /// <summary>
    /// [購入] 押下: 当該行を購入の武装状態にして 5 大マスターの選択ボタンを展開する。
    /// 強化の武装とは相互排他（同時に複数行が開かないよう片方を畳む）。実際の購入はまだ起きない。
    /// </summary>
    private void OnBuyArmPressed(Guid unitId)
    {
        _pendingBuyId = unitId;
        _pendingUpgradeId = null; // 相互排他: 強化の武装は解除。
        RenderShop();
    }

    /// <summary>
    /// [強化] 押下: 当該行を強化の武装状態（[強化する]／[やめる]）にする。
    /// 購入の武装とは相互排他。実際の強化はまだ起きない（次の [強化する] で確定）。
    /// </summary>
    private void OnUpgradeArmPressed(Guid unitId)
    {
        _pendingUpgradeId = unitId;
        _pendingBuyId = null; // 相互排他: 購入の武装は解除。
        RenderShop();
    }

    /// <summary>[やめる] 押下: 購入・強化の武装をともに解除して通常表示へ戻す（何も買わない・強化しない）。</summary>
    private void OnShopCancelPressed()
    {
        _pendingBuyId = null;
        _pendingUpgradeId = null;
        RenderShop();
    }

    /// <summary>
    /// 装備購入ボタン（5 大マスターのいずれか）押下: ChronicleGlobal.ExecuteBuyEquipment で
    /// ポイント消費・新装備装着・旧装備ロストを一括実行する。成功時は Economy/RosterChanged
    /// シグナル経由で自動再描画されるため、ここでは武装解除だけ行う。失敗時は手動で畳む。
    /// </summary>
    private void OnBuyItemPressed(Guid unitId, ItemId itemId)
    {
        if (_chronicleGlobal is null) return;

        // 武装は結果に関わらず解除（成功なら行が組み変わり、失敗なら通常表示に戻る）。
        _pendingBuyId = null;

        var result = _chronicleGlobal.ExecuteBuyEquipment(unitId, itemId);
        if (result is null)
        {
            GD.Print($"[MarriageUI] 🛡️ 装備購入 失敗: 残高不足／対象不在 Id={unitId} Item={itemId}");
            RenderShop(); // 失敗時はシグナル無し → 手動で武装解除を反映
            return;
        }

        var lostText = result.ReplacedEquipment is null
            ? ""
            : $" / 旧装備 {ItemName(result.ReplacedEquipment.ItemId)} Lv{result.ReplacedEquipment.Level} をロスト";
        GD.Print(
            $"[MarriageUI] 🛡️ 装備購入 成立 ({ShopService.BuyCost} pt) / " +
            $"{ItemName(result.PurchasedEquipment.ItemId)} Lv{result.PurchasedEquipment.Level} を装備{lostText}");
        // 残高・各リストの再描画はシグナル経由で自動。
    }

    /// <summary>
    /// [強化する] 押下: ChronicleGlobal.ExecuteUpgradeEquipment で現装備を 1 段階上げる。
    /// コストは SoT（ShopService.UpgradeCostFor）に従い SoT 側が現レベルから算出する。
    /// 成功時はシグナル経由で自動再描画。失敗（残高不足・上限・対象不在）時は手動で畳む。
    /// </summary>
    private void OnUpgradeConfirmPressed(Guid unitId)
    {
        if (_chronicleGlobal is null) return;

        // 武装は結果に関わらず解除。
        _pendingUpgradeId = null;

        var result = _chronicleGlobal.ExecuteUpgradeEquipment(unitId);
        if (result is null)
        {
            GD.Print($"[MarriageUI] 🛡️ 装備強化 失敗: 残高不足／上限／対象不在 Id={unitId}");
            RenderShop(); // 失敗時はシグナル無し → 手動で武装解除を反映
            return;
        }

        GD.Print(
            $"[MarriageUI] 🛡️ 装備強化 成立 / " +
            $"{ItemName(result.UpgradedEquipment.ItemId)} → Lv{result.UpgradedEquipment.Level}");
        // 残高・各リストの再描画はシグナル経由で自動。
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
        var sex = unit.Gender == Gender.Male ? "♂" : "♀";
        return $"{sex} {JobName(unit.Job)} Lv{unit.Level} (Age {unit.Age}) / 装備: {equip}";
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
