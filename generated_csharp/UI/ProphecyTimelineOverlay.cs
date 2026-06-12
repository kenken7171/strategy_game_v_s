// =============================================================================
//  ChronicleKnights — ProphecyTimelineOverlay.cs
// -----------------------------------------------------------------------------
//  敵の次手（運命の帯・未来の横軸）を最前面へ overlay する、完全無状態の純粋ビュー。
//
//  ★ 何を映すのか（時の横軸）:
//    現在戦闘中の敵が、この先 T+1 / T+2 / T+3 … に放つ予定の攻撃予告（AttackIntent）を、
//    左（手前 = 確度が高い）から右（奥 = 遠い未来 = 不確か）へ水平タイムラインに並べて描く。
//    予測の抽選は Godot 非依存の純粋ファクトリ AttackIntentRoller.Forecast に委ね、その入口は
//    SoT（ChronicleGlobal.ForecastEnemyIntents）が決定論シードを織って提供する。本クラスは
//    「読み直して描く」ことだけに徹する（脳と身体の分離）。
//
//  ★ 完全な無状態（設計憲法 ③・単一 SoT・単方向データフロー）:
//    本画面は予言データを 1 ミリもキャッシュ（複製保持）しない。再描画のたびに
//    ChronicleGlobal.ForecastEnemyIntents(LookaheadTurns) をその場で読み直して帯を組み直す。
//    戦況更新（BattleChanged）・世界初期化（StateInitialized）のシグナルで自動的に再描画され、
//    ターンが進めば帯も前進する。常に SoT の最新の真実だけを映す。
//
//  ★ 距離不確実性（confidence-fade）:
//    遠い未来のカードほど、ループインデックスに基づいて Modulate.A（アルファ）を逓減させて
//    霞ませる。「遠い予言ほど不確かである」という距離感をビジュアルとして正直に表現する。
//    Modulate は子へ乗算伝播するため、カード全体（種別・スキル名・威力・対象盤面）がまとめて褪色する。
//
//  ★ リークフリー規律（メモリリーク・取り残しノードの構造的根絶 — 要件 2）:
//    生成したカード・空表示などの盤面ノード（_timelineNodes）はすべて台帳へ記録し、再描画
//    （RenderAll）の冒頭と退場（_ExitTree）で例外なく一括 QueueFree して「更地化」する。
//    カードのサブツリー（余白・列・ラベル・盤面セル）は親カードの QueueFree で芋づる式に解放される。
//
//  ★ 堅牢性（範囲外・破綻の不在 — 要件 3）:
//    予測配列が空（非戦闘・lookahead 0 以下）でも空表示へ穏当に倒し、添字アクセスは行わず
//    foreach の連番のみで描くため、IndexOutOfRangeException は原理的に生じない。LookaheadTurns は
//    描画前に [0, AttackIntentRoller.MaxForecastTurns] へクランプする（過剰確保の構造的封じ込め）。
//
//  ★ 設計憲法①（日本語データ名のハードコード禁止）:
//    カードに載る敵スキル名は ChronicleGlobal.ResolveSkillName（localization 経由）で解決する。
//    ここで直書きするのは時間軸ラベル（T+1 等）・パターン種別・対象行という UI クローム語のみで、
//    データ名は持たない。testid はすべて機械生成の ASCII（prophecy-timeline-root 等）。
//
//  ★ 次段（第2段・後半）への布石:
//    カード中心座標（_cardCenters）を本描画で控えておく。後段で隣接ターン間へ Line2D「時間の矢」を
//    張り、JuiceDirector.GrowLine で手前から奥へ伸ばす開通演出を、この台帳の座標から無改修で叩ける。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 現在戦闘中の敵の N ターン先の攻撃予告（運命の帯）を水平タイムラインへ描く無状態オーバーレイ。
/// SoT をキャッシュせず、開かれるたび・戦況が動くたびに予測を読み直して描き直す。
/// </summary>
public partial class ProphecyTimelineOverlay : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>ヘッダー帯の高さ（カード配置の上端基準）。</summary>
    private const float HeaderHeight = 72.0f;

    /// <summary>盤面の上下パディング（ヘッダー下・画面下端の余白）。</summary>
    private const float BoardPadding = 16.0f;

    /// <summary>先読みターン数の既定値（マウント側が上書き可能）。</summary>
    private const int DefaultLookaheadTurns = 4;

    /// <summary>1 枚の予告カードの寸法（ミニ盤面を載せるため縦に余裕を持たせる）。</summary>
    private static readonly Vector2 CardSize = new(212.0f, 188.0f);

    /// <summary>ミニ盤面 1 セルの寸法（前衛 / 後左 / 後右の 3 マス）。</summary>
    private static readonly Vector2 BoardCellSize = new(58.0f, 34.0f);

    /// <summary>confidence-fade の 1 ターンあたりの褪色量（T+1 を 1.0 とし以降逓減）。</summary>
    private const float ConfidenceFadePerTurn = 0.16f;

    /// <summary>confidence-fade の下限アルファ（遠い未来でも完全には消さない）。</summary>
    private const float MinConfidenceAlpha = 0.34f;

    /// <summary>時間の矢（Line2D コネクタ）の太さ（px）。</summary>
    private const float ConnectorWidth = 3.0f;

    /// <summary>時間の矢 1 本が手前から奥へ伸び切るまでの秒数（GrowLine の補間時間）。</summary>
    private const double ConnectorGrowSeconds = 0.45;

    /// <summary>
    /// 時間の矢の段階遅延（stagger）1 段あたりの秒数。手前（T+1→T+2）の矢を先に、奥の矢ほど
    /// 遅らせて開通させ、「現在から未来へ運命が流れる」方向性を体感させる。
    /// </summary>
    private const double ConnectorGrowStaggerSeconds = 0.12;

    /// <summary>暗幕の色（半透明の深紫紺。背後の戦闘画面を沈めて未来予測へ集中させる）。</summary>
    private static readonly Color BackdropColor = new(0.05f, 0.04f, 0.10f, 0.93f);

    /// <summary>時間軸を象徴するアクセント色（青紫の燐光）。</summary>
    private static readonly Color AxisAccentColor = new(0.58f, 0.72f, 0.98f);

    /// <summary>対象（被攻撃）行セルの色（危険を示す赤）。</summary>
    private static readonly Color TargetedRowColor = new(0.78f, 0.22f, 0.22f);

    /// <summary>非対象（安全）行セルの色（沈めた灰青）。</summary>
    private static readonly Color SafeRowColor = new(0.16f, 0.18f, 0.24f);

    // ─── 外部 API（マウント側 = GameDirector が AddChild 前に設定する） ─────

    /// <summary>何ターン先まで読むか。AddChild 前に設定する（無状態の徹底）。</summary>
    public int LookaheadTurns { get; set; } = DefaultLookaheadTurns;

    /// <summary>閉じる意思表示。閉じるボタン押下・Esc で 1 度発火し、解放は購読側が引く。</summary>
    public event Action? CloseRequested;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    /// <summary>タイムラインの盤面（非コンテナ Control）。カードを絶対座標で並べる土台。</summary>
    private Control? _canvas;

    /// <summary>ヘッダーの副題（帯の長さ・確度の文脈を示すクロームラベル）。</summary>
    private Label? _subtitleLabel;

    // ─── リークフリー台帳（更地化の対象） ─────────────────────────────────

    /// <summary>生成したカード・空表示などの盤面ノード台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _timelineNodes = new();

    /// <summary>カード中心座標（_canvas ローカル）。後段の「時間の矢」端点の引き当て用。再描画で更地化。</summary>
    private readonly Dictionary<int, Vector2> _cardCenters = new();

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;   // 背後の戦闘画面への入力を遮断（overlay）
        SetMeta(TestIdMetaKey, "prophecy-timeline-root");

        BuildChrome();
        SubscribeSignals();
        RenderAll();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
        ClearTimeline();   // 退場時に台帳を更地化（取り残しノードの根絶）
    }

    /// <summary>Esc（ui_cancel）で閉じる。クローズの意思表示を発火し、解放は購読側に委ねる。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not null && @event.IsActionPressed("ui_cancel"))
        {
            CloseRequested?.Invoke();
            GetViewport()?.SetInputAsHandled();
        }
    }

    // ─── 不変クローム構築（暗幕 + ヘッダー + 盤面の土台） ──────────────────

    private void BuildChrome()
    {
        // ── 暗幕（フルレクト・入力遮断・背後を沈める） ────────────────
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "prophecy-timeline-backdrop");
        AddChild(backdrop);

        // ── 盤面の土台（非コンテナ・フルレクト。カードを絶対座標で並べる） ──
        //   入力は素通り（Ignore）させ、最前面のヘッダーのボタンを妨げない。
        _canvas = new Control();
        _canvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _canvas.MouseFilter = MouseFilterEnum.Ignore;
        _canvas.SetMeta(TestIdMetaKey, "prophecy-timeline-canvas");
        AddChild(_canvas);

        // ── ヘッダー（題字 + 副題 + 閉じるボタン） ───────────────────
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        header.OffsetLeft   = 16;
        header.OffsetRight  = -16;
        header.OffsetTop    = 12;
        header.OffsetBottom = HeaderHeight;
        header.SetMeta(TestIdMetaKey, "prophecy-timeline-header");
        AddChild(header);

        var title = new Label { Text = "🔮 運命の帯 ― 未来の横軸" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", AxisAccentColor);
        title.SetMeta(TestIdMetaKey, "prophecy-timeline-title");
        header.AddChild(title);

        _subtitleLabel = new Label { Text = string.Empty };
        _subtitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _subtitleLabel.VerticalAlignment = VerticalAlignment.Center;
        _subtitleLabel.SetMeta(TestIdMetaKey, "prophecy-timeline-subtitle");
        header.AddChild(_subtitleLabel);

        var closeButton = new Button { Text = "✕ 閉じる", CustomMinimumSize = new Vector2(120, 40) };
        closeButton.Pressed += OnClosePressed;
        closeButton.SetMeta(TestIdMetaKey, "prophecy-timeline-close-button");
        header.AddChild(closeButton);
    }

    // ─── シグナル購読 / 解除（SoT 変更に追従して無状態に描き直す） ─────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.BattleChanged    += OnBattleChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.BattleChanged    -= OnBattleChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    private void OnBattleChanged() => RenderAll();
    private void OnStateInitialized() => RenderAll();

    private void OnClosePressed() => CloseRequested?.Invoke();

    // ─── 更地化（カード・空表示を一括解放） ───────────────────────────────

    /// <summary>
    /// 盤面ノードの台帳をすべて解放して更地に戻す。再描画（RenderAll）の冒頭と退場（_ExitTree）で
    /// 必ず呼び、取り残しノードを構造的に根絶する。カードのサブツリーは親の QueueFree で芋づる解放。
    /// </summary>
    private void ClearTimeline()
    {
        foreach (var node in _timelineNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
        _timelineNodes.Clear();
        _cardCenters.Clear();
    }

    // ─── 描画（SoT をその場で読み直し、運命の帯を組み直す） ─────────────────

    /// <summary>
    /// 予測の帯を SoT からその場で読み直し、水平タイムラインへ描く。冒頭で必ず更地化してから
    /// 描くため、再描画でもカードが累積しない（無状態・リークフリー）。境界は穏当に空表示へ倒す。
    /// </summary>
    private void RenderAll()
    {
        ClearTimeline();   // 何より先に更地化（多重描画・リークの構造的封じ込め）

        if (_canvas is null) return;

        // SoT が未取得（Autoload 不在）なら、安全に空表示へ倒す。
        if (_chronicleGlobal is null)
        {
            if (_subtitleLabel is not null) _subtitleLabel.Text = string.Empty;
            RenderEmpty("（予言データを取得できません）");
            return;
        }

        // 番兵: 過大値・非正値をクランプ（Forecast 側でも守られるが UI 側でも二重に倒す）。
        var turns = Math.Clamp(LookaheadTurns, 0, AttackIntentRoller.MaxForecastTurns);

        // SoT からその場で手繰り寄せる（キャッシュしない・決定論）。
        var band = _chronicleGlobal.ForecastEnemyIntents(turns);

        if (band.Count == 0)
        {
            if (_subtitleLabel is not null) _subtitleLabel.Text = string.Empty;
            RenderEmpty("（いま読むべき敵の運命はありません）");
            return;
        }

        if (_subtitleLabel is not null)
        {
            _subtitleLabel.Text = $"この先 {band.Count} ターンの敵の手筋（左ほど確度が高い）";
        }

        LayoutCards(band);
    }

    /// <summary>
    /// 帯を水平方向に等間隔で並べる。各カードは画面幅を人数 +1 で割った位置に中央を置き、Y は
    /// 盤面中央に揃える。配置と同時にカード中心を _cardCenters へ控え、後段の「時間の矢」端点に使う。
    /// </summary>
    private void LayoutCards(IReadOnlyList<AttackIntent> band)
    {
        if (_canvas is null) return;

        var viewportSize = GetViewportRect().Size;
        float width  = viewportSize.X > 1.0f ? viewportSize.X : 1280.0f;
        float height = viewportSize.Y > 1.0f ? viewportSize.Y : 720.0f;

        float boardTop = HeaderHeight + BoardPadding;
        float usableHeight = Mathf.Max(height - boardTop - BoardPadding, CardSize.Y);
        float centerY = boardTop + usableHeight * 0.5f;

        for (int i = 0; i < band.Count; i++)
        {
            float centerX = width * (i + 1) / (band.Count + 1);
            var center = new Vector2(centerX, centerY);
            _cardCenters[i] = center;

            float alpha = Mathf.Max(MinConfidenceAlpha, 1.0f - i * ConfidenceFadePerTurn);
            SpawnCard(i, band[i], center, alpha);
        }

        // 全カードの中心が _cardCenters に出揃った後で、隣接ターン間へ「時間の矢」を張る。
        // （カード生成より後に呼ぶことで端点参照が必ず引ける。台帳カウントで境界を守る。）
        GrowTimeArrows(band.Count);
    }

    /// <summary>
    /// 隣接するターンカード（T+i から T+i+1）の間へ Godot.Line2D「時間の矢」を背面動的生成し、
    /// JuiceDirector.GrowLine で手前（T+1）から奥（T+N）へ段階遅延つきに伸ばす開通演出を焚く。
    ///
    /// ★ 背面配置: ZIndex を -1 にし、さらに MoveChild で描画順を最背面へ送る二重保証で、
    ///   矢が予告カードの視認性を 1 ミリも損なわないようにする。色は時間軸の意味論を統一する
    ///   AxisAccentColor（青紫の燐光）。
    /// ★ 時間の方向性: ループインデックス i に基づく段階遅延（i * stagger）で、現在から未来へ
    ///   運命の線が次々に開通していく感覚を体感させる。
    /// ★ 未来の霞み: 矢の Modulate.A を「繋ぐ先（より遠い）カード」のアルファに合わせて逓減させ、
    ///   カードの confidence-fade と色価言語を完全同調させる。
    /// ★ 境界ガード: 反復は台帳カウント基準（i &lt; cardCount - 1）。カードが 1 枚以下なら矢は
    ///   1 本も張られない（KeyNotFoundException・宙吊り線は原理的に生じない）。端点は TryGetValue で
    ///   防御的に引き、欠落時は静かに飛ばす。
    /// ★ リークフリー: 生成した各 Line2D は既存台帳 _timelineNodes へ登録し、再描画冒頭・退場時の
    ///   ClearTimeline で一括 QueueFree される。GrowLine の Tween は当の Line2D 自身へバインドされる
    ///   ため、Line2D の解放で自動失効する（新たなビュー状態を一切増設しない）。
    /// </summary>
    private void GrowTimeArrows(int cardCount)
    {
        if (_canvas is null) return;

        // 台帳カウント基準の安全境界。隣接ペアが作れない（カード 1 枚以下）なら何も張らない。
        for (int i = 0; i < cardCount - 1; i++)
        {
            if (!_cardCenters.TryGetValue(i, out var fromCenter)) continue;
            if (!_cardCenters.TryGetValue(i + 1, out var toCenter)) continue;

            // 手前カードの右端中央 → 奥カードの左端中央（全カードは同じ centerY ゆえ水平な一本鎖）。
            var from = new Vector2(fromCenter.X + CardSize.X * 0.5f, fromCenter.Y);
            var to   = new Vector2(toCenter.X   - CardSize.X * 0.5f, toCenter.Y);

            // 繋ぐ先（i+1）の confidence-fade に合わせて矢自体も霞ませる（色価言語の同調）。
            float arrowAlpha = Mathf.Max(MinConfidenceAlpha, 1.0f - (i + 1) * ConfidenceFadePerTurn);

            var connector = new Line2D
            {
                Width        = ConnectorWidth,
                DefaultColor = AxisAccentColor,
                ZIndex       = -1,                              // カード（既定 Z=0）より背面へ
                Modulate     = new Color(1.0f, 1.0f, 1.0f, arrowAlpha), // 未来ほど霞む
                Points       = new[] { from, from },            // 長さ 0 から開始（GrowLine が伸ばす）
            };
            connector.SetMeta(TestIdMetaKey, $"prophecy-timeline-connector-{i + 1}-{i + 2}");
            _canvas.AddChild(connector);
            // Z だけに頼らず、描画順でもカードの背面へ確実に送る（二重保証）。
            _canvas.MoveChild(connector, 0);
            _timelineNodes.Add(connector);   // 台帳へ登録（更地化で一括解放）

            // 手前から奥へ段階遅延。Tween は connector 自身へバインドされ、解放で自動失効するため
            // 戻り値は保持しない（新たなビュー状態を増設しない＝SoT 不増設の徹底）。
            double delaySeconds = i * ConnectorGrowStaggerSeconds;
            JuiceDirector.GrowLine(connector, from, to, ConnectorGrowSeconds, delaySeconds);
        }
    }

    /// <summary>
    /// 1 ターン分の予告カードを中心座標から配置する。confidence-fade（遠いほど褪色）を Modulate へ
    /// 乗せ、種別・スキル名（localization 解決）・威力・対象ミニ盤面を縦に積む。
    /// </summary>
    private void SpawnCard(int turnIndex, AttackIntent intent, Vector2 center, float alpha)
    {
        if (_canvas is null) return;

        int turn = turnIndex + 1; // 表示は 1 始まり（T+1, T+2, ...）

        var card = new Panel
        {
            Position          = center - CardSize * 0.5f,
            Size              = CardSize,
            CustomMinimumSize = CardSize,
            MouseFilter       = MouseFilterEnum.Ignore,
            Modulate          = new Color(1.0f, 1.0f, 1.0f, alpha), // 距離不確実性（confidence-fade）
        };
        card.SetMeta(TestIdMetaKey, $"prophecy-forecast-card-{turn}");

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        card.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        margin.AddChild(column);

        // 時間軸ラベル（クローム。T+1 / T+2 …）。
        var turnLabel = new Label { Text = $"⏳ T+{turn}" };
        turnLabel.AddThemeFontSizeOverride("font_size", 16);
        turnLabel.AddThemeColorOverride("font_color", AxisAccentColor);
        turnLabel.SetMeta(TestIdMetaKey, $"prophecy-card-turn-{turn}");
        column.AddChild(turnLabel);

        // パターン種別（クローム語。データ名ではない攻撃様式の説明）。
        var kindLabel = new Label { Text = KindLabel(intent.Kind) };
        kindLabel.AddThemeFontSizeOverride("font_size", 13);
        kindLabel.SetMeta(TestIdMetaKey, $"prophecy-card-kind-{turn}");
        column.AddChild(kindLabel);

        // 敵スキル名（localization 解決・データ名は直書きしない）。
        var skillLabel = new Label { Text = ResolveSkill(intent.SkillNameKey) };
        skillLabel.SetMeta(TestIdMetaKey, $"prophecy-card-skill-{turn}");
        column.AddChild(skillLabel);

        // 威力（1 体あたりの被ダメージ）。
        var damageLabel = new Label { Text = $"⚔ {intent.DamagePerUnit}" };
        damageLabel.AddThemeFontSizeOverride("font_size", 13);
        damageLabel.SetMeta(TestIdMetaKey, $"prophecy-card-damage-{turn}");
        column.AddChild(damageLabel);

        // 対象ミニ盤面（前衛 / 後左 / 後右。被攻撃行を赤、安全行を灰青で示す）。
        var boardRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        boardRow.AddThemeConstantOverride("separation", 4);
        boardRow.SetMeta(TestIdMetaKey, $"prophecy-card-board-{turn}");
        column.AddChild(boardRow);

        foreach (var row in FormationBoard.RowOrder)
        {
            bool targeted = intent.Targets(row);
            var cell = new ColorRect
            {
                Color             = targeted ? TargetedRowColor : SafeRowColor,
                CustomMinimumSize = BoardCellSize,
                MouseFilter       = MouseFilterEnum.Ignore,
            };
            cell.SetMeta(TestIdMetaKey, $"prophecy-card-row-{turn}-{RowSlug(row)}");

            var cellLabel = new Label
            {
                Text                = RowLabel(row),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                MouseFilter         = MouseFilterEnum.Ignore,
            };
            cellLabel.AddThemeFontSizeOverride("font_size", 12);
            cellLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            cell.AddChild(cellLabel);

            boardRow.AddChild(cell);
        }

        _canvas.AddChild(card);
        _timelineNodes.Add(card);
    }

    /// <summary>
    /// 描くべき予言が無い場合の空表示（中央のクローム文言）を盤面へ置く。台帳に積んで次の
    /// 更地化で消えるようにする。
    /// </summary>
    private void RenderEmpty(string message)
    {
        if (_canvas is null) return;

        var label = new Label
        {
            Text                = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        label.SetMeta(TestIdMetaKey, "prophecy-timeline-empty");
        _canvas.AddChild(label);
        _timelineNodes.Add(label);
    }

    // ─── ローカライゼーション / クロームラベル ────────────────────────────
    //   敵スキル名は ChronicleGlobal 経由で localization 解決し、Autoload 不在時は生キーへ
    //   フォールバックして画面を落とさない（設計憲法①）。種別・行ラベルは「データ名」ではなく
    //   UI が攻撃様式・盤面位置を説明するクローム語であり、日本語直書きを許容する。

    private string ResolveSkill(string skillNameKey)
        => _chronicleGlobal?.ResolveSkillName(skillNameKey) ?? skillNameKey;

    /// <summary>攻撃パターン種別 → 表示ラベル（クローム日本語。データ名ではない攻撃様式の説明語）。</summary>
    private static string KindLabel(AttackPatternKind kind) => kind switch
    {
        AttackPatternKind.SingleStrike => "▣ 単一分隊強襲",
        AttackPatternKind.Pincer       => "▤ 複数分隊挟撃",
        AttackPatternKind.TotalAssault => "▦ 全大隊総攻撃",
        _ => "攻撃",
    };

    /// <summary>分隊行 → 表示ラベル（クローム日本語。盤面位置の説明語）。</summary>
    private static string RowLabel(SquadRow row) => row switch
    {
        SquadRow.Front     => "前衛",
        SquadRow.RearLeft  => "後左",
        SquadRow.RearRight => "後右",
        _ => "列",
    };

    /// <summary>分隊行 → testid 用 ASCII スラッグ（機械生成の足場）。</summary>
    private static string RowSlug(SquadRow row) => row switch
    {
        SquadRow.Front     => "front",
        SquadRow.RearLeft  => "rear-left",
        SquadRow.RearRight => "rear-right",
        _ => "row",
    };
}
