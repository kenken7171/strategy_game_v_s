// =============================================================================
//  ChronicleKnights — UserInterface/Battle/BattleView.cs
// -----------------------------------------------------------------------------
//  戦場画面（無状態 UI）。拠点で鍛えた旅団を盤面へ解き放ち、100 年の暴君と刃を交える舞台。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    味方 HP・敵 HP・経過ターンといった戦闘数理を 1 つもキャッシュしない。表示する値はすべて
//    SoT（ChronicleGlobal.CurrentBattle = リアルタイム戦闘スナップショット）から「その場で」読み直して
//    ラベル／HP バーへ一方通行で流し込む（Push バインド）。CurrentBattle が動けば（BattleChanged）
//    自動で読み直して描き直す。非戦闘（CurrentBattle == null）への遷移もこのシグナルで安全に映る。
//
//  ★ リークフリー（_battleNodes 台帳方式・既存実証済みの規律）:
//    動的生成した味方 HP バー行はすべて _battleNodes 台帳へ記録し、再描画（RenderAll）の冒頭と退場
//    （_ExitTree）で例外なく一括 QueueFree して更地化する。BattleChanged 購読も _ExitTree で完全解除。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"BATTLEFIELD:"/"TURN:"/"HP:" 等）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using ChronicleKnights.UI;
using Godot;

namespace ChronicleKnights.UserInterface.Battle;

/// <summary>
/// SoT（ChronicleGlobal.CurrentBattle）から経過ターン・敵 HP・味方 HP をその場で読み直して
/// Push バインドするだけの、状態を持たない戦場ビュー。ターン解決は SoT の API を叩くだけに徹する。
/// </summary>
public partial class BattleView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>戦場の背景色（血と硝煙の暗赤）。</summary>
    private static readonly Color BackdropColor = new(0.10f, 0.05f, 0.06f, 1.0f);

    /// <summary>敵意の赤枠脈動・とどめの緋色（≒ Colors.Crimson）。</summary>
    private static readonly Color OmenColor = new(0.86f, 0.08f, 0.24f);

    /// <summary>赤枠フラッシュの脈動秒数。</summary>
    private const double FlashSeconds = 0.6;

    /// <summary>
    /// 戦闘決着後「PROCEED」押下で、戦果還流を終え次画面（当面は拠点、将来は決算）へ進むことを
    /// 上位ルータへ知らせる。戦果の SoT 確定（EndBattle → Finalize → ApplyBattleSpoils）は本ビューが
    /// 済ませ、遷移だけを購読側へ委譲する。
    /// </summary>
    public event Action? BattleConcluded;

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 表示ノード（SoT を流し込む先。戦闘状態のキャッシュではない） ─────────
    private Label? _turnLabel;
    private Label? _intentBanner;
    private ProgressBar? _enemyHpBar;
    private Label? _enemyHpLabel;
    private VBoxContainer? _allyContainer;
    private Button? _resolveButton;
    private Button? _concludeButton;

    /// <summary>動的生成した味方 HP バー行の台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _battleNodes = new();

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "battle-view-root");

        BuildChrome();
        SubscribeSignals();
        RenderAll();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
        ClearBattleNodes();
    }

    // ─── 不変クローム構築 ─────────────────────────────────────────────────

    private void BuildChrome()
    {
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "battle-view-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        column.SetMeta(TestIdMetaKey, "battle-view-panel");
        margin.AddChild(column);

        var header = new Label { Text = "BATTLEFIELD:" };
        header.AddThemeFontSizeOverride("font_size", 32);
        header.SetMeta(TestIdMetaKey, "battle-view-header");
        column.AddChild(header);

        _turnLabel = new Label();
        _turnLabel.AddThemeFontSizeOverride("font_size", 20);
        _turnLabel.SetMeta(TestIdMetaKey, "battle-view-turn");
        column.AddChild(_turnLabel);

        // ── 敵意の予告バナー（次手 / 決着の緋色警告） ────────────────────────
        _intentBanner = new Label { Text = "INTENT: -" };
        _intentBanner.SetMeta(TestIdMetaKey, "battle-view-intent-banner");
        column.AddChild(_intentBanner);

        // ── 敵 HP セクション ──────────────────────────────────────────────
        var enemyHeader = new Label { Text = "ENEMY HP:" };
        enemyHeader.SetMeta(TestIdMetaKey, "battle-view-enemy-header");
        column.AddChild(enemyHeader);

        _enemyHpBar = new ProgressBar
        {
            MinValue          = 0,
            MaxValue          = 1,
            ShowPercentage    = false,
            CustomMinimumSize = new Vector2(360, 22),
        };
        _enemyHpBar.SetMeta(TestIdMetaKey, "battle-view-enemy-hp-bar");
        column.AddChild(_enemyHpBar);

        _enemyHpLabel = new Label { Text = "HP: 0 / 0" };
        _enemyHpLabel.SetMeta(TestIdMetaKey, "battle-view-enemy-hp-label");
        column.AddChild(_enemyHpLabel);

        // ── 味方 HP セクション（盤面参加者を台帳方式で動的生成） ──────────────
        var allyHeader = new Label { Text = "BATTALION:" };
        allyHeader.SetMeta(TestIdMetaKey, "battle-view-ally-header");
        column.AddChild(allyHeader);

        _allyContainer = new VBoxContainer();
        _allyContainer.AddThemeConstantOverride("separation", 4);
        _allyContainer.SetMeta(TestIdMetaKey, "battle-view-ally-container");
        column.AddChild(_allyContainer);

        // ── ターン解決（SoT の ResolveBattleTurn を叩くだけ。再描画は BattleChanged 任せ） ──
        _resolveButton = new Button { Text = "RESOLVE TURN" };
        _resolveButton.SetMeta(TestIdMetaKey, "battle-view-resolve-button");
        _resolveButton.Pressed += OnResolvePressed;
        column.AddChild(_resolveButton);

        // ── 決着後の前進（とどめ → 戦果還流 → 次画面）。決着するまでは無効 ──────
        _concludeButton = new Button { Text = "PROCEED", Disabled = true };
        _concludeButton.SetMeta(TestIdMetaKey, "battle-view-conclude-button");
        _concludeButton.Pressed += OnConcludePressed;
        column.AddChild(_concludeButton);
    }

    // ─── シグナル購読 / 解除（戦闘進行に追従して無状態に描き直す） ────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.BattleChanged += OnBattleChanged;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.BattleChanged -= OnBattleChanged;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（リーク防止）。
        }
    }

    private void OnBattleChanged() => RenderAll();

    // ─── ターン解決（SoT を叩くだけ・無状態） ────────────────────────────────

    private void OnResolvePressed()
    {
        if (_chronicleGlobal is null) return;

        var battle = _chronicleGlobal.CurrentBattle;
        if (battle is null || battle.IsConcluded) return; // 非戦闘・決着済みは no-op。

        _chronicleGlobal.ResolveBattleTurn(null); // 無作戦で 1 ターン解決 → BattleChanged → 再描画。
    }

    /// <summary>
    /// 決着後の前進（PROCEED）。とどめの戦果を SoT へ確定して経済へ還流し、次画面へバトンを渡す。
    /// EndBattle（ロスタ書戻し）→ FinalizeBattleSpoils（統合台帳確定）→ ApplyBattleSpoils（経済還流）の
    /// 一気通貫を SoT 単一窓口へ集約し、遷移だけを BattleConcluded で上位へ委譲する。
    /// </summary>
    private void OnConcludePressed()
    {
        if (_chronicleGlobal is null) return;

        var battle = _chronicleGlobal.CurrentBattle;
        if (battle is null || !battle.IsConcluded) return; // 決着前は no-op。

        _chronicleGlobal.EndBattle();                          // 結末をロスタ本体へ書き戻す。
        var spoils = _chronicleGlobal.FinalizeBattleSpoils();  // 統合台帳（ターン成長＋とどめ）を確定。
        _chronicleGlobal.ApplyBattleSpoils(spoils);            // 婚姻ポイントを経済へ還流（EconomyChanged）。

        BattleConcluded?.Invoke(); // ルータへ「次画面を立てよ」（当面は拠点、将来は決算）。
    }

    // ─── 更地化（台帳の全味方 HP バー行を一括解放） ──────────────────────────

    private void ClearBattleNodes()
    {
        foreach (var node in _battleNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _battleNodes.Clear();
    }

    // ─── 描画（SoT をその場で読み直して Push バインド） ─────────────────────

    private void RenderAll()
    {
        ClearBattleNodes(); // 何より先に更地化（再描画で味方行が累積しない）

        if (_turnLabel is null || _intentBanner is null || _enemyHpBar is null || _enemyHpLabel is null
            || _allyContainer is null || _resolveButton is null || _concludeButton is null)
        {
            return;
        }

        var battle = _chronicleGlobal?.CurrentBattle;
        if (battle is null)
        {
            // 非戦闘状態（開戦前・終戦後）。空のプレースホルダ + 行動ボタン全無効。
            _turnLabel.Text          = "TURN: -";
            _intentBanner.Text       = "INTENT: -";
            _enemyHpBar.Value        = 0;
            _enemyHpLabel.Text       = "HP: 0 / 0";
            _resolveButton.Disabled  = true;
            _concludeButton.Disabled = true;
            return;
        }

        _turnLabel.Text = "TURN: " + battle.TurnNumber;

        // 敵 HP（EnemyState から直接）。
        var enemy = battle.Enemy;
        _enemyHpBar.MaxValue = enemy.MaxHp > 0 ? enemy.MaxHp : 1;
        _enemyHpBar.Value    = enemy.Hp;
        _enemyHpLabel.Text   = "HP: " + enemy.Hp + " / " + enemy.MaxHp;

        var concluded = battle.IsConcluded;
        var intent    = battle.NextEnemyIntent;

        // 味方 HP バー（盤面の各参加者を動的生成。敵意の対象行に居る者は赤枠で脈動警告）。
        foreach (var combatant in battle.Combatants)
        {
            var unit     = combatant.Value;
            var unitRow  = battle.Board.CoordinateOf(unit.Id)?.Row;
            var targeted = !concluded && unitRow is { } resolvedRow && intent.Targets(resolvedRow);
            BuildAllyHpBar(unit, battle, targeted);
        }

        // 敵意バナー / 決着セレモニーと、行動ボタンの活殺。
        if (concluded)
        {
            var victory = battle.Outcome == BattleOutcome.BattalionVictory;
            _intentBanner.Text       = victory ? "CRITICAL -- VICTORY" : "DEFEAT";
            JuiceDirector.Flash(_intentBanner, OmenColor, FlashSeconds); // とどめの緋色脈動。
            _resolveButton.Disabled  = true;
            _concludeButton.Disabled = false;
        }
        else
        {
            _intentBanner.Text       = "INTENT: " + intent.Kind + " DMG " + intent.DamagePerUnit;
            JuiceDirector.Flash(_intentBanner, OmenColor, FlashSeconds); // 敵の次手が迫る緋色警告。
            _resolveButton.Disabled  = false;
            _concludeButton.Disabled = true;
        }
    }

    /// <summary>
    /// 味方 1 名の HP バー行（ジョブ + HP バー + HP 数値）を組み、台帳へ登録する。
    /// 敵意の対象行に居る（targeted）場合は、行ノードを緋色フラッシュで赤枠脈動させる
    /// （Tween は行ノードへ束縛され、次の再描画で QueueFree される際に自動失効する）。
    /// </summary>
    private void BuildAllyHpBar(Unit unit, BattleSnapshot battle, bool targeted)
    {
        if (_allyContainer is null) return;

        var currentHp = battle.UnitHitPoints.TryGetValue(unit.Id, out var hp) ? hp : 0;
        // 最大 HP は職ステータスの SoT（JobMaster）から。BattleResolver.ResolveMaxHitPoints と同源。
        var maxHp = JobMaster.Find(unit.Job)?.Stats.MaxHp ?? 0;

        var rowBox = new HBoxContainer();
        rowBox.AddThemeConstantOverride("separation", 8);
        rowBox.SetMeta(TestIdMetaKey, $"battle-view-ally-row-{unit.Id}");

        var jobLabel = new Label { Text = unit.Job.ToString() };
        jobLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        jobLabel.SetMeta(TestIdMetaKey, $"battle-view-ally-job-{unit.Id}");
        rowBox.AddChild(jobLabel);

        var hpBar = new ProgressBar
        {
            MinValue          = 0,
            MaxValue          = maxHp > 0 ? maxHp : 1,
            Value             = currentHp,
            ShowPercentage    = false,
            CustomMinimumSize = new Vector2(200, 18),
        };
        hpBar.SetMeta(TestIdMetaKey, $"battle-view-hp-bar-{unit.Id}");
        rowBox.AddChild(hpBar);

        var hpText = new Label { Text = "HP: " + currentHp + " / " + maxHp };
        hpText.SetMeta(TestIdMetaKey, $"battle-view-hp-text-{unit.Id}");
        rowBox.AddChild(hpText);

        _allyContainer.AddChild(rowBox);
        _battleNodes.Add(rowBox); // 台帳へ登録（更地化で一括解放）

        if (targeted)
        {
            JuiceDirector.Flash(rowBox, OmenColor, FlashSeconds); // 敵意の対象行＝赤枠脈動。
        }
    }
}
