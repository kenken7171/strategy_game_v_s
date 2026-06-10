// =============================================================================
//  ChronicleKnights — BattleUI.cs
// -----------------------------------------------------------------------------
//  戦闘フェーズ画面（Control シーン）。1 ターン戦闘解決リゾルバ（BattleResolver）の
//  常駐スナップショット CurrentBattle を「丸ごと読み直して」描画するだけの、状態を
//  一切キャッシュしない薄い純粋描画層（設計憲法 ③）。
//
//  ★ 単方向データフロー（UI は状態を書き換えず、API を呼ぶだけ）:
//
//      コマンド操作（戦闘開始／無作戦・時計回り・反時計回りで 1 ターン／戦闘終了）
//          │  ChronicleGlobal.StartBattle / ResolveBattleTurn / EndBattle を呼ぶだけ
//          ▼
//      ChronicleGlobal が _stateLock 内で CurrentBattle を不変差し替え
//          │  ロック解放後に BattleChanged を SafeEmit
//          ▼
//      本画面が BattleChanged を受信し CurrentBattle を読み直して再描画
//
//  ★ ターンログの受け皿:
//    ResolveBattleTurn は「このターンに起きた出来事」の不変イベントログ
//    （ImmutableArray<BattleEvent>）を返す。本画面はそれを時系列に走査し、
//    1 行ずつログテキストへ落とす AppendTurnEvents をアニメーション再生の受け皿
//    （スケルトン）として備える。盤面・HP・敵カードの再描画自体は BattleChanged
//    経由の RenderAll が担うため、ログ追記と状態再描画は綺麗に分離される。
//
//  ★ 攻撃予告の可視化（未来を内包したスナップショット）:
//    CurrentBattle.NextEnemyIntent（常に非 null の「次ターンの敵攻撃」）を読み取り、
//    RenderEnemyIntent が敵カード直下の予告バナー（battle-enemy-intent-banner）へ
//    スキル名・パターン種別・威力・対象行チップを描く。さらに RenderBoard は対象行の
//    マスへ赤枠（StyleBoxFlat）を被せ、self_modulate のアルファをループ Tween で脈動
//    させて「危険エリア」を物質化する。決着済み（IsConcluded）・非戦闘ではバナーを
//    伏せ、赤枠も出さない（次ターンが無いため）。
//
//  ★ data-testid 規律:
//    各マスには座標から機械生成した ASCII 文字列（battle-slot-{Row}-{Column}）を中身の
//    Label に、予告対象マスにはラッパーへ battle-slot-targeted-{Row}-{Column} を付与する。
//    予告バナーは battle-enemy-intent-banner、対象行チップは
//    battle-enemy-intent-target-{Row}。敵カード・コマンド・ログ行にも battle-* の ASCII
//    命名を機械生成して付与する。
//
//  ★ ライフサイクル規律（メモリリーク防止 / Tween ゾンビ化の根絶）:
//    _Ready でシグナルを購読し、_ExitTree で確実に購読解除する。脈動用 Tween は
//    _dangerPulseTweens 台帳で一元管理し、RenderAll の【冒頭】と _ExitTree で必ず全
//    Kill する。これにより「丸ごと再描画」のたびの古い Tween 蓄積（リーク）を構造的に
//    根絶する。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）:
//    ジョブ名は ChronicleGlobal.ResolveJobName、敵スキル名は ChronicleGlobal.ResolveSkillName
//    経由で localization から解決する（コードに日本語の「データ名」を埋めない）。
//    敵の表示名・攻撃パターン種別は ASCII 列挙キーをそのまま見せる（squadRows と同様、
//    localization 解決は次段の拡張余地）。画面の地の文（chrome）の日本語は既存 UI と同じ方針。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 戦闘フェーズ画面。CurrentBattle を読み直して 9 マスの生存・HP・敵カードを描画し、
/// 回転コマンドで 1 ターンを解決する。戦闘状態のキャッシュは一切持たない。
/// </summary>
public partial class BattleUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せるメタキー（Godot ノードメタ）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>
    /// デモ用に戦闘を起動する際の世代年（敵スケールの基準）。本画面はゲーム進行の
    /// 一部としては Timeline/年の駆動を受けて StartBattle される想定だが、画面単体でも
    /// ライフサイクルを実演できるよう、ブートストラップ用の公称年を 1 つ持つ。
    /// </summary>
    private const int DemoBattleGenerationYear = 100;

    /// <summary>
    /// 危険スロット赤枠の脈動 1 山あたりの秒数（明 → 暗 / 暗 → 明 の片道）。
    /// ループ Tween がこの周期で self_modulate のアルファを上下させ、予告対象行を脈打たせる。
    /// </summary>
    private const double DangerPulseHalfPeriodSeconds = 0.55;

    /// <summary>危険脈動が最も淡くなった瞬間のアルファ（赤枠を消し切らず残光させる下限）。</summary>
    private const float DangerPulseMinimumAlpha = 0.30f;

    /// <summary>危険スロット赤枠の境界線の太さ（px）。</summary>
    private const int DangerFrameBorderWidth = 3;

    /// <summary>
    /// 予告危険色（赤）。スロット赤枠（StyleBoxFlat）の境界色であり、同時に「この行へ
    /// EnemyOffenseEvent が着弾する」という意味論の単一の色源でもある（Req 4 の整合点）。
    /// 予告の赤枠 → 実際の被弾ログ（EnemyOffenseEvent）が同じ赤で結ばれることで、
    /// プレイヤーは『予告された行が、まさにその行に着弾した』因果を一目で追える。
    /// </summary>
    private static readonly Color DangerFrameColor = new(0.86f, 0.20f, 0.20f);

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private Label? _statusLabel;
    private Label? _enemyNameLabel;
    private Label? _enemyHpLabel;
    private VBoxContainer? _boardContainer;
    private VBoxContainer? _logContainer;
    private Button? _startButton;
    private Button? _commandNoneButton;
    private Button? _commandClockwiseButton;
    private Button? _commandCounterClockwiseButton;
    private Button? _endButton;

    // ─── 敵攻撃予告バナー（BuildUI で 1 度だけ生成し、毎描画で中身だけ書き換える） ──

    private VBoxContainer? _intentBanner;
    private Label? _intentHeadlineLabel;
    private Label? _intentDetailLabel;
    private HBoxContainer? _intentTargetsRow;

    // ─── ログ行の連番（testid の機械生成に使う一過性のカウンタ） ──────────

    private int _logEntryCount;

    // ─── 危険スロット赤枠の脈動 Tween（再描画ごとに全 Kill して更地化する） ──
    //  ★ ゾンビ化・リーク防止規律（最重要）:
    //    「丸ごと再描画」のたびに古い脈動 Tween が残ると、無効ノードを掴んだまま
    //    多重起動して蓄積（リーク）する。これを根絶するため、生存中の脈動 Tween は
    //    この台帳で一元管理し、RenderAll の【冒頭】と _ExitTree で必ず全 Kill する。
    private readonly List<Tween> _dangerPulseTweens = new();

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
        // 退場時も生存中の脈動 Tween を必ず一掃する（リーク防止規律）。
        KillDangerPulses();
        UnsubscribeSignals();
    }

    // ─── UI 構築 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 16);
        AddChild(root);

        root.AddChild(new Label { Text = "⚔ 戦闘（V字3×3 / 1ターン解決）" });

        _statusLabel = new Label();
        _statusLabel.SetMeta(TestIdMetaKey, "battle-status-summary");
        root.AddChild(_statusLabel);

        // ── 敵カード ────────────────────────────────────────────────
        var enemyCard = new VBoxContainer();
        enemyCard.AddThemeConstantOverride("separation", 2);
        enemyCard.SetMeta(TestIdMetaKey, "battle-enemy-card");
        root.AddChild(enemyCard);

        _enemyNameLabel = new Label();
        _enemyNameLabel.SetMeta(TestIdMetaKey, "battle-enemy-name");
        enemyCard.AddChild(_enemyNameLabel);

        _enemyHpLabel = new Label();
        _enemyHpLabel.SetMeta(TestIdMetaKey, "battle-enemy-hp");
        enemyCard.AddChild(_enemyHpLabel);

        // ── 敵攻撃予告バナー（未来を内包したスナップショットの可視化） ──
        //  CurrentBattle.NextEnemyIntent を「読むだけ」で描く。バナー枠は 1 度だけ
        //  生成し、RenderEnemyIntent が毎描画で見出し・詳細・対象行チップを書き換える。
        _intentBanner = new VBoxContainer();
        _intentBanner.AddThemeConstantOverride("separation", 2);
        _intentBanner.SetMeta(TestIdMetaKey, "battle-enemy-intent-banner");
        root.AddChild(_intentBanner);

        _intentHeadlineLabel = new Label();
        _intentHeadlineLabel.SetMeta(TestIdMetaKey, "battle-enemy-intent-headline");
        _intentBanner.AddChild(_intentHeadlineLabel);

        _intentDetailLabel = new Label();
        _intentDetailLabel.SetMeta(TestIdMetaKey, "battle-enemy-intent-detail");
        _intentBanner.AddChild(_intentDetailLabel);

        _intentTargetsRow = new HBoxContainer();
        _intentTargetsRow.AddThemeConstantOverride("separation", 6);
        _intentTargetsRow.SetMeta(TestIdMetaKey, "battle-enemy-intent-targets");
        _intentBanner.AddChild(_intentTargetsRow);

        // ── 配置盤面（9 マス。BattleChanged ごとに再構築） ─────────
        root.AddChild(new Label { Text = "── 戦況盤面 ──" });

        _boardContainer = new VBoxContainer();
        _boardContainer.AddThemeConstantOverride("separation", 8);
        root.AddChild(_boardContainer);

        // ── コマンド行（戦闘開始 / 1ターン解決 / 戦闘終了） ────────
        var commandRow = new HBoxContainer();
        commandRow.AddThemeConstantOverride("separation", 8);
        commandRow.SetMeta(TestIdMetaKey, "battle-command-row");
        root.AddChild(commandRow);

        _startButton = new Button { Text = "⚔ 戦闘開始" };
        _startButton.SetMeta(TestIdMetaKey, "battle-command-start");
        _startButton.Pressed += OnStartPressed;
        commandRow.AddChild(_startButton);

        _commandNoneButton = new Button { Text = "▶ 無作戦で1ターン" };
        _commandNoneButton.SetMeta(TestIdMetaKey, "battle-command-none");
        _commandNoneButton.Pressed += () => OnResolveTurnPressed(null);
        commandRow.AddChild(_commandNoneButton);

        _commandClockwiseButton = new Button { Text = "⟳ 時計回りで1ターン" };
        _commandClockwiseButton.SetMeta(TestIdMetaKey, "battle-command-clockwise");
        _commandClockwiseButton.Pressed += () => OnResolveTurnPressed(RotationDirection.Clockwise);
        commandRow.AddChild(_commandClockwiseButton);

        _commandCounterClockwiseButton = new Button { Text = "⟲ 反時計回りで1ターン" };
        _commandCounterClockwiseButton.SetMeta(TestIdMetaKey, "battle-command-counter-clockwise");
        _commandCounterClockwiseButton.Pressed +=
            () => OnResolveTurnPressed(RotationDirection.CounterClockwise);
        commandRow.AddChild(_commandCounterClockwiseButton);

        _endButton = new Button { Text = "🏁 戦闘終了" };
        _endButton.SetMeta(TestIdMetaKey, "battle-command-end");
        _endButton.Pressed += OnEndPressed;
        commandRow.AddChild(_endButton);

        // ── ターンログ（ResolveBattleTurn の戻りイベントを時系列に積む） ──
        root.AddChild(new Label { Text = "── ターンログ ──" });

        var logScroll = new ScrollContainer();
        logScroll.SetMeta(TestIdMetaKey, "battle-log-scroll");
        logScroll.CustomMinimumSize = new Vector2(0, 220);
        logScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.AddChild(logScroll);

        _logContainer = new VBoxContainer();
        _logContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _logContainer.AddThemeConstantOverride("separation", 2);
        _logContainer.SetMeta(TestIdMetaKey, "battle-log");
        logScroll.AddChild(_logContainer);
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.BattleChanged     += OnBattleChanged;
        _chronicleGlobal.StateInitialized  += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.BattleChanged     -= OnBattleChanged;
            _chronicleGlobal.StateInitialized  -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnBattleChanged()    => RenderAll();
    private void OnStateInitialized() => RenderAll();

    // ─── 描画（すべて SoT を読み直して再構築。UI はキャッシュしない） ─────

    private void RenderAll()
    {
        // ★ 再描画の【冒頭】で必ず脈動 Tween を更地化する（ゾンビ化・リーク防止規律）。
        //   この後の RenderBoard が新しい予告に基づく脈動 Tween を生成し直す。
        KillDangerPulses();

        RenderStatus();
        RenderEnemy();
        RenderEnemyIntent();
        RenderBoard();
        UpdateCommandAvailability();
    }

    private void RenderStatus()
    {
        if (_statusLabel is null) return;

        var battle = _chronicleGlobal?.CurrentBattle;
        if (battle is null)
        {
            _statusLabel.Text = "現在、戦闘は行われていません（戦闘開始で起動）";
            return;
        }

        var outcomeText = battle.Outcome switch
        {
            BattleOutcome.BattalionVictory => "大隊の勝利",
            BattleOutcome.BattalionDefeat  => "大隊の敗北",
            _                              => "交戦中",
        };
        _statusLabel.Text = $"ターン {battle.TurnNumber}  /  状態: {outcomeText}";
    }

    private void RenderEnemy()
    {
        if (_enemyNameLabel is null || _enemyHpLabel is null) return;

        var battle = _chronicleGlobal?.CurrentBattle;
        if (battle is null)
        {
            _enemyNameLabel.Text = "敵: ―";
            _enemyHpLabel.Text = string.Empty;
            return;
        }

        var enemy = battle.Enemy;
        // 敵の表示名は現状 ASCII 列挙キー（localization 解決は次段の拡張余地）。
        _enemyNameLabel.Text = $"敵: {enemy.Archetype}";
        var percent = (int)Math.Round(enemy.HpRatio * 100.0);
        _enemyHpLabel.Text =
            $"HP {enemy.Hp} / {enemy.MaxHp} ({percent}%)  ATK {enemy.Attack}  SPD {enemy.Speed}";
    }

    /// <summary>
    /// 「未来を内包したスナップショット」CurrentBattle.NextEnemyIntent を読み取り、
    /// 次ターンの敵攻撃予告（スキル名・パターン種別・威力・対象行）をバナーへ描く。
    ///
    /// ★ 単方向・無キャッシュ: 状態は一切持たず、その時の SoT を読むだけ。
    /// ★ ① 準拠: スキル名は localization（ResolveSkillName）経由で解決し、コードに
    ///   日本語の「データ名」を埋めない。パターン種別は ASCII enum をそのまま併記する。
    /// ★ 状態安全ガード: 戦闘が無い／決着済み（IsConcluded）のときはバナーを
    ///   非表示（Visible = false）にする。予告データの読み取り自体はヌル安全に行う。
    /// </summary>
    private void RenderEnemyIntent()
    {
        if (_intentBanner is null
            || _intentHeadlineLabel is null
            || _intentDetailLabel is null
            || _intentTargetsRow is null)
        {
            return;
        }

        var battle = _chronicleGlobal?.CurrentBattle;

        // 非戦闘時・決着済みは予告の意味が無いのでバナーごと伏せる（次ターンが無い）。
        if (battle is null || battle.IsConcluded)
        {
            _intentBanner.Visible = false;
            ClearChildren(_intentTargetsRow);
            return;
        }

        _intentBanner.Visible = true;

        var intent = battle.NextEnemyIntent;
        var skillName = ResolveSkillName(intent.SkillNameKey);

        _intentHeadlineLabel.Text = "⚠ 次の敵攻撃予告";
        // スキル名（localization 解決）／パターン種別（ASCII enum）／1 体あたり威力を併記。
        _intentDetailLabel.Text =
            $"{skillName}（{intent.Kind}） — 対象 {intent.TargetRows.Length} 行 / 1 体あたり {intent.DamagePerUnit} ダメージ";

        // 対象行チップを機械的に組み立てる（battle-enemy-intent-target-{Row}）。
        ClearChildren(_intentTargetsRow);
        foreach (var row in intent.TargetRows)
        {
            var chip = new Label { Text = $"[{row}]" };
            chip.SetMeta(TestIdMetaKey, $"battle-enemy-intent-target-{row}");
            _intentTargetsRow.AddChild(chip);
        }
    }

    private void RenderBoard()
    {
        if (_boardContainer is null) return;

        ClearChildren(_boardContainer);

        var battle = _chronicleGlobal?.CurrentBattle;
        // 非戦闘時も 9 マスの testid を欠かさないよう、空盤面で骨格を描く。
        var board = battle?.Board ?? FormationBoard.Empty();

        // 予告対象マスは赤枠を被せておき、盤面が tree に入った後で脈動 Tween を起動する。
        // （Node.CreateTween はノードが SceneTree 内にある必要があるため、生成は parenting 後。）
        var targetedPanels = new List<PanelContainer>();

        foreach (var row in FormationBoard.RowOrder)
        {
            var rowGroup = new HBoxContainer();
            rowGroup.AddThemeConstantOverride("separation", 8);

            rowGroup.AddChild(new Label
            {
                Text = $"[{row}]",
                CustomMinimumSize = new Vector2(96, 0),
            });

            for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                var coordinate = new SlotCoordinate(row, column);
                var slot = BuildSlotPanel(battle, board, coordinate);
                rowGroup.AddChild(slot);

                if (IsSlotTargeted(battle, row))
                {
                    targetedPanels.Add(slot);
                }
            }

            _boardContainer.AddChild(rowGroup);
        }

        // 盤面が tree へ入った後で脈動 Tween を起動する（更地化済みの台帳へ積み直す）。
        foreach (var panel in targetedPanels)
        {
            StartDangerPulse(panel);
        }
    }

    /// <summary>
    /// 1 マスを PanelContainer で構築する。占有者の氏名・HP を中央の Label に描き、
    /// 予告対象行のマスには赤枠（StyleBoxFlat）と定型 testid を被せる。脈動 Tween は
    /// ノードが tree に入ってから <see cref="StartDangerPulse"/> が起動する（二段構え）。
    /// </summary>
    private PanelContainer BuildSlotPanel(BattleSnapshot? battle, FormationBoard board, SlotCoordinate coordinate)
    {
        var label = new Label
        {
            CustomMinimumSize = new Vector2(180, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 既存 E2E 互換: 基底のマス testid は従来どおり中身の Label に載せる。
        label.SetMeta(TestIdMetaKey, SlotTestId(coordinate));

        var occupant = board.OccupantAt(coordinate);
        if (occupant is { } unitId && battle is not null)
        {
            label.Text = DescribeCombatant(battle, unitId);
        }
        else if (occupant is { } emptyBattleId)
        {
            // 戦闘外でも盤面占有者は見せる（HP は戦闘文脈にしか無いので名前だけ）。
            label.Text = DescribeRosterUnit(emptyBattleId);
        }
        else
        {
            label.Text = "（空）";
        }

        var panel = new PanelContainer();
        panel.AddChild(label);

        // 予告対象行のマスだけ赤枠 + 定型 ASCII testid を被せる（脈動は parenting 後）。
        if (IsSlotTargeted(battle, coordinate.Row))
        {
            panel.SetMeta(TestIdMetaKey, $"battle-slot-targeted-{coordinate.Row}-{coordinate.Column}");
            ApplyDangerFrameStyle(panel);
        }

        return panel;
    }

    /// <summary>
    /// このマスの行が「次ターンの敵攻撃予告の対象行」に含まれるか。戦闘中（Ongoing）かつ
    /// NextEnemyIntent.Targets(row) のときのみ true（決着済み・非戦闘では危険表示しない）。
    /// </summary>
    private static bool IsSlotTargeted(BattleSnapshot? battle, SquadRow row)
        => battle is { Outcome: BattleOutcome.Ongoing } && battle.NextEnemyIntent.Targets(row);

    /// <summary>
    /// 予告対象マスへ赤枠（StyleBoxFlat）を被せる。境界色は <see cref="DangerFrameColor"/>
    /// で、これは EnemyOffenseEvent 着弾の意味論と同じ赤（Req 4 の整合点）。脈動は
    /// self_modulate を上下させるので、ここでは初期アルファを 1.0（不透明）に整える。
    /// </summary>
    private static void ApplyDangerFrameStyle(PanelContainer panel)
    {
        var frame = new StyleBoxFlat
        {
            BgColor     = new Color(DangerFrameColor, 0.08f),  // 内側はごく薄い赤の塗り
            BorderColor = DangerFrameColor,
        };
        frame.SetBorderWidthAll(DangerFrameBorderWidth);
        panel.AddThemeStyleboxOverride("panel", frame);
        panel.SelfModulate = Colors.White;
    }

    /// <summary>
    /// 予告対象マスの赤枠を、self_modulate のアルファだけ上下させてループ脈動させる。
    /// self_modulate はパネル自身の StyleBox を染めるが子（文字）は染めないため、
    /// 枠は脈打っても文字は褪せない。生成した Tween は台帳へ登録し、次回 RenderAll 冒頭の
    /// <see cref="KillDangerPulses"/> で必ず Kill する（ゾンビ化・リーク防止規律）。
    /// </summary>
    private void StartDangerPulse(PanelContainer panel)
    {
        var tween = panel.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(panel, "self_modulate:a", DangerPulseMinimumAlpha, DangerPulseHalfPeriodSeconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(panel, "self_modulate:a", 1.0f, DangerPulseHalfPeriodSeconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);

        _dangerPulseTweens.Add(tween);
    }

    /// <summary>
    /// 生存中の脈動 Tween をすべて明示的に Kill して台帳を更地化する。RenderAll の冒頭
    /// および _ExitTree で必ず呼び、「丸ごと再描画」のたびに古い Tween が無効ノードを
    /// 掴んだまま多重蓄積（リーク）するのを構造的に根絶する。
    /// </summary>
    private void KillDangerPulses()
    {
        foreach (var tween in _dangerPulseTweens)
        {
            if (tween is not null && tween.IsValid())
            {
                tween.Kill();
            }
        }
        _dangerPulseTweens.Clear();
    }

    // ─── コマンド操作（すべて ChronicleGlobal の API を呼ぶだけ） ─────────

    /// <summary>
    /// 戦闘開始: デモ用にスケールした試練の門の守護者を生成し、StartBattle を依頼する。
    /// 戦闘の真実差し替え・再描画は BattleChanged 経由で自動的に行われる（単方向フロー）。
    /// </summary>
    private void OnStartPressed()
    {
        if (_chronicleGlobal is null) return;

        // デモ起動用の敵を決定論ファクトリでスケールする（ブートストラップ）。
        var enemy = EnemyScaler.ScaleTrialGuardian(DemoBattleGenerationYear, new Random());

        ClearLog();
        AppendLogLine("戦闘開始", "battle-log-start");
        _chronicleGlobal.StartBattle(enemy);
    }

    /// <summary>
    /// 1 ターン解決: 選択された回転作戦を適用し、戻りイベントログをターンログへ積む。
    /// 盤面・HP・敵カードの再描画は BattleChanged 経由で別途行われる。
    /// </summary>
    private void OnResolveTurnPressed(RotationDirection? rotation)
    {
        if (_chronicleGlobal is null) return;

        var events = _chronicleGlobal.ResolveBattleTurn(rotation);
        AppendTurnEvents(events);
    }

    /// <summary>戦闘終了: 結末をロスタへ反映して非戦闘状態へ戻すよう EndBattle を依頼する。</summary>
    private void OnEndPressed()
    {
        if (_chronicleGlobal is null) return;

        var outcome = _chronicleGlobal.EndBattle();
        var outcomeText = outcome switch
        {
            BattleOutcome.BattalionVictory => "勝利で決着",
            BattleOutcome.BattalionDefeat  => "敗北で決着",
            _                              => "戦闘を終了",
        };
        AppendLogLine($"戦闘終了: {outcomeText}", "battle-log-end");
    }

    // ─── コマンド可否（戦闘状態から導出。UI はキャッシュしない） ──────────

    private void UpdateCommandAvailability()
    {
        var battle = _chronicleGlobal?.CurrentBattle;
        var isActive = battle is not null;
        var isOngoing = battle is { Outcome: BattleOutcome.Ongoing };

        if (_startButton is not null) _startButton.Disabled = isActive;
        if (_commandNoneButton is not null) _commandNoneButton.Disabled = !isOngoing;
        if (_commandClockwiseButton is not null) _commandClockwiseButton.Disabled = !isOngoing;
        if (_commandCounterClockwiseButton is not null)
            _commandCounterClockwiseButton.Disabled = !isOngoing;
        // 終了は「戦闘中（決着済みも含む）」でのみ押せる。
        if (_endButton is not null) _endButton.Disabled = !isActive;
    }

    // ─── ターンログ（イベント駆動の受け皿スケルトン） ────────────────────

    /// <summary>
    /// 1 ターンのイベントログを発生順に走査し、各イベントを 1 行へ落とす。ここが将来の
    /// アニメーション再生（被弾フラッシュ・撃破演出・回復ポップ等）のフックポイントになる。
    /// </summary>
    private void AppendTurnEvents(ImmutableArray<BattleEvent> events)
    {
        if (events.IsDefaultOrEmpty) return;

        foreach (var battleEvent in events)
        {
            AppendLogLine(DescribeEvent(battleEvent), $"battle-log-entry-{_logEntryCount}");
            // 将来の演出フック: イベント種別に応じて Tween / SE をここで起動する。
            //   特に EnemyOffenseEvent の着弾フラッシュは、予告で赤く脈動していた対象行
            //   （DangerFrameColor）と同じ赤で焚くことで、「予告された行 → 実際に着弾した
            //   行」の因果を色で結ぶ。赤枠（予告）と着弾フラッシュ（実弾）は単一の
            //   DangerFrameColor を共有し、意味論を一致させる（Req 4 の整合点）。
        }
    }

    /// <summary>
    /// 1 つの戦闘イベントを人間可読な 1 行へ変換する。判別共用体（record 継承）を
    /// switch 式で型安全に分岐する。ユニット名・ジョブ名は localization 経由で解決し、
    /// 数値のみを地の文に載せる（① 準拠）。
    /// </summary>
    private string DescribeEvent(BattleEvent battleEvent) => battleEvent switch
    {
        RotationPerformedEvent rotation =>
            $"作戦: 分隊を{DescribeDirection(rotation.Direction)}にローテーション",

        AllyOffenseEvent ally =>
            $"味方 {ResolveCombatantLabel(ally.AttackerId)} の攻撃 → {ally.Damage} ダメージ" +
            $"（敵 残HP {ally.EnemyHpAfter}）",

        EnemyOffenseEvent enemy =>
            $"敵の攻撃 → [{enemy.TargetRow}] 行へ 1 体あたり {enemy.DamagePerUnit} ダメージ",

        UnitDamagedEvent damaged =>
            $"被弾: {ResolveCombatantLabel(damaged.UnitId)} が {damaged.Damage} 受けた" +
            $"（残HP {damaged.HpAfter}）",

        UnitDefeatedEvent defeated =>
            $"撃破: {ResolveCombatantLabel(defeated.UnitId)} が戦闘不能（完全ロスト）",

        UnitHealedEvent healed =>
            $"回復: {ResolveCombatantLabel(healed.UnitId)} が {healed.HealAmount} 回復" +
            $"（現HP {healed.HpAfter}）",

        LastHitResolvedEvent lastHit => DescribeLastHit(lastHit),

        BattleConcludedEvent concluded =>
            concluded.Outcome == BattleOutcome.BattalionVictory
                ? "決着: 大隊の勝利！"
                : "決着: 大隊の敗北...",

        _ => battleEvent.ToString() ?? string.Empty,
    };

    private string DescribeLastHit(LastHitResolvedEvent lastHit)
    {
        var who = ResolveCombatantLabel(lastHit.UnitId);
        if (lastHit.ItemDestroyed && lastHit.GreedPointsStolen > 0)
        {
            return $"とどめ: {who} — 装備が砕け {lastHit.GreedPointsStolen} pt を強奪";
        }
        if (lastHit.ItemDestroyed)
        {
            return $"とどめ: {who} — 装備(Lv5)が砕け散った";
        }
        if (lastHit.ItemLevelUpTriggered)
        {
            return $"とどめ: {who} — 装備が +1 進化";
        }
        if (lastHit.LevelOverflow)
        {
            return $"とどめ: {who} — 既に Lv上限で経験は流れた";
        }
        return $"とどめ: {who} がトドメを刺した";
    }

    private static string DescribeDirection(RotationDirection direction)
        => direction == RotationDirection.Clockwise ? "時計回り" : "反時計回り";

    // ─── ログ行の追加 / クリア ────────────────────────────────────────────

    private void AppendLogLine(string text, string testId)
    {
        if (_logContainer is null) return;

        var line = new Label { Text = text };
        line.SetMeta(TestIdMetaKey, testId);
        _logContainer.AddChild(line);
        _logEntryCount++;
    }

    private void ClearLog()
    {
        if (_logContainer is null) return;
        ClearChildren(_logContainer);
        _logEntryCount = 0;
    }

    // ─── 補助 ─────────────────────────────────────────────────────────────

    /// <summary>戦闘中の占有者を「氏名 [ジョブ] HP現在/最大」で表現する。</summary>
    private string DescribeCombatant(BattleSnapshot battle, Guid unitId)
    {
        var unit = battle.CombatantOf(unitId) ?? _chronicleGlobal?.FindUnit(unitId);
        if (unit is null) return unitId.ToString();

        var currentHp = battle.HitPointsOf(unitId);
        var maxHp = ResolveMaxHitPoints(unit);
        var status = battle.IsCombatantAlive(unitId) ? string.Empty : "  (戦闘不能)";
        return $"{ResolveDisplayName(unit)}\n[{ResolveJobName(unit.Job)}] HP {currentHp}/{maxHp}{status}";
    }

    /// <summary>戦闘外（CurrentBattle == null）で盤面占有者を名前のみで表現する。</summary>
    private string DescribeRosterUnit(Guid unitId)
    {
        var unit = _chronicleGlobal?.FindUnit(unitId);
        if (unit is null) return unitId.ToString();
        return $"{ResolveDisplayName(unit)}\n[{ResolveJobName(unit.Job)}]";
    }

    /// <summary>ログ用の短いユニット表記（氏名 [ジョブ]）。占有者→ロスタの順に解決。</summary>
    private string ResolveCombatantLabel(Guid unitId)
    {
        var unit = _chronicleGlobal?.CurrentBattle?.CombatantOf(unitId)
                   ?? _chronicleGlobal?.FindUnit(unitId);
        if (unit is null) return unitId.ToString();
        return $"{ResolveDisplayName(unit)} [{ResolveJobName(unit.Job)}]";
    }

    private string ResolveDisplayName(Unit unit)
        => _chronicleGlobal?.ResolveDisplayName(unit) ?? unit.FirstNameKey;

    private string ResolveJobName(JobId job)
        => _chronicleGlobal?.ResolveJobName(job) ?? job.ToString();

    /// <summary>ユニットの最大 HP を所属ジョブ（JobMaster）から解決する。未知ジョブは 0。</summary>
    private static int ResolveMaxHitPoints(Unit unit)
        => JobMaster.Find(unit.Job)?.Stats.MaxHp ?? 0;

    /// <summary>座標から data-testid 用 ASCII 文字列を機械的に組み立てる。</summary>
    private static string SlotTestId(SlotCoordinate coordinate)
        => $"battle-slot-{coordinate.Row}-{coordinate.Column}";

    /// <summary>コンテナの全子ノードを破棄する（再構築前のクリア）。</summary>
    private static void ClearChildren(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            child.QueueFree();
        }
    }
}
