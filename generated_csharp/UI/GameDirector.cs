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
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Bootstrap;
using ChronicleKnights.Core.Chronicle;        // EpochForYear / EpochId (shared background)
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.UserInterface;          // BackgroundTextureLibrary
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

    /// <summary>
    /// コンテンツカードを画面端から離す余白（px）。全画面ではなく余白を残すことで、最背面の
    /// 共通背景がカードの周囲に額縁のように見える（最終的に「背景の上にカードを置く」見た目の土台）。
    /// </summary>
    private const int ContentCardMarginPx = 24;

    /// <summary>コンテンツカードの角丸半径（px）。</summary>
    private const int ContentCardCornerRadius = 14;

    /// <summary>コンテンツカードの内側パディング（px）。中身（ヘッダ＋画面）と縁の間。</summary>
    private const int ContentCardInnerPadding = 16;

    /// <summary>コンテンツカードの地色（半透明の暗色）。背景がうっすら透ける薄いカード。</summary>
    private static readonly Color ContentCardColor = new(0.06f, 0.07f, 0.10f, 0.74f);

    /// <summary>コンテンツカードの縁取り色（淡い枠線）。</summary>
    private static readonly Color ContentCardBorderColor = new(1.0f, 1.0f, 1.0f, 0.10f);

    /// <summary>
    /// 運命の帯（予言タイムライン）オーバーレイの既定先読みターン数。数ターン先の戦術判断
    /// （ローテーションの読み合い）に足る現実的な深さに留める。AttackIntentRoller 側でも上限クランプ。
    /// </summary>
    private const int DefaultProphecyLookaheadTurns = 4;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private Label? _phaseIndicatorLabel;
    private Button? _advanceButton;
    private Control? _screenContainer;

    /// <summary>
    /// 全フェーズ共通の戦場背景（章 Epoch ごとの画像）。root より背面に全画面で敷き、
    /// すべての画面（年代記/拠点/編成/戦闘）の背後に共通で表示する。年（暦）が変わって
    /// 章をまたいだら張り替える。未配置の章は Texture=null（従来どおり背景なし）。
    /// </summary>
    private TextureRect? _backgroundRect;

    /// <summary>
    /// ★ 動的B型ライフサイクルの心臓: 「今そこに生きている」唯一のフェーズ画面ノード。
    /// 起動時一斉生成（旧 BuildScreens）は廃止し、フェーズ遷移の瞬間に
    /// <see cref="MountScreenForCurrentPhase"/> が現在フェーズの画面だけを new してマウントし、
    /// <see cref="FreeCurrentScreen"/> が旧画面を QueueFree で完全消滅させる。常に最大 1 枚しか
    /// 存在しないため、隠れた画面がグローバル信号を裏で購読して副作用を起こす余地が無い。未展開時 null。
    /// </summary>
    private Godot.Control? _currentScreen;

    /// <summary>
    /// 起動直後に最前面へ overlay する無状態タイトルゲート。IsInitialized == false の
    /// 間だけ生存し、新規/継続のいずれかで世界が初期化（StateInitialized）された瞬間に
    /// QueueFree して静かに退場する（自己崩壊型ライフサイクル）。未展開時は null。
    /// </summary>
    private TitleScreen? _titleScreen;

    /// <summary>
    /// 現在前面に展開中の家系図オーバーレイ。閉じる意思表示（CloseRequested）または退場時に
    /// QueueFree して静かに退場する（タイトルゲートと同型の自己崩壊型ライフサイクル）。未展開時は null。
    /// </summary>
    private PedigreeOverlay? _pedigreeOverlay;

    /// <summary>
    /// 現在前面に展開中の予言タイムラインオーバーレイ。閉じる意思表示（CloseRequested）または退場時に
    /// QueueFree して静かに退場する（家系図オーバーレイと同型の自己崩壊型ライフサイクル）。未展開時は null。
    /// </summary>
    private ProphecyTimelineOverlay? _prophecyOverlay;

    /// <summary>
    /// 現在前面に展開中のジョブマニュアル（全ジョブ解説カタログ）オーバーレイ。CLOSE 押下
    /// （CloseRequested）または退場時に QueueFree する自己崩壊型ライフサイクル。未展開時は null。
    /// ジョブデータは静的なため SoT 購読は不要（オーバーレイ自身が JobCodex/JobMaster を読むだけ）。
    /// </summary>
    private JobManualOverlay? _jobManualOverlay;

    /// <summary>
    /// 現在前面に展開中のユニット詳細モーダル。CLOSE 押下（CloseRequested）または退場時に
    /// QueueFree する自己崩壊型ライフサイクル。未展開時は null。
    /// </summary>
    private UnitDetailOverlay? _unitDetailOverlay;

    /// <summary>
    /// 現在前面に展開中の休息報酬通知オーバーレイ。編成で「休息」を選んで前進したときに、
    /// 戦闘フェーズを完全にバイパスして即マウントする。「確認（次代へ）」押下（Confirmed）で
    /// 初めて AdvancePhase（年送り）を駆動し、自身を QueueFree する。未展開時は null。
    /// </summary>
    private RestResultOverlay? _restResultOverlay;

    /// <summary>
    /// 現在前面に展開中の 3 択ドロップオーバーレイ。EquipmentDrop 予言で候補が発生した
    /// （DropChoicePending）ときにマウントし、1 つ選んで取得（Chosen）すると自身を QueueFree する。
    /// </summary>
    private EquipmentDropOverlay? _equipmentDropOverlay;

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        // localization を読み込み、名前・フェーズ名リゾルバを準備する
        // （失敗しても false が返るだけで、生スラッグへフォールバックして継続）。
        _chronicleGlobal?.LoadLocalization();

        BuildLayout();
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
            // 既に世界が在る（ホットリロード・セーブ継続後の再アタッチ等）→ 現在フェーズ画面を動的生成。
            MountScreenForCurrentPhase();
            RenderHeader();
        }
        else
        {
            // まっさらな起動 → タイトルゲートを最前面へ展開（唯一の起動契機）。
            MountTitleScreen();
        }
    }

    public override void _ExitTree()
    {
        // タイトルゲート・家系図・予言オーバーレイの購読を先に解いて確実に解放
        // （ゾンビノード・購読二重接続・Tween リークの根絶）。
        DismissTitleScreen();
        DismissPedigreeOverlay();
        DismissProphecyOverlay();
        DismissJobManualOverlay();
        DismissUnitDetailOverlay();
        DismissRestResultOverlay();
        DismissEquipmentDropOverlay();

        // 現在生きているフェーズ画面（最大1枚）の従属イベント購読を解いて完全消滅させる。
        FreeCurrentScreen();

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

    // ─── 予言タイムラインオーバーレイ（未来の横軸ビューア） ────────────────
    //  拠点D の戦闘画面の「運命の帯を開く」押下を受け、N ターン先の敵の手筋を
    //  無状態オーバーレイ ProphecyTimelineOverlay として最前面へ overlay する。家系図
    //  オーバーレイと同じ自己崩壊型ライフサイクル（CloseRequested / _ExitTree で QueueFree）
    //  で後始末を一元化する。先読みターン数だけを注入し、予言データの手繰りと描画は
    //  オーバーレイ自身が _Ready / RenderAll で ChronicleGlobal から行う（画面側は無状態）。

    /// <summary>
    /// 戦闘画面の「運命の帯を開く」意思表示ハンドラ。予言タイムラインオーバーレイを
    /// 最前面へマウントする（戦闘画面が発火する唯一の窓口）。
    /// </summary>
    private void OnProphecyRequested() => MountProphecyOverlay();

    /// <summary>予言タイムラインオーバーレイの「閉じる」意思表示ハンドラ。前面のオーバーレイを解放する。</summary>
    private void OnProphecyCloseRequested() => DismissProphecyOverlay();

    /// <summary>
    /// 既定先読みターン数（<see cref="DefaultProphecyLookaheadTurns"/>）の予言タイムライン
    /// オーバーレイを最前面へ展開する。多重展開・前回オーバーレイの取り残しを避けるため、
    /// 生存中の旧オーバーレイがあれば先に確実に解放してから展開する。LookaheadTurns は
    /// AddChild 前に注入する（オーバーレイは無状態で、予言の手繰りと描画を自身の _Ready で行う）。
    /// </summary>
    private void MountProphecyOverlay()
    {
        DismissProphecyOverlay();

        var overlay = new ProphecyTimelineOverlay { LookaheadTurns = DefaultProphecyLookaheadTurns };
        overlay.CloseRequested += OnProphecyCloseRequested;
        overlay.SetMeta(TestIdMetaKey, "game-director-prophecy-overlay");
        _prophecyOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中の予言タイムラインオーバーレイがあれば購読を解いて確実に解放する。閉じる意思表示
    /// （OnProphecyCloseRequested）および退場時（_ExitTree）に呼び、ゾンビノード・購読の二重接続・
    /// Tween リークを根絶する。オーバーレイ側は _ExitTree で自前の台帳（予告カード・盤面セル）を
    /// 更地化するため、ここでの QueueFree だけで描画ノードも綺麗に掃除される。
    /// </summary>
    private void DismissProphecyOverlay()
    {
        if (_prophecyOverlay is null) return;

        if (GodotObject.IsInstanceValid(_prophecyOverlay))
        {
            _prophecyOverlay.CloseRequested -= OnProphecyCloseRequested;
            _prophecyOverlay.QueueFree();
        }
        _prophecyOverlay = null;
    }

    // ─── ジョブマニュアル オーバーレイ（全ジョブ解説カタログ） ────────────────
    //  ヘッダの常設「JOB MANUAL」押下を受け、全 8 ジョブの解説（JobCodex の英語テキスト +
    //  JobMaster の構造化データ）を無状態オーバーレイ JobManualOverlay として最前面へ overlay する。
    //  家系図・予言オーバーレイと同型の自己崩壊型ライフサイクル（CloseRequested / _ExitTree で QueueFree）。

    /// <summary>ヘッダの「JOB MANUAL」押下ハンドラ。ジョブ解説カタログを最前面へマウントする。</summary>
    private void OnJobManualPressed() => MountJobManualOverlay();

    /// <summary>ジョブマニュアルの「閉じる」意思表示ハンドラ。前面のオーバーレイを解放する。</summary>
    private void OnJobManualCloseRequested() => DismissJobManualOverlay();

    /// <summary>
    /// 全ジョブ解説カタログ（JobManualOverlay）を最前面へ展開する。多重展開・取り残しを避けるため、
    /// 生存中の旧オーバーレイがあれば先に確実に解放してから展開する。
    /// </summary>
    private void MountJobManualOverlay()
    {
        DismissJobManualOverlay();

        var overlay = new JobManualOverlay();
        overlay.CloseRequested += OnJobManualCloseRequested;
        overlay.SetMeta(TestIdMetaKey, "game-director-job-manual-overlay");
        _jobManualOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中のジョブマニュアルがあれば購読を解いて確実に解放する。閉じる意思表示
    /// （OnJobManualCloseRequested）および退場時（_ExitTree）に呼び、ゾンビノード・購読二重接続を根絶する。
    /// </summary>
    private void DismissJobManualOverlay()
    {
        if (_jobManualOverlay is null) return;

        if (GodotObject.IsInstanceValid(_jobManualOverlay))
        {
            _jobManualOverlay.CloseRequested -= OnJobManualCloseRequested;
            _jobManualOverlay.QueueFree();
        }
        _jobManualOverlay = null;
    }

    // ─── ユニット詳細モーダル（編成画面の「Details」から開く） ────────────────
    //  編成画面の名簿カード「Details」押下を受け、対象ユニットの完全プロファイル
    //  （ステータス/性別/血統/現在の陣形スロット番号 + ジョブ解説）を無状態モーダル
    //  UnitDetailOverlay として最前面へ overlay する。他オーバーレイと同型の自己崩壊型ライフサイクル。

    /// <summary>編成画面の「Details」意思表示ハンドラ。対象ユニットの詳細モーダルをマウントする。</summary>
    private void OnUnitInspectRequested(Guid unitId) => MountUnitDetailOverlay(unitId);

    /// <summary>ユニット詳細モーダルの「閉じる」意思表示ハンドラ。前面のモーダルを解放する。</summary>
    private void OnUnitDetailCloseRequested() => DismissUnitDetailOverlay();

    /// <summary>
    /// 指定ユニットの詳細モーダルを最前面へ展開する。多重展開・取り残しを避けるため、生存中の
    /// 旧モーダルがあれば先に確実に解放してから展開する。TargetUnitId は AddChild 前に注入する。
    /// </summary>
    private void MountUnitDetailOverlay(Guid unitId)
    {
        DismissUnitDetailOverlay();

        var overlay = new UnitDetailOverlay { TargetUnitId = unitId };
        overlay.CloseRequested += OnUnitDetailCloseRequested;
        overlay.SetMeta(TestIdMetaKey, "game-director-unit-detail-overlay");
        _unitDetailOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中のユニット詳細モーダルがあれば購読を解いて確実に解放する。閉じる意思表示
    /// （OnUnitDetailCloseRequested）および退場時（_ExitTree）に呼び、ゾンビノード・購読二重接続を根絶する。
    /// </summary>
    private void DismissUnitDetailOverlay()
    {
        if (_unitDetailOverlay is null) return;

        if (GodotObject.IsInstanceValid(_unitDetailOverlay))
        {
            _unitDetailOverlay.CloseRequested -= OnUnitDetailCloseRequested;
            _unitDetailOverlay.QueueFree();
        }
        _unitDetailOverlay = null;
    }

    // ─── 休息報酬通知オーバーレイ（戦闘を回避した年の成果提示） ────────────────
    //  編成で「休息」を選んで「次へ」を押したとき、編成画面・戦闘画面を一切経由せず、
    //  その場で ExecuteRest() の成果を提示する自己崩壊型オーバーレイ。家系図・予言・
    //  決算スクリーンと同型のライフサイクル（Confirmed / _ExitTree で QueueFree）。年送りは
    //  「確認（次代へ）」押下まで遅延し、提示が消滅副作用（加齢・予言再生成）に先行する（案A）。

    /// <summary>
    /// 休息報酬通知の「確認（次代へ）」意思表示ハンドラ。前面のオーバーレイを解放した後、
    /// 初めて AdvancePhase（編成 → 年代記の年送り）を駆動する。これにより休息成果の提示が
    /// 年送りの消滅副作用に確実に先行する。
    /// </summary>
    private void OnRestConfirmed()
    {
        DismissRestResultOverlay();
        _chronicleGlobal?.AdvancePhase();
    }

    /// <summary>
    /// 休息報酬通知オーバーレイを最前面へ展開する。多重展開・取り残しを避けるため、生存中の
    /// 旧オーバーレイがあれば先に確実に解放してから展開する。成果データはオーバーレイ自身が
    /// _Ready で ChronicleGlobal.LastRestOutcome を読むため、ここでは注入しない（無状態の徹底）。
    /// </summary>
    private void MountRestResultOverlay()
    {
        DismissRestResultOverlay();

        var overlay = new RestResultOverlay();
        overlay.Confirmed += OnRestConfirmed;
        overlay.SetMeta(TestIdMetaKey, "game-director-rest-result-overlay");
        _restResultOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    /// <summary>
    /// 前面展開中の休息報酬通知オーバーレイがあれば購読を解いて確実に解放する。確認意思表示
    /// （OnRestConfirmed）および退場時（_ExitTree）に呼び、ゾンビノード・購読二重接続を根絶する。
    /// </summary>
    private void DismissRestResultOverlay()
    {
        if (_restResultOverlay is null) return;

        if (GodotObject.IsInstanceValid(_restResultOverlay))
        {
            _restResultOverlay.Confirmed -= OnRestConfirmed;
            _restResultOverlay.QueueFree();
        }
        _restResultOverlay = null;
    }

    // ─── 3 択ドロップ overlay（EquipmentDrop 予言の戦利品選択） ────────────────

    /// <summary>
    /// DropChoicePending を受け、3 択ドロップオーバーレイを最前面へ立ち上げる。候補データは
    /// オーバーレイ自身が _Ready で ChronicleGlobal.PendingDropCandidates を読むため注入しない。
    /// </summary>
    private void OnDropChoicePending()
    {
        MountEquipmentDropOverlay();
    }

    /// <summary>取得（Chosen）後の後始末。前面のドロップオーバーレイを確実に解放する。</summary>
    private void OnDropChosen()
    {
        DismissEquipmentDropOverlay();
    }

    private void MountEquipmentDropOverlay()
    {
        DismissEquipmentDropOverlay();

        var overlay = new EquipmentDropOverlay();
        overlay.Chosen += OnDropChosen;
        overlay.SetMeta(TestIdMetaKey, "game-director-equipment-drop-overlay");
        _equipmentDropOverlay = overlay;

        AddChild(overlay); // root（および各フェーズ画面）の後に追加 = 最前面 overlay
    }

    private void DismissEquipmentDropOverlay()
    {
        if (_equipmentDropOverlay is null) return;

        if (GodotObject.IsInstanceValid(_equipmentDropOverlay))
        {
            _equipmentDropOverlay.Chosen -= OnDropChosen;
            _equipmentDropOverlay.QueueFree();
        }
        _equipmentDropOverlay = null;
    }

    // ─── レイアウト構築（ヘッダー + 画面コンテナ） ─────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // ── 全フェーズ共通の戦場背景（最背面・全画面） ───────────────
        //  root より先に AddChild して最背面へ敷く。各フェーズ画面は透明コンテナなので、
        //  この 1 枚が全画面（年代記/拠点/編成/戦闘）の背後に共通で透けて見える。
        _backgroundRect = new TextureRect
        {
            Name        = "SharedBackground",
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore, // 背景はクリックを食わない
        };
        _backgroundRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backgroundRect.SetMeta(TestIdMetaKey, "game-director-background");
        AddChild(_backgroundRect);
        RefreshBackground();

        // ── コンテンツカード（全画面ではない半透明カード） ───────────────
        //  画面端から ContentCardMarginPx だけ離した枠（MarginContainer）の中に、半透明の
        //  カード（PanelContainer）を置き、その上にヘッダ＋画面を載せる。余白から最背面の
        //  共通背景が額縁のように覗き、カード地は薄く背景を透かす（「背景の上にカード」の土台）。
        var cardMargin = new MarginContainer { Name = "ContentCardMargin" };
        cardMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cardMargin.AddThemeConstantOverride("margin_left", ContentCardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_top", ContentCardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_right", ContentCardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_bottom", ContentCardMarginPx);
        AddChild(cardMargin);

        var card = new PanelContainer { Name = "ContentCard" };
        card.AddThemeStyleboxOverride("panel", BuildContentCardStyleBox());
        card.SetMeta(TestIdMetaKey, "game-director-content-card");
        cardMargin.AddChild(card);

        // root（ヘッダ＋画面コンテナ）はカードの子。サイズは PanelContainer が司るので
        // FullRect アンカーは不要（カードの内側パディングぶん詰めて配置される）。
        var root = new VBoxContainer { Name = "DirectorRoot" };
        root.AddThemeConstantOverride("separation", 8);
        root.SetMeta(TestIdMetaKey, "game-director-root");
        card.AddChild(root);

        // ── 常設ヘッダー：フェーズインジケータ + 次へボタン ──────────
        var header = new HBoxContainer { Name = "DirectorHeader" };
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "game-director-header");
        root.AddChild(header);

        _phaseIndicatorLabel = new Label { Name = "PhaseIndicator" };
        _phaseIndicatorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _phaseIndicatorLabel.SetMeta(TestIdMetaKey, "game-director-phase-indicator");
        header.AddChild(_phaseIndicatorLabel);

        // 常設「ジョブマニュアル」ボタン。全フェーズ共通でヘッダに在駐し、押下で全ジョブ解説
        // カタログ（JobManualOverlay）を最前面へ overlay する（家系図・予言と同型の overlay 窓口）。
        var jobManualButton = new Button { Name = "JobManualButton", Text = "JOB MANUAL" };
        jobManualButton.Pressed += OnJobManualPressed;
        jobManualButton.SetMeta(TestIdMetaKey, "game-director-job-manual-button");
        header.AddChild(jobManualButton);

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

    /// <summary>
    /// コンテンツカードの地（半透明の暗色 + 角丸 + 淡い枠 + 内側パディング）を組む。
    /// 地色にアルファを持たせることで最背面の共通背景がうっすら透ける「薄いカード」になる。
    /// </summary>
    private static StyleBoxFlat BuildContentCardStyleBox()
    {
        var box = new StyleBoxFlat
        {
            BgColor      = ContentCardColor,
            BorderColor  = ContentCardBorderColor,
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(ContentCardCornerRadius);
        box.SetContentMarginAll(ContentCardInnerPadding);
        return box;
    }

    // ─── 動的B型 画面ライフサイクル（オンデマンド生成・遷移で完全消滅） ────────
    //  起動時一斉生成（旧 BuildScreens / CreateScreenFor）は完全廃止した。フェーズ遷移の
    //  その瞬間に、現在フェーズの画面【だけ】を new してマウントし、旧画面は QueueFree で
    //  シーンツリーから完全消滅させる。常に最大 1 枚しか存在しないため、隠れた画面が
    //  グローバル信号（PhaseChanged 等）を裏で購読して副作用（敵生成・ボタン多重上書き）を
    //  起こす余地が構造的に消える。各画面の ChronicleGlobal 購読はその画面が生きている間だけ
    //  有効で、QueueFree（_ExitTree）で確実に解かれる。

    /// <summary>
    /// 現在フェーズ（<see cref="ChronicleGlobal.CurrentPhase"/>）に対応する画面を【その場で new して】
    /// マウントする。先に旧画面を <see cref="FreeCurrentScreen"/> で完全消滅させてから、現在フェーズの
    /// 専用 Control を 1 枚だけ生成し、従属オーバーレイ召喚の意思表示を（その画面が生きている間だけ）
    /// 購読してツリーへ追加する。Battle 画面は自身の _Ready で（出撃時のみ）開戦＝敵生成する。
    /// </summary>
    private void MountScreenForCurrentPhase()
    {
        if (_chronicleGlobal is null || _screenContainer is null) return;

        // 旧フェーズ画面を Visible=false ではなく QueueFree で完全に消滅させる（イベント汚染封鎖）。
        FreeCurrentScreen();

        var phase = _chronicleGlobal.CurrentPhase;
        Godot.Control screen = phase switch
        {
            GamePhase.Chronicle => new TimelineUI(),   // 拠点A: 予言・歴史進行
            GamePhase.Guild     => new MarriageUI(),    // 拠点B: 婚姻・スカウト・今年の行動選択
            GamePhase.Formation => new FormationUI(),   // 大隊編成（出撃時のみ到達）
            GamePhase.Battle    => new BattleUI(),       // 戦闘: ターン制 → とどめ → 決算（出撃時のみ）
            _ => new Godot.Control(),                    // 未知フェーズの安全網（空画面）
        };

        // 従属オーバーレイ召喚の意思表示を、この画面が生きている間だけ購読する。
        switch (screen)
        {
            case MarriageUI marriage:
                marriage.PedigreeRequested += OnPedigreeRequested;
                break;
            case FormationUI formation:
                formation.UnitInspectRequested += OnUnitInspectRequested;
                break;
            case BattleUI battle:
                battle.ProphecyRequested += OnProphecyRequested;
                battle.UnitInspectRequested += OnUnitInspectRequested;
                break;
        }

        screen.Name = phase.Slug();
        screen.SetMeta(TestIdMetaKey, $"game-director-screen-{phase.Slug()}");
        screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _currentScreen = screen;
        _screenContainer.AddChild(screen); // AddChild で画面の _Ready が走り、現在状態を描画する
    }

    /// <summary>
    /// 現在生きているフェーズ画面（最大1枚）を、従属イベント購読を解いたうえで QueueFree し、
    /// シーンツリー・メモリから完全消滅させる。これにより隠れノードの裏購読を構造的に根絶する。
    /// </summary>
    private void FreeCurrentScreen()
    {
        if (_currentScreen is null) return;

        if (GodotObject.IsInstanceValid(_currentScreen))
        {
            // 破棄に先んじて従属オーバーレイ召喚の購読を解く（ゾンビ購読・二重接続の防止）。
            switch (_currentScreen)
            {
                case MarriageUI marriage:
                    marriage.PedigreeRequested -= OnPedigreeRequested;
                    break;
                case FormationUI formation:
                    formation.UnitInspectRequested -= OnUnitInspectRequested;
                    break;
                case BattleUI battle:
                    battle.ProphecyRequested -= OnProphecyRequested;
                    battle.UnitInspectRequested -= OnUnitInspectRequested;
                    break;
            }
            _currentScreen.QueueFree();
        }
        _currentScreen = null;
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.PhaseChanged     += OnPhaseChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
        // 暦が進んで章をまたいだら共通背景を張り替える（全フェーズに効く戦場背景の更新）。
        _chronicleGlobal.TimelineChanged  += OnTimelineChanged;
        // 編成変化で「次へ（出撃）」ボタンの可否を即時に再評価する（無人出撃の提示層ガード）。
        _chronicleGlobal.FormationChanged += OnFormationChanged;
        // 戦闘の進行/決着でヘッダ「次へ」のラベル（1ターン進める ⇄ 次へ）を即時に切り替える。
        _chronicleGlobal.BattleChanged    += OnBattleChanged;
        // EquipmentDrop の 3 択候補が発生したら、現フェーズに関わらず最前面へ overlay を立ち上げる。
        _chronicleGlobal.DropChoicePending += OnDropChoicePending;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.PhaseChanged     -= OnPhaseChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
            _chronicleGlobal.TimelineChanged  -= OnTimelineChanged;
            _chronicleGlobal.FormationChanged -= OnFormationChanged;
            _chronicleGlobal.BattleChanged    -= OnBattleChanged;
            _chronicleGlobal.DropChoicePending -= OnDropChoicePending;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    // ★ フェーズ遷移＝画面の動的な生成/破棄。旧画面を QueueFree し現在フェーズの画面を new する。
    //   ヘッダも合わせて更新する（画面の生死とヘッダ表示を 1 つの遷移で同期）。
    private void OnPhaseChanged()
    {
        MountScreenForCurrentPhase();
        RenderHeader();
    }

    // 行動トグル（FormationChanged）・戦闘進行（BattleChanged）はフェーズを跨がない＝画面は再生成
    // しない。ヘッダの「次へ」ラベル/活性だけを引き直す（生きている画面ノードを壊さない狙い撃ち）。
    private void OnFormationChanged() => RenderHeader();
    private void OnBattleChanged() => RenderHeader();

    // 暦の前進（世代交代で年が進む）で章が変わりうるため、共通背景を張り替える。
    private void OnTimelineChanged() => RefreshBackground();

    /// <summary>
    /// 世界が初期化された（新規 Initialize / セーブ LoadGame のいずれか）瞬間のハンドラ。
    /// まずタイトルゲートを退場（QueueFree）させ、その後に現在フェーズ画面を【動的生成】して
    /// ヘッダを更新する。新規・継続のどちらの経路でも本ハンドラがゲート後始末の単一窓口となる。
    /// </summary>
    private void OnStateInitialized()
    {
        DismissTitleScreen();
        MountScreenForCurrentPhase();
        RenderHeader();
        RefreshBackground(); // 新規/継続で世界が確定した瞬間に章背景を反映
    }

    /// <summary>
    /// 現在年の章（Epoch）に対応する共通背景を最背面へ張り替える。年は SoT
    /// （CurrentTimeline.Turn）から読み、未配置の章・未初期化時は Texture=null
    /// （従来どおり背景なし）へフォールバックする。全フェーズで共通の 1 枚を更新する。
    /// </summary>
    private void RefreshBackground()
    {
        if (_backgroundRect is null) return;

        var turn = _chronicleGlobal?.CurrentTimeline?.Turn ?? 0;
        if (turn <= 0)
        {
            _backgroundRect.Texture = null;
            return;
        }

        var epoch = ChronicleTimelineConfig.EpochForYear(turn).Id;
        _backgroundRect.Texture = BackgroundTextureLibrary.TryLoad(epoch);
    }

    // ─── ヘッダ描画（インジケータ + 次へボタンのみ。画面はマウント側が司る） ──────
    //  動的B型では「画面の切替」はノードの生成/破棄（MountScreenForCurrentPhase / FreeCurrentScreen）が
    //  担い、本メソッドは常設ヘッダ（フェーズ名 + 次へボタン）の表示更新だけに専念する。
    //  フェーズ遷移時のほか、行動トグル（FormationChanged）・戦闘進行（BattleChanged）でも呼ばれ、
    //  ボタンのラベル/活性を現在の SoT から都度引き直す。

    private void RenderHeader()
    {
        if (_chronicleGlobal is null) return;

        var current = _chronicleGlobal.CurrentPhase;

        // インジケータ更新：現在フェーズの日本語名を表示する。
        if (_phaseIndicatorLabel is not null)
        {
            _phaseIndicatorLabel.Text = $"🧭 現在: {_chronicleGlobal.ResolveCurrentPhaseName()}";
        }

        // 次へボタン更新：次フェーズ名を出し、年代記では隠す（予言選択が前進契機）。
        if (_advanceButton is not null)
        {
            var nextPhase = GamePhaseFlow.Next(current);
            _advanceButton.Visible = current != GamePhase.Chronicle;
            _advanceButton.Disabled = false;

            if (current == GamePhase.Battle
                && _chronicleGlobal.CurrentBattle is { Outcome: BattleOutcome.Ongoing })
            {
                // 戦闘中：フェーズは飛ばさず戦闘を 1 ターン進める（スキップ根治）。
                _advanceButton.Text = "▶ 1ターン進める";
            }
            else if (current == GamePhase.Guild)
            {
                // 拠点の「次へ」は今年の行動で行き先が変わる（選択した行動とフェーズが矛盾しない）。
                //   休息：編成・戦闘を回避し安全に年を送る → 配置は不要。
                //   出撃：満を持して編成画面へ入場する。
                _advanceButton.Text = _chronicleGlobal.CurrentAction == PlannedAction.Rest
                    ? "▶ 次へ：休息（戦闘を回避）"
                    : "▶ 次へ：出撃（編成へ）";
            }
            else if (current == GamePhase.Formation)
            {
                // 編成へ入るのは出撃を選んだ場合のみ。出撃 → 戦闘の前進は最低1名の配置を要求する
                // （無人出撃の提示層ガード。出撃時のみ発火＝FormationChanged で随時再評価）。
                if (!DeploymentGate.CanMarch(_chronicleGlobal.CurrentFormation))
                {
                    _advanceButton.Text = "▶ 出撃不可：最低1名を配置せよ";
                    _advanceButton.Disabled = true;
                }
                else
                {
                    _advanceButton.Text = "▶ 次へ：戦闘開始";
                }
            }
            else
            {
                _advanceButton.Text = $"▶ 次へ：{_chronicleGlobal.ResolvePhaseName(nextPhase)}";
            }
        }
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    private void OnAdvancePressed()
    {
        if (_chronicleGlobal is null) return;

        // ★ 戦闘スキップの根治: 戦闘フェーズで戦闘が進行中なら、「次へ」はフェーズを前進させず
        //   戦闘を 1 ターン進める（無作戦＝回転なし）へ再配線する。AdvancePhase へは委ねない。
        //   決着済み／非戦闘時のみ通常のフェーズ前進へ委ねる（前進可否は AdvancePhase 側の
        //   BattleProgressGate が最終判定し、未決着の Battle→Chronicle を構造的に拒絶する）。
        if (_chronicleGlobal.CurrentPhase == GamePhase.Battle
            && _chronicleGlobal.CurrentBattle is { Outcome: BattleOutcome.Ongoing })
        {
            _chronicleGlobal.ResolveBattleTurn(null);
            return;
        }

        // ★ 休息の根治: 拠点（Guild）で「休息」を選んで前進する場合、編成・戦闘の両フェーズを
        //   完全にバイパスする（行動決定を編成より上流に置いたので、休息では編成画面も戦闘画面も
        //   一度も描かれない）。AdvancePhase（年送り）へ即委ねず、まず ExecuteRest() で休息成果を
        //   確定・公開し、その成果を提示する RestResultOverlay を最前面へ展開する。年送りは
        //   オーバーレイの「確認（次代へ）」押下（OnRestConfirmed）まで遅延する。これにより
        //   編成画面・戦闘画面を物理的に 1 ミリも表示せず、その場で休息成果を提示できる。
        if (_chronicleGlobal.CurrentPhase == GamePhase.Guild
            && _chronicleGlobal.CurrentAction == PlannedAction.Rest)
        {
            _chronicleGlobal.ExecuteRest();
            // ★ 行動トグルの亡霊封鎖（B型: 役目終了＝即消滅）: 休息報酬画面を出す前に、役目を終えた
            //   拠点画面（MarriageUI＝「今年の行動」トグルの宿主）をノードごと QueueFree で完全消滅
            //   させる。休息は CurrentPhase が拠点のまま overlay を重ねるため、これを怠ると半透明
            //   オーバーレイ越しにトグルが透け、さらに確認後の年送りで CurrentAction が March へ戻る
            //   瞬間に「出撃」へ見た目リセットされた亡霊が一閃する。先に宿主を消せば 1 ピクセルも残らない。
            FreeCurrentScreen();
            MountRestResultOverlay();
            return;
        }

        // 前進の可否・順序は ChronicleGlobal（内部で GamePhaseFlow / BattleProgressGate）に委ねる。
        // 戻り値の再描画は PhaseChanged シグナル経由で自動的に行われる。
        _chronicleGlobal.AdvancePhase();
    }
}
