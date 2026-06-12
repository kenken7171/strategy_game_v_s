// =============================================================================
//  ChronicleKnights — GameDirector.cs
// -----------------------------------------------------------------------------
//  メイン制御ディレクター（ゲームのプレイループを一本の線に繋ぐ司令塔）。
//
//  常駐ノード ChronicleGlobal の PhaseChanged シグナルを購読し、現在の GamePhase
//  に応じて 4 つのフェーズ画面（年代記 / 旅団組合 / 大隊編成 / 戦闘解決）の
//  表示・非表示を統括する Control ノード。画面の引き当ては GamePhase.Slug() が
//  払い出す ASCII スラッグをキーに、シーンツリーから安全に解決する
//  （各画面ノードの Name をスラッグに設定し、GetNodeOrNull で取得する）。
//
//  画面構成:
//    ┌──────────────────────────────────────────────────────┐
//    │ 🧭 現在: 年代記フェーズ            [ ▶ 次へ：旅団組合 ] │ ← 常設ヘッダー
//    ├──────────────────────────────────────────────────────┤
//    │                                                      │
//    │   （現在フェーズに対応する画面だけが Visible=true）  │ ← 画面コンテナ
//    │                                                      │
//    └──────────────────────────────────────────────────────┘
//
//  ★ フェーズ名のローカライズ:
//    ヘッダーのインジケータは ChronicleGlobal.ResolveCurrentPhaseName() で解決した
//    日本語フェーズ名を表示する。フェーズ遷移のたびに自動更新される。
//    解決ロジックは純粋層 PhaseNameResolver（Core/Localization）が担い、本クラスは
//    res:// の localization 読込（ChronicleGlobal 経由）と描画だけを担当する。
//
//  ★ フェーズの前進:
//    - 年代記フェーズは「予言を選ぶ」こと自体が前進の契機（ChronicleGlobal が
//      自動で旅団組合へ進める）。よってヘッダーの『次へ』ボタンは年代記では隠す。
//    - 旅団組合 / 大隊編成 / 戦闘解決では『次へ』ボタンで AdvancePhase を呼び、
//      組合→編成→戦闘→（次世代の）年代記 と循環させる。
//
//  クリーン設計:
//    - 略称（正式名称のみを使う方針）は本ファイルでも完全未使用
//    - 遷移可否の判断は持たず ChronicleGlobal / GamePhaseFlow に委ねる
//    - メモリリーク防止: _ExitTree で全シグナルを購読解除
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Bootstrap;
using ChronicleKnights.Core.GameFlow;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 現在の GamePhase に応じて画面を切り替えるメイン制御ディレクター。
/// </summary>
public partial class GameDirector : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private Label? _phaseIndicatorLabel;
    private Button? _advanceButton;
    private Control? _screenContainer;

    /// <summary>
    /// 起動直後に最前面へ overlay する無状態タイトルゲート。IsInitialized == false の
    /// 間だけ生存し、新規/継続のいずれかで世界が初期化（StateInitialized）された瞬間に
    /// QueueFree して静かに退場する（自己崩壊型ライフサイクル）。未展開時は null。
    /// </summary>
    private TitleScreen? _titleScreen;

    /// <summary>
    /// 婚姻・家系図運営画面（拠点B）への参照。家系図ビューアの「家系図を開く」意思表示
    /// （<see cref="MarriageUI.PedigreeRequested"/>）を購読し、本ディレクターが
    /// PedigreeOverlay を最前面へマウントするために BuildScreens で 1 度だけ捕捉する。
    /// </summary>
    private MarriageUI? _marriageScreen;

    /// <summary>
    /// 現在前面に展開中の家系図オーバーレイ。閉じる意思表示（CloseRequested）または退場時に
    /// QueueFree して静かに退場する（タイトルゲートと同型の自己崩壊型ライフサイクル）。未展開時は null。
    /// </summary>
    private PedigreeOverlay? _pedigreeOverlay;

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        // localization を読み込み、名前・フェーズ名リゾルバを準備する
        // （失敗しても false が返るだけで、生スラッグへフォールバックして継続）。
        _chronicleGlobal?.LoadLocalization();

        BuildLayout();
        BuildScreens();
        SubscribeSignals();

        // ★ 起動エントリの単一窓口（タイトルゲート方式）:
        //   かつてはここで問答無用に新規ゲームを初期化していたが、プレイヤーが最初に
        //   「新規 / つづきから」を選ぶ門が無かった。現在は世界が未初期化の間だけ
        //   TitleScreen を最前面へ overlay し、その意思表示イベントを受けて初めて
        //   Initialize / LoadGame という SoT トリガーを引く（OnNewChronicleRequested /
        //   OnContinueChronicleRequested）。これにより全画面ノードへ初回シグナルが確実に
        //   届くタイミング（AddChild 済み）で初期化が走り、最初の旅団員と予言が描画される。
        if (_chronicleGlobal is { IsInitialized: true })
        {
            // 既に世界が在る（ホットリロード・セーブ継続後の再アタッチ等）→ 現在フェーズを描画。
            RenderCurrentPhase();
        }
        else
        {
            // まっさらな起動 → タイトルゲートを最前面へ展開（唯一の起動契機）。
            MountTitleScreen();
        }
    }

    public override void _ExitTree()
    {
        // タイトルゲート・家系図オーバーレイの購読を先に解いて確実に解放
        // （ゾンビノード・購読二重接続・Tween リークの根絶）。
        DismissTitleScreen();
        DismissPedigreeOverlay();

        // 家系図ビューアの「開く」意思表示の購読も解除（婚姻画面ノードの破棄に先んじて）。
        if (_marriageScreen is not null && GodotObject.IsInstanceValid(_marriageScreen))
        {
            try
            {
                _marriageScreen.PedigreeRequested -= OnPedigreeRequested;
            }
            catch
            {
                // ノードが既に破棄されている場合の安全網（メモリリーク防止）
            }
        }
        _marriageScreen = null;

        UnsubscribeSignals();
    }

    // ─── タイトルゲート（起動エントリの単一窓口） ─────────────────────────
    //  ⚠ 最重要: 常駐ノード ChronicleGlobal は生成直後 IsInitialized == false の
    //  「無（未初期化）」状態で待機している。Initialize / LoadGame がどこからも呼ばれ
    //  なければ、ロスターも予言も空のまま何も描画されない。タイトルゲートのボタンが
    //  その唯一の起動契機であり、本ディレクターがその意思表示を受けて SoT を初期化する。

    /// <summary>
    /// 無状態タイトルゲート（TitleScreen）を最前面へ overlay する。多重展開・前回ゲートの
    /// 取り残しを避けるため、生存中の旧ゲートがあれば先に確実に解放してから展開する。
    ///
    /// 設計:
    ///   - 「つづきから」を活性化してよいかは、ここで ChronicleGlobal.HasSaveData() を
    ///     一度だけ問い合わせ、AddChild 前に ContinueAvailable へ注入する（TitleScreen は
    ///     SoT を自分で触らない無状態の徹底）。
    ///   - 2 つの意思表示イベントを購読し、押下時に初めて Initialize / LoadGame を引く。
    ///   - root（VBox）より後に AddChild するため、本ゲートは全 UI の最前面に描かれ、
    ///     かつ MouseFilter=Stop で背後（ヘッダ・各フェーズ画面）への入力を遮断する。
    /// </summary>
    private void MountTitleScreen()
    {
        // 旧ゲートが取り残されていれば購読を解いて解放（多重展開・リーク防止）。
        DismissTitleScreen();

        var title = new TitleScreen
        {
            ContinueAvailable = _chronicleGlobal?.HasSaveData() ?? false,
        };
        title.NewChronicleRequested      += OnNewChronicleRequested;
        title.ContinueChronicleRequested += OnContinueChronicleRequested;
        title.SetMeta(TestIdMetaKey, "game-director-title-screen");
        _titleScreen = title;

        AddChild(title); // root の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中のタイトルゲートがあれば購読を解いて確実に解放する。世界が初期化された
    /// 瞬間（OnStateInitialized）および退場時（_ExitTree）に呼び、ゾンビノード・購読の
    /// 二重接続・Tween リークを根絶する。TitleScreen 側は _ExitTree で篝火 Tween を自ら
    /// Kill するため、ここでの QueueFree だけで演出ノードも綺麗にお掃除される。
    /// </summary>
    private void DismissTitleScreen()
    {
        if (_titleScreen is null) return;

        if (GodotObject.IsInstanceValid(_titleScreen))
        {
            _titleScreen.NewChronicleRequested      -= OnNewChronicleRequested;
            _titleScreen.ContinueChronicleRequested -= OnContinueChronicleRequested;
            _titleScreen.QueueFree();
        }
        _titleScreen = null;
    }

    /// <summary>
    /// タイトルゲート「新たな年代記を始める」の意思表示ハンドラ。新規ゲームの初期状態
    /// （初期資金・初期ロスター・ターン 1 の予言 3 択）を生成して Initialize へ注入する。
    ///
    /// 設計:
    ///   - 初期ロスターと初期資金の構築は Godot 非依存の純粋ファクトリ
    ///     NewGameFactory（Core/Bootstrap）へ委譲する（脳と身体の分離・テスト容易性）。
    ///   - タイムライン（ターン 1 の予言 3 つ）は initialTimeline=null で渡し、
    ///     Initialize 内で同じ Random から生成させる（乱数列を 1 本に統一）。
    ///   - Initialize は SafeEmit(StateInitialized) を同期発火する。その購読ハンドラ
    ///     OnStateInitialized が、本ゲートを QueueFree して退場させる（後始末は一元化）。
    /// </summary>
    private void OnNewChronicleRequested()
    {
        if (_chronicleGlobal is null) return;

        var rng = new Random();
        var seed = NewGameFactory.Create(rng);

        _chronicleGlobal.Initialize(
            initialRoster:   seed.Roster,
            initialEconomy:  seed.Economy,
            initialTimeline: null,   // ターン 1 の予言は Initialize が同じ rng で生成する
            rng:             rng);
    }

    /// <summary>
    /// タイトルゲート「つづきから」の意思表示ハンドラ。既定パスのセーブを読み込み、
    /// 4 状態（経済・タイムライン・ロスター・旅団史）を一括復元する。
    ///
    /// 安全性:
    ///   - 「つづきから」は HasSaveData() == true のときしか活性化しないため、通常ここに
    ///     到達した時点でセーブは存在する。それでも LoadGame は false（ファイル消失・破損）を
    ///     返し得るが、その場合は既存状態を一切変えずに何もしない（ゲートは残り再選択可能）。
    ///   - 成功時は LoadGame が SafeEmit(StateInitialized) を同期発火し、OnStateInitialized が
    ///     本ゲートを退場させる（新規と継続でゲート後始末の経路を一本化）。
    /// </summary>
    private void OnContinueChronicleRequested()
    {
        _chronicleGlobal?.LoadGame();
    }

    // ─── 家系図オーバーレイ（血統の縦軸ビューア） ─────────────────────────
    //  拠点B の家系図ビューア各行の「家系図を開く」押下を受け、指定ユニットを根とする
    //  無状態オーバーレイ PedigreeOverlay を最前面へ overlay する。タイトルゲートと同じ
    //  自己崩壊型ライフサイクル（CloseRequested / _ExitTree で QueueFree）で後始末を一元化する。

    /// <summary>
    /// 家系図ビューアの「家系図を開く」意思表示ハンドラ。指定ユニットを根に
    /// PedigreeOverlay を最前面へマウントする（婚姻画面が発火する唯一の窓口）。
    /// </summary>
    private void OnPedigreeRequested(Guid targetUnitId) => MountPedigreeOverlay(targetUnitId);

    /// <summary>家系図オーバーレイの「閉じる」意思表示ハンドラ。前面のオーバーレイを解放する。</summary>
    private void OnPedigreeCloseRequested() => DismissPedigreeOverlay();

    /// <summary>
    /// 指定ユニット（<paramref name="targetUnitId"/>）を根とする家系図オーバーレイを最前面へ
    /// 展開する。多重展開・前回オーバーレイの取り残しを避けるため、生存中の旧オーバーレイが
    /// あれば先に確実に解放してから展開する。TargetUnitId は AddChild 前に注入する
    /// （オーバーレイは無状態で、SoT 手繰りと描画を自身の _Ready で行う）。
    /// </summary>
    private void MountPedigreeOverlay(Guid targetUnitId)
    {
        DismissPedigreeOverlay();

        var overlay = new PedigreeOverlay { TargetUnitId = targetUnitId };
        overlay.CloseRequested += OnPedigreeCloseRequested;
        overlay.SetMeta(TestIdMetaKey, "game-director-pedigree-overlay");
        _pedigreeOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中の家系図オーバーレイがあれば購読を解いて確実に解放する。閉じる意思表示
    /// （OnPedigreeCloseRequested）および退場時（_ExitTree）に呼び、ゾンビノード・購読の
    /// 二重接続・Tween リークを根絶する。オーバーレイ側は _ExitTree で自前の台帳（カード・
    /// コネクタ・開通 Tween）を更地化するため、ここでの QueueFree だけで演出ノードも綺麗に掃除される。
    /// </summary>
    private void DismissPedigreeOverlay()
    {
        if (_pedigreeOverlay is null) return;

        if (GodotObject.IsInstanceValid(_pedigreeOverlay))
        {
            _pedigreeOverlay.CloseRequested -= OnPedigreeCloseRequested;
            _pedigreeOverlay.QueueFree();
        }
        _pedigreeOverlay = null;
    }

    // ─── レイアウト構築（ヘッダー + 画面コンテナ） ─────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var root = new VBoxContainer { Name = "DirectorRoot" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        root.SetMeta(TestIdMetaKey, "game-director-root");
        AddChild(root);

        // ── 常設ヘッダー：フェーズインジケータ + 次へボタン ──────────
        var header = new HBoxContainer { Name = "DirectorHeader" };
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "game-director-header");
        root.AddChild(header);

        _phaseIndicatorLabel = new Label { Name = "PhaseIndicator" };
        _phaseIndicatorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _phaseIndicatorLabel.SetMeta(TestIdMetaKey, "game-director-phase-indicator");
        header.AddChild(_phaseIndicatorLabel);

        _advanceButton = new Button { Name = "AdvancePhaseButton" };
        _advanceButton.Pressed += OnAdvancePressed;
        _advanceButton.SetMeta(TestIdMetaKey, "game-director-advance-button");
        header.AddChild(_advanceButton);

        // ── 画面コンテナ：4 フェーズ画面をぶら下げ、1 つだけ Visible にする ──
        _screenContainer = new Control { Name = "ScreenContainer" };
        _screenContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _screenContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _screenContainer.SetMeta(TestIdMetaKey, "game-director-screen-container");
        root.AddChild(_screenContainer);
    }

    // ─── 画面生成（フェーズごとに 1 つ、Name をスラッグに設定） ────────────

    private void BuildScreens()
    {
        if (_screenContainer is null) return;

        foreach (var phase in GamePhaseFlow.Cycle)
        {
            var screen = CreateScreenFor(phase);

            // 拠点B（婚姻・家系図）画面なら、家系図ビューアの「開く」意思表示を購読する。
            // オーバーレイの生死は本ディレクターが一手に握る（画面側は無状態のまま）。
            if (screen is MarriageUI marriageScreen)
            {
                _marriageScreen = marriageScreen;
                marriageScreen.PedigreeRequested += OnPedigreeRequested;
            }

            // スラッグを Node 名にして、後段で GetNodeOrNull により安全に引き当てる。
            screen.Name = phase.Slug();
            // testid もスラッグ込みで付与（E2E がフェーズ画面を一意に掴めるようにする）。
            screen.SetMeta(TestIdMetaKey, $"game-director-screen-{phase.Slug()}");
            screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            screen.Visible = false; // 初期は全て非表示。RenderCurrentPhase で 1 つだけ表示。
            _screenContainer.AddChild(screen);
        }
    }

    /// <summary>
    /// フェーズに対応する画面ノードを生成する。各フェーズは専用 Control を持つ。
    /// </summary>
    private static Godot.Control CreateScreenFor(GamePhase phase) => phase switch
    {
        GamePhase.Chronicle => new TimelineUI(),     // 拠点A: 予言・歴史進行
        GamePhase.Guild     => new MarriageUI(),      // 拠点B: 婚姻・スカウト
        GamePhase.Formation => new FormationUI(),     // 大隊編成
        GamePhase.Battle    => new BattleUI(),        // 戦闘: ターン制戦闘 → とどめ → 決算（三段）
        _ => new Godot.Control(),                     // 未知フェーズの安全網（空画面）
    };

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.PhaseChanged     += OnPhaseChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.PhaseChanged     -= OnPhaseChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnPhaseChanged() => RenderCurrentPhase();

    /// <summary>
    /// 世界が初期化された（新規 Initialize / セーブ LoadGame のいずれか）瞬間のハンドラ。
    /// まずタイトルゲートを退場（QueueFree）させ、その後に現在フェーズ（= 拠点・年代記）を
    /// 描画する。新規・継続のどちらの経路でも本ハンドラがゲート後始末の単一窓口となる。
    /// </summary>
    private void OnStateInitialized()
    {
        DismissTitleScreen();
        RenderCurrentPhase();
    }

    // ─── 描画（フェーズに応じた画面切り替え + インジケータ更新） ──────────

    private void RenderCurrentPhase()
    {
        if (_chronicleGlobal is null || _screenContainer is null) return;

        var current = _chronicleGlobal.CurrentPhase;

        // 1. 画面切り替え：スラッグをキーに各画面の Visible を設定する。
        foreach (var phase in GamePhaseFlow.Cycle)
        {
            var screen = _screenContainer.GetNodeOrNull<Godot.Control>(phase.Slug());
            if (screen is not null)
            {
                screen.Visible = phase == current;
            }
        }

        // 2. インジケータ更新：現在フェーズの日本語名を表示する。
        if (_phaseIndicatorLabel is not null)
        {
            _phaseIndicatorLabel.Text = $"🧭 現在: {_chronicleGlobal.ResolveCurrentPhaseName()}";
        }

        // 3. 次へボタン更新：次フェーズ名を出し、年代記では隠す（予言選択が前進契機）。
        if (_advanceButton is not null)
        {
            var nextPhase = GamePhaseFlow.Next(current);
            _advanceButton.Text = $"▶ 次へ：{_chronicleGlobal.ResolvePhaseName(nextPhase)}";
            _advanceButton.Visible = current != GamePhase.Chronicle;
        }
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    private void OnAdvancePressed()
    {
        // 前進の可否・順序は ChronicleGlobal（内部で GamePhaseFlow）に委ねる。
        // 戻り値の再描画は PhaseChanged シグナル経由で自動的に行われる。
        _chronicleGlobal?.AdvancePhase();
    }
}
