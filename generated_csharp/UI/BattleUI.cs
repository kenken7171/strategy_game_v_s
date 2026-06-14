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
using ChronicleKnights.Core.GameFlow;
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
    /// さらに被弾フラッシュ（FlashRow）も同じ DangerFrameColor で焚き、予告→着弾の
    /// 因果を「色」で完全に一致させる。
    /// </summary>
    private static readonly Color DangerFrameColor = new(0.86f, 0.20f, 0.20f);

    // ─── 手触り演出（カメラシェイク / 被弾フラッシュ / 自己崩壊ポップアップ）の定数 ──
    //  ★ 演出はすべて「ワンショットで自己完結・自動消滅する Tween」で物質化し、UI には
    //    演出中フラグ等の状態を一切持たせない（設計憲法 ③）。唯一カメラシェイクだけは
    //    永続ノード（盤面ルート）を対象に取るため、多重起動による位置ドリフトを防ぐ目的で
    //    直近 1 本のハンドルを保持し、新シェイク前に必ず Kill する（リーク・ゾンビ化防止）。

    /// <summary>カメラシェイク 1 ジョルトあたりの秒数（往復の片道）。</summary>
    private const double CameraShakeStepSeconds = 0.05;

    /// <summary>カメラシェイクの初期振幅（px）。ジョルトごとに係数で減衰させ収束させる。</summary>
    private const float CameraShakeAmplitude = 14.0f;

    /// <summary>被弾フラッシュ（対象行 modulate の赤→白フェード）の秒数。</summary>
    private const double RowFlashSeconds = 0.35;

    /// <summary>ダメージポップアップが上方へ跳ね上がる距離（px）。</summary>
    private const float DamagePopupRiseDistance = 48.0f;

    /// <summary>ダメージポップアップの寿命（上昇 + フェードアウトの秒数）。完了で自己 QueueFree。</summary>
    private const double DamagePopupLifeSeconds = 0.70;

    /// <summary>被ダメージ / 撃破ポップアップの色（赤）。</summary>
    private static readonly Color DamagePopupColor = new(0.94f, 0.28f, 0.24f);

    /// <summary>回復ポップアップの色（緑）。</summary>
    private static readonly Color HealPopupColor = new(0.36f, 0.84f, 0.42f);

    /// <summary>とどめポップアップの色（金）。</summary>
    private static readonly Color LastHitPopupColor = new(0.98f, 0.82f, 0.25f);

    /// <summary>撃破ポップアップの記号（chrome 装飾。データ名ではないので ① に抵触しない）。</summary>
    private const string DefeatPopupMark = "✖";

    /// <summary>とどめポップアップの記号（chrome 装飾）。</summary>
    private const string LastHitPopupMark = "★";

    // ─── 4 大 UX 演出の定数（JuiceDirector へ渡す調律値。SoT ではなく見た目の調整値） ──
    //  ★ ① 敵 HP ドレイン: 前面バーが即座に新 HP を示し、背後のドレインバーが遅れて追従する。
    //  ★ ② ローテーション滑走: 盤面再構築後、各ユニットの旧位置から新位置へ position 補間。
    //  ★ ③ 死亡退場: 撃破された英霊のカードを低アルファのセピアへ沈める。
    //  ★ ④ 予告ワインドアップ: 敵攻撃の着弾と同時に敵カードをスケールパンチさせる。

    /// <summary>① ドレインバーが追従を始めるまでの溜め（秒）。前面バーの即時減少を一瞬見せる。</summary>
    private const double EnemyHpDrainDelaySeconds = 0.10;

    /// <summary>① ドレインバーが新 HP へ滑り込むまでの時間（秒）。被害の重みを尾を引いて見せる。</summary>
    private const double EnemyHpDrainDurationSeconds = 0.55;

    /// <summary>② ローテーション滑走 1 回の時間（秒）。</summary>
    private const double RotationSlideSeconds = 0.32;

    /// <summary>② 滑走を焚く最小移動量の二乗（px²）。これ未満の微動は滑走させない（静止セルの除外）。</summary>
    private const float RotationSlideMinTravelSquared = 4.0f;

    /// <summary>③ 死亡フェードの時間（秒）。</summary>
    private const double DeathFadeSeconds = 0.65;

    /// <summary>④ 予告ワインドアップ（敵カードのスケールパンチ）の時間（秒）。</summary>
    private const double EnemyWindupPunchSeconds = 0.28;

    /// <summary>④ パンチのピーク倍率（1.0 = 等倍）。1.10 で 1 割膨らんでから戻る。</summary>
    private const float EnemyWindupPunchScale = 1.10f;

    /// <summary>③ 死亡フェードの到達色（低アルファのセピア調）。modulate 乗算でカード全体を褪色させる。</summary>
    private static readonly Color DeathFadeTint = new(0.42f, 0.32f, 0.24f, 0.32f);

    /// <summary>① ドレインバー（遅れて追従する尾）の色。淡い金で「直前まで在った HP」を残光させる。</summary>
    private static readonly Color EnemyHpDrainColor = new(0.97f, 0.86f, 0.42f);

    /// <summary>① HP バーの空トラック（背景）の色。</summary>
    private static readonly Color EnemyHpBarTrackColor = new(0.12f, 0.12f, 0.14f);

    /// <summary>① 前面 HP バーが満タン寄りのときの色（緑）。比率で <see cref="EnemyHpLowColor"/> へ寄る。</summary>
    private static readonly Color EnemyHpHighColor = new(0.30f, 0.78f, 0.36f);

    /// <summary>① 前面 HP バーが瀕死寄りのときの色（赤）。</summary>
    private static readonly Color EnemyHpLowColor = new(0.85f, 0.22f, 0.20f);

    // ─── 意思表示イベント（オーバーレイのマウントは購読側 = GameDirector が引く） ──

    /// <summary>
    /// 運命の帯（予言タイムライン）オーバーレイを開く意思表示。購読側（GameDirector）が
    /// ProphecyTimelineOverlay を最前面へマウントする。本 UI はオーバーレイの生死を一切
    /// 持たない（無状態の徹底。N ターン先予言の手繰りと描画はオーバーレイ自身が担う）。
    /// </summary>
    public event Action? ProphecyRequested;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private Label? _statusLabel;
    private VBoxContainer? _enemyCard;
    private Label? _enemyNameLabel;

    /// <summary>時代スケール敵の原型を機械可読 testid（battle-enemy-instance-{archetype}）で晒す無表示ビーコン。</summary>
    private Control? _enemyInstanceMarker;
    private Label? _enemyHpLabel;
    private VBoxContainer? _boardContainer;
    private VBoxContainer? _logContainer;

    // ─── ① 敵 HP ドレインバー（前面＝即時 HP / 背面＝遅れて追従する尾） ─────────
    //  ★ 背面（ドレイン）バーを先に AddChild して下に敷き、前面バーを後から重ねる。
    //    前面バーは背景 stylebox を透明にして「自分の塗り（fill）だけ」を描くため、
    //    前面が削れて短くなった領域から、背面ドレインバーの尾（淡い金）が覗く。
    private ProgressBar? _enemyHpBarDrain;
    private ProgressBar? _enemyHpBarFront;

    /// <summary>
    /// ① ドレインバーの直近の追従 Tween。永続ノード（ドレインバー）を対象にするため、
    /// 新しい減少のたびに前の追従を Kill して多重 value 補間の競合を断つ（ゾンビ化防止規律）。
    /// </summary>
    private Tween? _enemyHpDrainTween;

    // ─── 手触り演出のための参照（BuildUI で 1 度だけ確定） ────────────────
    //  ★ カメラシェイクは盤面ルート（VBoxContainer）の position を往復させて実現する。
    //    ルートの親は本 Control（コンテナではない）なので、コンテナのレイアウト再ソートに
    //    position を奪われない。これにより「画面全体の揺れ」を安定して焚ける。
    private VBoxContainer? _rootShakeTarget;

    //  ★ ダメージポップアップ専用オーバーレイ層。コンテナ配下に置くとレイアウト対象に
    //    なってしまうため、本 Control 直下（非コンテナ）の全画面 Control として独立させ、
    //    マウスは透過（Ignore）させる。ポップアップはここへ載せ、global 座標で配置する。
    private Control? _popupLayer;
    private Button? _startButton;
    private Button? _commandNoneButton;
    private Button? _commandClockwiseButton;
    private Button? _commandCounterClockwiseButton;
    private Button? _endButton;

    // ─── 三段フローの従属モーダル（決着時に順に前面展開する無状態ノード） ──────
    //  ★ 三段フローの規律（戦闘 → とどめ → 決算 → 世代交代）:
    //    ① 決着後 EndBattle で戦闘後ロスタを正本化（決算はまだ確定しない・遅延算出）。
    //    ② とどめの儀式（LastHitCeremonyScreen）を前面展開し、生存者から 1 名を必ず
    //       選ばせて ResolveLastHit を解決する。
    //    ③ 儀式の Confirmed を受けて FinalizeBattleSpoils（統合台帳の確定）→ 戦果決算
    //       スクリーン（BattleSpoilsScreen）を前面展開する。
    //    ④ 決算の Confirmed を受けて初めて AdvancePhase を駆動し、世代交代の消滅副作用
    //       （完全ロストの掃き出し）の前に戦果と死者を安全に提示しきる。
    private LastHitCeremonyScreen? _lastHitScreen;
    private BattleSpoilsScreen? _spoilsScreen;

    // ─── 敵攻撃予告バナー（BuildUI で 1 度だけ生成し、毎描画で中身だけ書き換える） ──

    private VBoxContainer? _intentBanner;
    private Label? _intentHeadlineLabel;
    private Label? _intentDetailLabel;
    private HBoxContainer? _intentTargetsRow;

    // ─── ログ行の連番（testid の機械生成に使う一過性のカウンタ） ──────────

    private int _logEntryCount;

    // ─── 手触り演出の補助状態（描画ごとに作り直す or 一過性カウンタ） ──────

    /// <summary>
    /// 行 → その行の 3 マス Panel の対応表。RenderBoard の冒頭で更地化し再構築する。
    /// 被弾フラッシュ（FlashRow）が「予告対象行 = 着弾行」を一発で掴むための索引。
    /// </summary>
    private readonly Dictionary<SquadRow, List<PanelContainer>> _rowPanels = new();

    /// <summary>
    /// ユニット Id → そのユニットが占有するマス Panel の対応表。RenderBoard の冒頭で
    /// 更地化し再構築する。被弾・回復・撃破・とどめポップアップを当人のマス上へ出すための索引。
    /// </summary>
    private readonly Dictionary<Guid, PanelContainer> _unitPanels = new();

    /// <summary>ポップアップ testid の機械生成に使う一過性カウンタ（自己崩壊するので回収不要）。</summary>
    private int _popupSpawnCount;

    /// <summary>
    /// 直近のカメラシェイク Tween のハンドル。永続ノード（盤面ルート）を対象にするため、
    /// 新シェイク前に必ず Kill して位置ドリフト・多重起動を断つ（ゾンビ化・リーク防止規律）。
    /// </summary>
    private Tween? _cameraShakeTween;

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
        // 永続ノード（盤面ルート）を掴むカメラシェイク Tween も明示的に Kill する。
        // （ルート自体は cascade で解放され Tween も自動失効するが、規律として明示破棄する。）
        KillCameraShake();
        // ① 永続ノード（ドレインバー）を掴む HP ドレイン追従 Tween も明示的に Kill する。
        KillEnemyHpDrain();
        // 従属モーダルが前面展開中のまま退場する場合に備え、購読を解いて確実に解放する。
        DismissLastHitCeremony();
        DismissSpoilsScreen();
        UnsubscribeSignals();
    }

    // ─── UI 構築 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 16);
        AddChild(root);

        // カメラシェイクの対象（盤面ルート）。親は本 Control（非コンテナ）なので position を奪われない。
        _rootShakeTarget = root;

        root.AddChild(new Label { Text = "⚔ 戦闘（V字3×3 / 1ターン解決）" });

        _statusLabel = new Label();
        _statusLabel.SetMeta(TestIdMetaKey, "battle-status-summary");
        root.AddChild(_statusLabel);

        // ── 敵カード ────────────────────────────────────────────────
        var enemyCard = new VBoxContainer();
        enemyCard.AddThemeConstantOverride("separation", 2);
        enemyCard.SetMeta(TestIdMetaKey, "battle-enemy-card");
        root.AddChild(enemyCard);
        // 味方の攻撃着弾（AllyOffenseEvent）でこのカードをフラッシュさせるため参照を保持。
        _enemyCard = enemyCard;

        _enemyNameLabel = new Label();
        _enemyNameLabel.SetMeta(TestIdMetaKey, "battle-enemy-name");
        enemyCard.AddChild(_enemyNameLabel);

        // 時代スケール敵の原型を晒す無表示の testid ビーコン（RenderEnemy で原型ごとに meta を更新）。
        // 一度だけ生成し以後は更新のみ＝再描画でノードを増やさない（リークフリー）。
        _enemyInstanceMarker = new Control
        {
            MouseFilter       = MouseFilterEnum.Ignore,
            CustomMinimumSize = Vector2.Zero,
        };
        _enemyInstanceMarker.SetMeta(TestIdMetaKey, "battle-enemy-instance-none");
        enemyCard.AddChild(_enemyInstanceMarker);

        _enemyHpLabel = new Label();
        _enemyHpLabel.SetMeta(TestIdMetaKey, "battle-enemy-hp");
        enemyCard.AddChild(_enemyHpLabel);

        // ── ① 敵 HP ドレインバー領域（前面＝即時 HP / 背面＝遅れて追従する尾） ──
        //  非コンテナの素の Control をトラックにし、その中へ 2 本の ProgressBar を
        //  FullRect で重ねる。背面（ドレイン）を先に敷き、前面を後から重ねて最前面にする。
        var hpBarRegion = new Control { CustomMinimumSize = new Vector2(0, 18) };
        hpBarRegion.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hpBarRegion.SetMeta(TestIdMetaKey, "battle-enemy-hp-bar");
        enemyCard.AddChild(hpBarRegion);

        // 背面: 空トラック（暗）＋ ドレインの塗り（淡い金）。前面の下から尾が覗く。
        _enemyHpBarDrain = BuildHpBar(
            new StyleBoxFlat { BgColor = EnemyHpBarTrackColor }, EnemyHpDrainColor,
            "battle-enemy-hp-bar-drain");
        hpBarRegion.AddChild(_enemyHpBarDrain);

        // 前面: 背景は透明（自分の塗りだけ描く）＋ HP 色の塗り。比率で緑→赤に再着色する。
        _enemyHpBarFront = BuildHpBar(
            new StyleBoxEmpty(), EnemyHpHighColor, "battle-enemy-hp-bar-front");
        hpBarRegion.AddChild(_enemyHpBarFront);

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

        // 戦闘開始トリガーが「時代スケール敵生成」であることを晒す無表示の testid ビーコン
        // （E2E・マクロ調律の計測点。既存 battle-command-start を改名せず追加する）。
        var eraStartBeacon = new Control
        {
            MouseFilter       = MouseFilterEnum.Ignore,
            CustomMinimumSize = Vector2.Zero,
        };
        eraStartBeacon.SetMeta(TestIdMetaKey, "battle-start-era-scaled");
        commandRow.AddChild(eraStartBeacon);

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

        // ── 運命の帯（予言タイムライン）を開くボタン ────────────────
        //  押下で ProphecyRequested を発火するだけ（無状態の徹底）。オーバーレイの生死は
        //  購読側 GameDirector が一手に握る。ボタンは戦闘状態を持たないためフィールド化せず
        //  ローカル変数で生成・即購読・即ぶら下げる（assigned-but-never-read を構造的に回避）。
        var prophecyButton = new Button { Text = "🔮 運命の帯" };
        prophecyButton.SetMeta(TestIdMetaKey, "battle-prophecy-open-button");
        prophecyButton.Pressed += OnProphecyPressed;
        commandRow.AddChild(prophecyButton);

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

        // ── ダメージポップアップ用オーバーレイ層（最前面・マウス透過の全画面 Control） ──
        //  本 Control 直下（root と兄弟）に置くことで、VBox のレイアウト対象にならず
        //  自由な global 座標へポップアップを配置できる。root より後に AddChild するため
        //  描画順は最前面。MouseFilter=Ignore で下のコマンドボタンへの操作を妨げない。
        _popupLayer = new Control();
        _popupLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _popupLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        _popupLayer.SetMeta(TestIdMetaKey, "battle-damage-popup-layer");
        AddChild(_popupLayer);
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
            _enemyInstanceMarker?.SetMeta(TestIdMetaKey, "battle-enemy-instance-none");
            ResetEnemyHpBars();
            return;
        }

        var enemy = battle.Enemy;
        // 敵の表示名は現状 ASCII 列挙キー（display 名の localization 解決は次段の拡張余地）。
        _enemyNameLabel.Text = $"敵: {enemy.Archetype}";
        // 時代スケール敵の原型を機械可読 testid へ反映（E2E・マクロ調律の計測点）。
        _enemyInstanceMarker?.SetMeta(TestIdMetaKey, $"battle-enemy-instance-{ArchetypeSlug(enemy.Archetype)}");
        var percent = (int)Math.Round(enemy.HpRatio * 100.0);
        _enemyHpLabel.Text =
            $"HP {enemy.Hp} / {enemy.MaxHp} ({percent}%)  ATK {enemy.Attack}  SPD {enemy.Speed}";

        // ① 前面バーは即座に新 HP を示し、背面ドレインバーが遅れて追従する（被害の重みの可視化）。
        DriveEnemyHpBars(enemy.Hp, enemy.MaxHp, enemy.HpRatio);
    }

    /// <summary>
    /// 敵原型 enum を testid 用 ASCII kebab スラッグへ写す（EternalSovereign → "eternal-sovereign"）。
    /// 表示テキストではなく機械可読キーゆえ localization は不要（PascalCase の語境界に '-' を差す）。
    /// </summary>
    private static string ArchetypeSlug(EnemyArchetype archetype)
    {
        var name = archetype.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('-');
            }
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    // ─── ① 敵 HP ドレインバーの駆動（前面＝即時 / 背面＝遅延追従） ──────────────

    /// <summary>
    /// 前面バーを新 HP へ即時スナップし比率で再着色する。背面ドレインバーは、HP が
    /// 「減った」ときだけ現在値（＝直前まで在った高い HP）から新 HP へ遅延補間させ、
    /// 削れた量を尾を引くように見せる。HP が増えた（回復）・初回描画では尾を即時スナップする。
    ///
    /// ★ 無キャッシュ整合: 前面・背面バーの value はノード自身が前回描画値を保持するため、
    ///   UI 側に「直前の HP」を別途キャッシュせずとも、背面 value との大小比較だけで
    ///   「減少か否か」を導出できる（SoT は CurrentBattle のみ・憲法③）。
    /// </summary>
    private void DriveEnemyHpBars(int hp, int maxHp, double ratio)
    {
        // ローカルへ束ねてから null 判定する。以後は KillEnemyHpDrain 等の呼び出しを挟んでも
        // ローカルの非 null 状態は保たれるため、CS8602（possibly null 参照）を構造的に避ける。
        var front = _enemyHpBarFront;
        var drain = _enemyHpBarDrain;
        if (front is null || drain is null) return;

        front.MaxValue = maxHp;
        drain.MaxValue = maxHp;

        // 前面バーは即時に新 HP を示し、残量比率で緑→赤へ再着色する。
        front.Value = hp;
        ApplyBarFillColor(front,
            EnemyHpHighColor.Lerp(EnemyHpLowColor, 1.0f - (float)Math.Clamp(ratio, 0.0, 1.0)));

        // 背面ドレインバーは直近の追従 Tween を必ず Kill してから判定する（多重 value 補間の競合防止）。
        KillEnemyHpDrain();
        if (hp < drain.Value)
        {
            // 減少: 現在値（高い HP）から新 HP へ遅延追従させ、削れた尾を残光させる。
            _enemyHpDrainTween = JuiceDirector.DrainBar(
                drain, hp, EnemyHpDrainDelaySeconds, EnemyHpDrainDurationSeconds);
        }
        else
        {
            // 回復・初回描画: 尾を新 HP へ即時スナップ（遅延追従しない）。
            drain.Value = hp;
        }
    }

    /// <summary>非戦闘へ戻ったとき、両 HP バーを空に畳み、追従 Tween を明示的に Kill する。</summary>
    private void ResetEnemyHpBars()
    {
        KillEnemyHpDrain();
        if (_enemyHpBarFront is not null)
        {
            _enemyHpBarFront.MaxValue = 1;
            _enemyHpBarFront.Value = 0;
        }
        if (_enemyHpBarDrain is not null)
        {
            _enemyHpBarDrain.MaxValue = 1;
            _enemyHpBarDrain.Value = 0;
        }
    }

    /// <summary>① ドレインバーの追従 Tween を明示的に Kill する（新減少前・退場時）。</summary>
    private void KillEnemyHpDrain()
    {
        if (_enemyHpDrainTween is not null && _enemyHpDrainTween.IsValid())
        {
            _enemyHpDrainTween.Kill();
        }
        _enemyHpDrainTween = null;
    }

    /// <summary>HP バー（ProgressBar）の塗り（fill）stylebox を指定色で差し替える。</summary>
    private static void ApplyBarFillColor(ProgressBar bar, Color color)
    {
        var fill = new StyleBoxFlat { BgColor = color };
        fill.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("fill", fill);
    }

    /// <summary>
    /// 重ね描き用の ProgressBar を 1 本組み立てる。パーセント表記は伏せ、背景・塗りの
    /// stylebox を与えて FullRect で親トラックいっぱいに広げる。
    /// </summary>
    private static ProgressBar BuildHpBar(StyleBox background, Color fillColor, string testId)
    {
        var bar = new ProgressBar
        {
            ShowPercentage = false,
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
        };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bar.AddThemeStyleboxOverride("background", background);

        var fill = new StyleBoxFlat { BgColor = fillColor };
        fill.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("fill", fill);

        bar.SetMeta(TestIdMetaKey, testId);
        return bar;
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
        var skillName = _chronicleGlobal?.ResolveSkillName(intent.SkillNameKey) ?? intent.SkillNameKey;

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

        // 手触り演出の索引（行→Panel / ユニット→Panel）も盤面と運命を共にする。再描画の
        // たびに古い Panel 参照（QueueFree 済み）を掴み続けないよう、ここで必ず更地化する。
        _rowPanels.Clear();
        _unitPanels.Clear();

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

            var panelsInRow = new List<PanelContainer>();

            for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                var coordinate = new SlotCoordinate(row, column);
                var slot = BuildSlotPanel(battle, board, coordinate);
                rowGroup.AddChild(slot);
                panelsInRow.Add(slot);

                // ユニット占有マスは Id → Panel を索引し、被弾/回復ポップアップの着地点にする。
                if (board.OccupantAt(coordinate) is { } occupantId)
                {
                    _unitPanels[occupantId] = slot;
                }

                if (IsSlotTargeted(battle, row))
                {
                    targetedPanels.Add(slot);
                }
            }

            _rowPanels[row] = panelsInRow;
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

    // ─── 手触り演出: カメラシェイク（盤面ルートの位置往復・ワンショット） ──────

    /// <summary>
    /// 被弾の瞬間、盤面ルート（_rootShakeTarget）の position を減衰しながら数回往復させ、
    /// 「画面全体の揺れ（カメラシェイク）」を焚く。最後は必ず基準位置（Vector2.Zero）へ収束する。
    ///
    /// ★ ワンショット・自己完結: SetLoops を付けない単発 Tween なので、最後のジョルトを
    ///   終えると自動的に完了・破棄される。演出中フラグ等の状態は一切持たない。
    /// ★ ドリフト・多重起動の防止: 対象は永続ノードなので、直近 1 本のハンドルを保持し、
    ///   新シェイク開始前に必ず Kill して位置を Zero へ戻す（ゾンビ化・リーク防止規律）。
    /// ★ コンテナ非干渉: _rootShakeTarget の親は本 Control（非コンテナ）であり、コンテナの
    ///   レイアウト再ソートに position を奪われないため、揺れが安定して可視化される。
    /// </summary>
    private void ShakeCamera()
    {
        // 直近のシェイクが生きていれば Kill してからやり直す（多重起動・位置ドリフト根絶）。
        // 揺れの生成自体は無状態ヘルパ JuiceDirector.Shake に委譲し、本体は「永続ノードを
        // 対象に取る演出は直近 1 本のハンドルを保持して Kill する」規律だけを負う。
        KillCameraShake();
        _cameraShakeTween = JuiceDirector.Shake(
            _rootShakeTarget, CameraShakeAmplitude, CameraShakeStepSeconds);
    }

    /// <summary>
    /// 直近のカメラシェイク Tween を明示的に Kill する。新シェイク開始前・退場時に呼び、
    /// 永続ノードを掴んだ Tween の多重蓄積・位置ドリフトを断つ。
    /// </summary>
    private void KillCameraShake()
    {
        if (_cameraShakeTween is not null && _cameraShakeTween.IsValid())
        {
            _cameraShakeTween.Kill();
        }
        _cameraShakeTween = null;
    }

    // ─── 手触り演出: 被弾フラッシュ（対象行 / 敵カードの modulate 赤→白・ワンショット） ──

    /// <summary>
    /// 予告対象行（= 着弾行）の 3 マスを一斉にフラッシュさせる。色は予告赤枠と同一の
    /// <see cref="DangerFrameColor"/> で、「予告された行が、まさにその行へ着弾した」因果を色で結ぶ。
    /// </summary>
    private void FlashRow(SquadRow row)
    {
        if (!_rowPanels.TryGetValue(row, out var panels)) return;
        foreach (var panel in panels)
        {
            FlashControl(panel, DangerFrameColor);
        }
    }

    /// <summary>
    /// ノードの modulate を一瞬 flashColor に染め、白（恒等）へワンショットでフェードして戻す。
    /// modulate は self_modulate（危険脈動が使う別プロパティ）と独立なので、脈動と衝突しない。
    ///
    /// ★ リーク防止: Tween は対象ノードへバインドされる（Node.CreateTween）。対象 Panel が
    ///   次の再描画で QueueFree される際、この Tween も自動失効する（明示台帳は不要）。
    /// </summary>
    private static void FlashControl(Control? node, Color flashColor)
        => JuiceDirector.Flash(node, flashColor, RowFlashSeconds);

    // ─── 手触り演出: 自己崩壊型ダメージポップアップ（生成→上昇フェード→自己 QueueFree） ──

    /// <summary>ユニット Id からそのマス Panel を引き、その上にポップアップを跳ね上げる。</summary>
    private void SpawnDamagePopupForUnit(Guid unitId, string text, Color color)
    {
        if (_unitPanels.TryGetValue(unitId, out var panel))
        {
            SpawnDamagePopup(panel, text, color);
        }
    }

    /// <summary>
    /// ③ 撃破された英霊のマス Panel を引き、低アルファのセピアへ沈める死亡退場フェードを焚く。
    /// Tween は Panel 自身へバインドされるため、次の RenderBoard で Panel が QueueFree される際に
    /// 自動失効する（明示台帳は不要・リーク/ゾンビ化なし）。
    /// </summary>
    private void FadeUnitToDeath(Guid unitId)
    {
        if (_unitPanels.TryGetValue(unitId, out var panel))
        {
            JuiceDirector.FadeToDeath(panel, DeathFadeTint, DeathFadeSeconds);
        }
    }

    /// <summary>
    /// アンカー（被弾マス・敵カード等）の上空に軽量 Label を生成し、上方へ跳ね上げつつ
    /// アルファを 0 へフェードさせ、Tween 完了時に自分自身を QueueFree する自己崩壊型演出。
    ///
    /// ★ 自己崩壊ライフサイクル（リーク・ゾンビ化の構造的根絶）:
    ///   Tween は Label 自身へバインドされ、最終ステップの TweenCallback が当の Label を
    ///   QueueFree する。これにより BattleUI が何度再描画されても演出ノードが残留しない。
    ///   万一、親（_popupLayer）の cascade 解放が先に走っても、Label の解放で Tween は
    ///   自動失効し、コールバックは IsInstanceValid ガードにより安全に no-op となる（二重解放なし）。
    /// ★ 無状態: 生成・消滅は Tween に完結し、UI 側は演出中フラグ等を一切持たない。
    /// </summary>
    private void SpawnDamagePopup(Control? anchor, string text, Color color)
    {
        // 生成・上昇フェード・自己 QueueFree の規律は無状態ヘルパ JuiceDirector.RisingPopup に
        // 委譲する。本体は testid の連番（battle-damage-popup-{n}）だけを払い出して渡す（① 準拠の
        // 表示テキスト・testid 組み立ては呼び出し側＝本体の責務）。
        JuiceDirector.RisingPopup(
            _popupLayer, anchor, text, color,
            TestIdMetaKey, $"battle-damage-popup-{_popupSpawnCount}",
            DamagePopupRiseDistance, DamagePopupLifeSeconds);
        _popupSpawnCount++;
    }

    // ─── ② ローテーション滑走（旧位置 → 新位置へ各マスを position 補間） ─────────

    /// <summary>
    /// 現在 tree にある各ユニットマスの画面上 global 位置を控える。回転解決の【前】に呼び、
    /// 再構築後の新マスを「元居た場所」から滑り込ませるための始点として使う。
    /// </summary>
    private Dictionary<Guid, Vector2> CaptureUnitGlobalPositions()
    {
        var snapshot = new Dictionary<Guid, Vector2>();
        foreach (var pair in _unitPanels)
        {
            if (GodotObject.IsInstanceValid(pair.Value))
            {
                snapshot[pair.Key] = pair.Value.GlobalPosition;
            }
        }
        return snapshot;
    }

    /// <summary>
    /// 再構築済みの盤面に対し、各ユニットマスを旧 global 位置から現在のレイアウト位置へ
    /// 滑り込ませる。コンテナの sort（レイアウト確定）は AddChild と同フレームでは完了せず
    /// 遅延するため、1 プロセスフレーム待ってマスが最終位置を報告できる状態にしてから焚く。
    ///
    /// ★ ライフサイクル安全（Req 4）: await の前後で this / 各マスの IsInstanceValid を検査し、
    ///   待機中に画面（BattleUI）やマスが破棄されても安全に no-op する。滑走 Tween は各マス
    ///   自身へバインドされるため、次の RenderBoard でマスが QueueFree される際に自動失効する。
    /// </summary>
    private async void PlayRotationSlide(Dictionary<Guid, Vector2> previousGlobalPositions)
    {
        if (!GodotObject.IsInstanceValid(this)) return;

        // コンテナ sort の確定を 1 フレーム待つ（新マスが最終 global 位置を報告できるように）。
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (!GodotObject.IsInstanceValid(this)) return;

        foreach (var pair in _unitPanels)
        {
            var panel = pair.Value;
            if (!GodotObject.IsInstanceValid(panel)) continue;
            if (!previousGlobalPositions.TryGetValue(pair.Key, out var oldGlobal)) continue;

            // 動いていないマス（同じ場所に留まったユニット）は滑走させない。
            if (oldGlobal.DistanceSquaredTo(panel.GlobalPosition) < RotationSlideMinTravelSquared) continue;

            JuiceDirector.SlideTo(panel, oldGlobal, RotationSlideSeconds);
        }
    }

    // ─── コマンド操作（すべて ChronicleGlobal の API を呼ぶだけ） ─────────

    /// <summary>
    /// 戦闘開始: 現在年に応じた時代スケール敵を「正本ファクトリ」CreateCurrentYearEnemy で生成し、
    /// StartBattle を依頼する。章ボス出現年（25/50/75/100）はその章の章ボス、それ以外は通常敵が、
    /// 100 年史の難易度曲線（章の難易度・環境補正＋個体差ジッタ）を纏って戦場へ立つ。
    /// 戦闘の真実差し替え・再描画は BattleChanged 経由で自動的に行われる（単方向フロー・無状態）。
    /// </summary>
    private void OnStartPressed()
    {
        if (_chronicleGlobal is null) return;

        // デモ用固定敵（旧 ScaleTrialGuardian 仮置き）を排し、歴史の年数を貫いた時代スケール敵を生成。
        var enemy = _chronicleGlobal.CreateCurrentYearEnemy();

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

        // ② ローテーション滑走の種: 回転作戦を伴うときだけ、解決の【前】に各ユニットの
        //    現在の画面上 global 位置を控えておく。解決すると BattleChanged 経由で盤面が
        //    丸ごと再構築（新しい配置のマスへ）されるため、旧位置はここでしか掴めない。
        var preRotationPositions = rotation is null
            ? null
            : CaptureUnitGlobalPositions();

        var events = _chronicleGlobal.ResolveBattleTurn(rotation);

        // 盤面はこの時点で BattleChanged → RenderBoard により新配置へ再構築済み。
        // 控えた旧位置から新位置へ各マスを滑り込ませる（コンテナ sort 確定を 1 フレーム待つ）。
        if (preRotationPositions is { Count: > 0 })
        {
            PlayRotationSlide(preRotationPositions);
        }

        AppendTurnEvents(events);
    }

    /// <summary>
    /// 戦闘終了 → とどめの儀式 → 戦果決算 → 拠点への帰還（ゲームループの結節点・三段フロー）。
    ///
    /// ★ 三段フローの規律（戦闘 → とどめ → 決算 → 世代交代）:
    ///   まず EndBattle で戦闘後の参加者複製（戦死・成長・装備変化）を正本ロスタへ書き戻し、
    ///   非戦闘状態へ戻す。⚠️ここでは戦果決算をまだ確定しない（統合台帳化のため遅延算出）。
    ///   「勝敗が確定（BattalionVictory / BattalionDefeat）」かつ「現在フェーズが Battle」の
    ///   ときは、まず とどめの儀式（LastHitCeremonyScreen）を前面展開する。儀式が
    ///   Confirmed を発火したら FinalizeBattleSpoils で統合台帳を確定し、戦果決算スクリーン
    ///   （BattleSpoilsScreen）を前面展開する。AdvancePhase は決算の「次代へ（OK）」押下
    ///   ハンドラ（OnSpoilsConfirmed）で初めて駆動する。
    ///
    /// ★ なぜ「とどめ → 決算 → AdvancePhase」の順序か:
    ///   とどめ（ResolveLastHit）はラストヒットを取った 1 名へ昇級・装備進化/Lv5 破壊・
    ///   強奪を刻む。これを EndBattle 後・決算確定前に挟むことで、戦果決算（統合台帳）が
    ///   「戦闘中の成長」と「とどめの成長/喪失」の両方を 1 枚に合算できる。さらに
    ///   AdvancePhase → AdvanceGenerationLocked は加齢 → 完全ロストの最終仕分け → 盤面の
    ///   自動掃き出し（消滅副作用）を芋づる式に起こすため、死者を含む戦果は消滅副作用の
    ///   前に確定済み LastBattleSpoils から提示しきる必要がある。
    ///
    /// ★ 撤退（未決着）との区別:
    ///   まだ決着していない（Ongoing）状態での終了は「途中撤退」とみなし、戦闘状態の
    ///   クリアだけに留めて儀式も決算も提示せずフェーズも進めない（世代交代を起こさない）。
    ///
    /// ★ Tween ライフサイクルの安全（脈動リーク防止）:
    ///   EndBattle は _stateLock を取得・解放した後 BattleChanged を同期発火する。これを
    ///   受けて RenderAll が走り、その【冒頭】の KillDangerPulses が危険スロット赤枠の
    ///   脈動 Tween を全 Kill する。したがってモーダルを前面展開する時点で盤面は既に脈動
    ///   クリーンであり、以後 AdvancePhase で画面を切り替えてもゾンビ Tween は残らない。
    /// </summary>
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

        // 勝敗が確定した戦闘のみ三段フローへ進む。現在フェーズが Battle であることを併せて
        // 確認し、非戦闘フェーズからの誤起動を防ぐ。まずは とどめの儀式を前面展開する。
        var isConcluded = outcome is BattleOutcome.BattalionVictory or BattleOutcome.BattalionDefeat;
        if (isConcluded && _chronicleGlobal.CurrentPhase == GamePhase.Battle)
        {
            ShowLastHitCeremony();
        }
    }

    /// <summary>
    /// 「運命の帯」ボタン押下ハンドラ。予言タイムラインオーバーレイを開く意思表示
    /// （<see cref="ProphecyRequested"/>）を 1 度だけ発火する。本 UI はオーバーレイを自分で
    /// 生成せず、購読側 GameDirector に生死を委ねる（無状態の徹底・単方向の堅持）。
    /// </summary>
    private void OnProphecyPressed() => ProphecyRequested?.Invoke();

    /// <summary>
    /// とどめの儀式（三段フロー第 2 段）を前面展開する。
    ///
    /// 無状態の LastHitCeremonyScreen を新規生成して子に加える。スクリーンは生存者から
    /// 1 名を必ず選ばせて ChronicleGlobal.ResolveLastHit を内部で解決し、見届けると
    /// Confirmed を 1 度だけ発火する。多重起動・取り残しを避けるため、生存中の旧モーダルが
    /// あれば先に確実に解放してから展開する。
    /// </summary>
    private void ShowLastHitCeremony()
    {
        // 旧モーダルが取り残されていれば購読を解いて解放（多重展開・リーク防止）。
        DismissLastHitCeremony();

        var screen = new LastHitCeremonyScreen();
        screen.Confirmed += OnLastHitConfirmed;
        _lastHitScreen = screen;
        AddChild(screen);
    }

    /// <summary>
    /// とどめの儀式「戦果へ」押下ハンドラ（三段フロー第 2 段 → 第 3 段の結節点）。
    ///
    /// 儀式（ResolveLastHit）が済んだこの時点で、正本ロスタには「戦闘中の成長」も
    /// 「とどめの成長/喪失」も両方が刻まれている。ここで FinalizeBattleSpoils を呼び、
    /// 開戦時 × とどめ完了後ロスタの差分を統合台帳として確定してから、戦果決算スクリーンを
    /// 前面展開する。儀式スクリーンは Confirmed を高々 1 度しか発火しない（自身で QueueFree
    /// 済み）。本体側も CurrentPhase == Battle を再確認してから進める。
    /// </summary>
    private void OnLastHitConfirmed()
    {
        // スクリーンは自身で QueueFree 済み。参照だけ手放す（購読も同時に失効する）。
        _lastHitScreen = null;

        if (_chronicleGlobal is null) return;
        if (_chronicleGlobal.CurrentPhase != GamePhase.Battle) return;

        // 統合台帳を確定（開戦時 × とどめ完了後ロスタの差分）してから決算を提示する。
        _chronicleGlobal.FinalizeBattleSpoils();
        ShowSpoilsScreen();
    }

    /// <summary>
    /// 前面展開中の とどめの儀式モーダルがあれば購読を解いて確実に解放する。退場時および
    /// 新規展開の直前に呼び、ゾンビノード・購読の二重接続・リークを根絶する。
    /// </summary>
    private void DismissLastHitCeremony()
    {
        if (_lastHitScreen is null) return;

        if (GodotObject.IsInstanceValid(_lastHitScreen))
        {
            _lastHitScreen.Confirmed -= OnLastHitConfirmed;
            _lastHitScreen.QueueFree();
        }
        _lastHitScreen = null;
    }

    /// <summary>
    /// 戦果決算スクリーンを前面展開する（三段フロー第 3 段）。
    ///
    /// 無状態の BattleSpoilsScreen を新規生成して子に加える。スクリーンは
    /// ChronicleGlobal.LastBattleSpoils を一方向に読むだけで自分では状態を持たない。
    /// 多重起動・前回モーダルの取り残しを避けるため、生存中の旧モーダルがあれば先に
    /// 確実に解放してから展開する。
    /// </summary>
    private void ShowSpoilsScreen()
    {
        // 旧モーダルが取り残されていれば購読を解いて解放（多重展開・リーク防止）。
        DismissSpoilsScreen();

        var screen = new BattleSpoilsScreen();
        screen.Confirmed += OnSpoilsConfirmed;
        _spoilsScreen = screen;
        AddChild(screen);
    }

    /// <summary>
    /// 決算スクリーン「次代へ（OK）」押下ハンドラ。ここで初めて AdvancePhase を駆動し、
    /// Battle → Chronicle へ正規遷移させて世代交代（完全ロストの掃き出し・加齢・収入・
    /// 次世代予言）を一連の単方向の因果としてノンストップで走らせる。
    ///
    /// スクリーンは Confirmed を高々 1 度しか発火しない（自身で QueueFree 済み）。本体側も
    /// CurrentPhase == Battle を再確認してから前進し、二重前進を二重に防ぐ。
    /// </summary>
    private void OnSpoilsConfirmed()
    {
        // スクリーンは自身で QueueFree 済み。参照だけ手放す（購読も同時に失効する）。
        _spoilsScreen = null;

        if (_chronicleGlobal is null) return;
        if (_chronicleGlobal.CurrentPhase != GamePhase.Battle) return;

        var nextPhase = _chronicleGlobal.AdvancePhase();
        AppendLogLine(
            $"拠点へ帰還: フェーズを {nextPhase} へ前進（世代交代を適用）",
            "battle-log-return-to-base");
    }

    /// <summary>
    /// 前面展開中の決算モーダルがあれば購読を解いて確実に解放する。退場時および
    /// 新規展開の直前に呼び、ゾンビノード・購読の二重接続・リークを根絶する。
    /// </summary>
    private void DismissSpoilsScreen()
    {
        if (_spoilsScreen is null) return;

        if (GodotObject.IsInstanceValid(_spoilsScreen))
        {
            _spoilsScreen.Confirmed -= OnSpoilsConfirmed;
            _spoilsScreen.QueueFree();
        }
        _spoilsScreen = null;
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
    /// 1 ターンのイベントログを発生順に走査し、各イベントを 1 行のテキストへ落としつつ、
    /// 種別に応じた手触り演出（被弾フラッシュ・自己崩壊ポップアップ）をワンショット Tween で
    /// 焚く。盤面・HP・敵カードの再描画は別途 BattleChanged 経由の RenderAll が済ませている
    /// （SafeEmit は同期発火のため、本メソッド到達時には _rowPanels / _unitPanels は最新）。
    ///
    /// ★ カメラシェイクは「敵対的着弾が 1 つでもあったターン」に高々 1 回だけ焚く。
    ///   全戦線強襲（TotalAssault）で複数行が同時被弾しても、画面の揺れは 1 回に集約して
    ///   過剰な振動を避ける（演出は強いが、うるさくしない）。
    ///
    /// ★ 状態を持たない: 演出中フラグの類は一切持たず、各 Tween が自己完結・自動消滅する。
    ///   ポップアップは完了時に自分自身を QueueFree し、フラッシュは Panel が次の再描画で
    ///   QueueFree される際に Tween ごと自動失効する（リーク・ゾンビ化の構造的根絶）。
    /// </summary>
    private void AppendTurnEvents(ImmutableArray<BattleEvent> events)
    {
        if (events.IsDefaultOrEmpty) return;

        var hostileImpactOccurred = false;

        foreach (var battleEvent in events)
        {
            AppendLogLine(DescribeEvent(battleEvent), $"battle-log-entry-{_logEntryCount}");
            hostileImpactOccurred |= PlayEventJuice(battleEvent);
        }

        // 大隊側への着弾があったターンだけ、画面全体を 1 回だけ揺らす。
        if (hostileImpactOccurred)
        {
            ShakeCamera();
        }
    }

    /// <summary>
    /// 1 戦闘イベントに対応する手触り演出（フラッシュ / ポップアップ）を焚く。
    /// 戻り値は「このイベントが大隊側への敵対的着弾か（= カメラシェイク対象か）」を表す。
    ///
    /// ★ 予告→着弾の色の一致: EnemyOffenseEvent の対象行フラッシュは、予告で脈動していた
    ///   赤枠と同じ <see cref="DangerFrameColor"/> で焚く（Req 4 の整合点）。
    /// ★ ① 準拠: ポップアップに載るのは数値と chrome 記号（✖ / ★）のみで「データ名」は出さない。
    /// </summary>
    private bool PlayEventJuice(BattleEvent battleEvent)
    {
        switch (battleEvent)
        {
            case AllyOffenseEvent ally:
                // 味方の攻撃が敵へ命中 → 敵カードを赤くフラッシュし、敵カード上にダメージを出す。
                FlashControl(_enemyCard, DangerFrameColor);
                SpawnDamagePopup(_enemyCard, $"-{ally.Damage}", DamagePopupColor);
                return false;

            case EnemyOffenseEvent enemy:
                // 敵の攻撃が対象行へ着弾 → 行全体を予告と同じ赤でフラッシュ（カメラシェイク対象）。
                FlashRow(enemy.TargetRow);
                // ④ 予告ワインドアップ: 着弾の瞬間に敵カードをスケールパンチさせ「敵が振り抜いた」
                //    手応えを出す。scale は modulate（フラッシュ）とも position（シェイク）とも独立。
                JuiceDirector.Punch(_enemyCard, EnemyWindupPunchScale, EnemyWindupPunchSeconds);
                return true;

            case UnitDamagedEvent damaged:
                // 個々の被弾 → 当人のマス上に被ダメージを跳ね上げる（カメラシェイク対象）。
                SpawnDamagePopupForUnit(damaged.UnitId, $"-{damaged.Damage}", DamagePopupColor);
                return true;

            case UnitDefeatedEvent defeated:
                // 撃破（完全ロスト）→ 当人のマス上に撃破記号を出す（カメラシェイク対象）。
                SpawnDamagePopupForUnit(defeated.UnitId, DefeatPopupMark, DamagePopupColor);
                // ③ 英霊の死亡退場: 当人のカードを低アルファのセピアへ沈め、とどめの儀式へ移る前に
                //    「褪せて退場する」気配を残す。FadeToDeath は被弾フラッシュ（同じ modulate）の
                //    後に焚かれるため、より長い本フェードが最終的に勝ち、セピアで静止する。
                FadeUnitToDeath(defeated.UnitId);
                return true;

            case UnitHealedEvent healed:
                // 回復 → 当人のマス上に回復量を緑で跳ね上げる（敵対着弾ではないので揺らさない）。
                SpawnDamagePopupForUnit(healed.UnitId, $"+{healed.HealAmount}", HealPopupColor);
                return false;

            case LastHitResolvedEvent lastHit:
                // とどめ成立 → とどめを刺した当人のマス上に金の星を出す（祝福。揺らさない）。
                SpawnDamagePopupForUnit(lastHit.UnitId, LastHitPopupMark, LastHitPopupColor);
                return false;

            default:
                // ローテーション・決着など、盤面着弾を伴わないイベントは演出なし。
                return false;
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
