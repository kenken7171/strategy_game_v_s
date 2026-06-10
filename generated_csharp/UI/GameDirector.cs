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
    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private Label? _phaseIndicatorLabel;
    private Button? _advanceButton;
    private Control? _screenContainer;

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

        // 新規ゲームのブートストラップ（起動エントリ）。
        // ★ 画面ノード群を AddChild 済み（= 各 UI が自身の _Ready でシグナル購読済み）
        //   のこのタイミングで Initialize を呼ぶことで、StateInitialized 等の初回シグナルが
        //   全 UI へ確実に届き、起動した瞬間に最初の旅団員と予言が描画される。
        BootstrapNewGameIfNeeded();

        RenderCurrentPhase();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
    }

    // ─── 新規ゲーム・ブートストラップ（起動エントリ） ───────────────────────
    //  ⚠ 最重要: 常駐ノード ChronicleGlobal は生成直後 IsInitialized == false の
    //  「無（未初期化）」状態で待機している。Initialize がどこからも呼ばれなければ、
    //  ロスターも予言も空のまま何も描画されない。本メソッドがその唯一の起動契機。

    /// <summary>
    /// 常駐ノード ChronicleGlobal がまだ初期化されていなければ、新規ゲームの初期状態
    /// （初期資金・初期ロスター・ターン 1 の予言 3 択）を生成して Initialize へ注入する。
    ///
    /// 設計:
    ///   - 初期ロスターと初期資金の構築は Godot 非依存の純粋ファクトリ
    ///     NewGameFactory（Core/Bootstrap）へ委譲する（脳と身体の分離・テスト容易性）。
    ///   - タイムライン（ターン 1 の予言 3 つ）は initialTimeline=null で渡し、
    ///     Initialize 内で同じ Random から生成させる（乱数列を 1 本に統一）。
    ///   - 既に初期化済み（IsInitialized == true）なら何もしない。これはセーブ継続ロードや
    ///     ホットリロードで二重初期化（＝進行中の世界の破棄）を起こさないための安全網。
    ///
    /// ★ 将来「つづきから」を実装する際は、本メソッドの先頭で
    ///   ChronicleGlobal.HasSaveData → LoadGame を試み、無ければ新規ゲームへ
    ///   フォールバックする分岐を足すだけでよい（起動エントリの単一窓口）。
    /// </summary>
    private void BootstrapNewGameIfNeeded()
    {
        if (_chronicleGlobal is null) return;
        if (_chronicleGlobal.IsInitialized) return;

        var rng = new Random();
        var seed = NewGameFactory.Create(rng);

        _chronicleGlobal.Initialize(
            initialRoster:   seed.Roster,
            initialEconomy:  seed.Economy,
            initialTimeline: null,   // ターン 1 の予言は Initialize が同じ rng で生成する
            rng:             rng);
    }

    // ─── レイアウト構築（ヘッダー + 画面コンテナ） ─────────────────────────

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var root = new VBoxContainer { Name = "DirectorRoot" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        // ── 常設ヘッダー：フェーズインジケータ + 次へボタン ──────────
        var header = new HBoxContainer { Name = "DirectorHeader" };
        header.AddThemeConstantOverride("separation", 16);
        root.AddChild(header);

        _phaseIndicatorLabel = new Label { Name = "PhaseIndicator" };
        _phaseIndicatorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_phaseIndicatorLabel);

        _advanceButton = new Button { Name = "AdvancePhaseButton" };
        _advanceButton.Pressed += OnAdvancePressed;
        header.AddChild(_advanceButton);

        // ── 画面コンテナ：4 フェーズ画面をぶら下げ、1 つだけ Visible にする ──
        _screenContainer = new Control { Name = "ScreenContainer" };
        _screenContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _screenContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(_screenContainer);
    }

    // ─── 画面生成（フェーズごとに 1 つ、Name をスラッグに設定） ────────────

    private void BuildScreens()
    {
        if (_screenContainer is null) return;

        foreach (var phase in GamePhaseFlow.Cycle)
        {
            var screen = CreateScreenFor(phase);
            // スラッグを Node 名にして、後段で GetNodeOrNull により安全に引き当てる。
            screen.Name = phase.Slug();
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

    private void OnPhaseChanged()     => RenderCurrentPhase();
    private void OnStateInitialized() => RenderCurrentPhase();

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
