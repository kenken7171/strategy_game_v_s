// =============================================================================
//  ChronicleKnights — PedigreeOverlay.cs
// -----------------------------------------------------------------------------
//  家系図（血統の縦軸）を最前面へ overlay する、完全無状態の純粋ビュー。
//
//  ★ 何を映すのか（血の縦軸）:
//    指定された 1 名（TargetUnitId）を根として、SoT に刻まれた血統リンク
//    （Unit.Parentage = 父母 Id）と婚姻リンク（Unit.SpouseId = 配偶者 Id）を手繰り、
//    「祖父母世代」「父母世代」「本人・配偶者・兄弟世代」「子孫世代（子・孫）」へ
//    またがる縦系の家系ネットを 1 枚の盤面に描く。関係の抽出は Godot 非依存の純粋
//    ファクトリ PedigreeBuilder.Build に委ね、本クラスは「描く」ことだけに徹する。
//
//  ★ 完全な無状態（設計憲法 ③・単一 SoT・単方向データフロー）:
//    本画面は家系図データを 1 ミリもキャッシュ（複製保持）しない。再描画のたびに
//    ChronicleGlobal.GetPedigreeUniverse()（= 英霊アーカイブ ∪ 現役ロスタ）と現役 Id
//    集合をその場で読み直し、PedigreeBuilder へ渡してグラフを組み直す。ロスタ変更
//    （RosterChanged）・世界初期化（StateInitialized）のシグナルで自動的に再描画され、
//    常に SoT の最新の真実だけを映す。
//
//  ★ 英霊（遺影）と現役の対比:
//    既に旅団を去った（死亡・引退・解雇された）先祖も英霊アーカイブから拾い上げ、
//    カードの Modulate をセピア・半透明（MemorialTint）へ沈めて「遺影」として厳かに
//    配置する。現役（現在ロスタ在籍）は等倍（白）で生き生きと描く。Modulate は子へも
//    乗算伝播するため、カード全体（関係・氏名・ジョブ）がまとめて褪色する。
//
//  ★ 血統コネクタの開通演出（JuiceDirector の規律を応用）:
//    親カード下端 → 子カード上端へ Godot.Line2D を動的に張り、JuiceDirector.GrowLine で
//    「線が親から子へスーッと伸びていく」開通 Tween を叩く。世代帯ごとに僅かな時間差
//    （stagger）を付け、祖先から子孫へ向けて血脈が順に通電していくように見せる。
//
//  ★ リークフリー規律（メモリリーク・Tween ゾンビ化の構造的根絶 — 要件 2）:
//    生成したカード・コネクタ（_treeNodes）と補間 Tween（_growTweens）はすべて台帳へ
//    記録し、再描画（RenderTree）の冒頭と退場（_ExitTree）で例外なく一括 Kill ＆
//    QueueFree して「更地化」する。Tween はノードへバインドされ、ノード解放で自動失効
//    するが、本クラスは明示 Kill も併用して二重に安全側へ倒す。
//
//  ★ 堅牢性（多世代の肥大・サイクル・参照欠落 — 要件 3）:
//    世代の探索と重複排除・サイクル遮断・null 番兵は純粋層 PedigreeBuilder が構造的に
//    担保する（世代は -2〜+2 に限定 → スタックオーバーフロー不可能、visited で無限ループ
//    遮断、TryGetValue で参照欠落を黙ってスキップ）。本 UI 側も座標の引き当てを
//    TryGetValue で行い、両端のカードが揃ったエッジにだけ線を張る（宙吊り線を出さない）。
//
//  ★ 設計憲法①（日本語データ名のハードコード禁止）:
//    カードに載るユニット氏名・ジョブ名は ChronicleGlobal.ResolveDisplayName /
//    ResolveJobName（localization 経由）で解決する。ここで直書きするのは関係ラベル
//    （祖父母 / 本人 等）・題字・記号というクローム文字列のみで、データ名は持たない。
//    testid はすべて機械生成の ASCII（pedigree-overlay-root 等）。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Pedigree;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 指定ユニットを根とする家系図（血統の縦軸）を最前面へ描く無状態オーバーレイ。
/// SoT をキャッシュせず、開かれるたび・更新されるたびに血統宇宙を読み直して描き直す。
/// </summary>
public partial class PedigreeOverlay : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>ヘッダー帯の高さ（カード配置の上端基準）。</summary>
    private const float HeaderHeight = 72.0f;

    /// <summary>盤面の上下パディング（ヘッダー下・画面下端の余白）。</summary>
    private const float BoardPadding = 16.0f;

    /// <summary>世代帯の本数（-2 祖父母 / -1 父母 / 0 本人帯 / +1 子 / +2 孫 の 5 段）。</summary>
    private const int GenerationBandCount = 5;

    /// <summary>本人を 0 とした世代帯の最小値（祖父母 = -2）。帯インデックス換算の原点。</summary>
    private const int MinGeneration = -2;

    /// <summary>本人を 0 とした世代帯の最大値（孫 = +2）。</summary>
    private const int MaxGeneration = 2;

    /// <summary>1 枚のユニットカードの寸法。</summary>
    private static readonly Vector2 CardSize = new(176.0f, 78.0f);

    /// <summary>コネクタ（Line2D）の太さ。</summary>
    private const float ConnectorWidth = 3.0f;

    /// <summary>コネクタ 1 本が親から子へ伸び切るまでの時間（秒）。</summary>
    private const double ConnectorGrowSeconds = 0.55;

    /// <summary>世代帯ごとの開通の時間差（秒）。祖先帯から順に通電させる。</summary>
    private const double ConnectorGrowStaggerSeconds = 0.12;

    /// <summary>暗幕の色（半透明の深紺。背後の拠点・戦闘画面を沈めて家系図に集中させる）。</summary>
    private static readonly Color BackdropColor = new(0.04f, 0.05f, 0.09f, 0.93f);

    /// <summary>遺影（現役を去った先祖）のセピア・半透明 Modulate。子へ乗算伝播して全体を褪色させる。</summary>
    private static readonly Color MemorialTint = new(0.80f, 0.67f, 0.49f, 0.55f);

    /// <summary>血脈コネクタの色（金褐色。血の縦軸を象徴する一本鎖の色価）。</summary>
    private static readonly Color ConnectorColor = new(0.83f, 0.71f, 0.42f);

    // ─── 外部 API（マウント側 = GameDirector が AddChild 前に設定する） ─────

    /// <summary>家系図の根とするユニット Id。AddChild 前に設定する（無状態の徹底）。</summary>
    public Guid TargetUnitId { get; set; }

    /// <summary>閉じる意思表示。閉じるボタン押下・Esc で 1 度発火し、解放は購読側が引く。</summary>
    public event Action? CloseRequested;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    /// <summary>家系図の盤面（非コンテナ Control）。カード・コネクタを絶対座標で並べる土台。</summary>
    private Control? _canvas;

    /// <summary>ヘッダーの副題（根となる本人の氏名を表示する文脈ラベル）。</summary>
    private Label? _subtitleLabel;

    // ─── リークフリー台帳（更地化の対象） ─────────────────────────────────

    /// <summary>生成したカード・コネクタ・空表示などの盤面ノード台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _treeNodes = new();

    /// <summary>生成した開通 Tween の台帳（再描画・退場で全 Kill）。</summary>
    private readonly List<Tween> _growTweens = new();

    /// <summary>カード中心座標（_canvas ローカル）。コネクタ端点の引き当てに使う。再描画で更地化。</summary>
    private readonly Dictionary<Guid, Vector2> _cardCenters = new();

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;   // 背後の拠点・戦闘画面への入力を遮断（overlay）
        SetMeta(TestIdMetaKey, "pedigree-overlay-root");

        BuildChrome();
        SubscribeSignals();
        RenderTree();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
        ClearTree();   // 退場時に台帳を更地化（Tween ゾンビ化・ノードリークの根絶）
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
        backdrop.SetMeta(TestIdMetaKey, "pedigree-overlay-backdrop");
        AddChild(backdrop);

        // ── 盤面の土台（非コンテナ・フルレクト。カード/線を絶対座標で並べる） ──
        //   入力は素通り（Ignore）させ、最前面のヘッダーのボタンを妨げない。
        _canvas = new Control();
        _canvas.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _canvas.MouseFilter = MouseFilterEnum.Ignore;
        _canvas.SetMeta(TestIdMetaKey, "pedigree-overlay-canvas");
        AddChild(_canvas);

        // ── ヘッダー（題字 + 副題 + 閉じるボタン） ───────────────────
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        header.OffsetLeft   = 16;
        header.OffsetRight  = -16;
        header.OffsetTop    = 12;
        header.OffsetBottom = HeaderHeight;
        header.SetMeta(TestIdMetaKey, "pedigree-overlay-header");
        AddChild(header);

        var title = new Label { Text = "🌳 家系図 ― 血の縦軸" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", ConnectorColor);
        title.SetMeta(TestIdMetaKey, "pedigree-overlay-title");
        header.AddChild(title);

        _subtitleLabel = new Label { Text = string.Empty };
        _subtitleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _subtitleLabel.VerticalAlignment = VerticalAlignment.Center;
        _subtitleLabel.SetMeta(TestIdMetaKey, "pedigree-overlay-subtitle");
        header.AddChild(_subtitleLabel);

        var closeButton = new Button { Text = "✕ 閉じる", CustomMinimumSize = new Vector2(120, 40) };
        closeButton.Pressed += OnClosePressed;
        closeButton.SetMeta(TestIdMetaKey, "pedigree-overlay-close-button");
        header.AddChild(closeButton);
    }

    // ─── シグナル購読 / 解除（SoT 変更に追従して無状態に描き直す） ─────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.RosterChanged    += OnRosterChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.RosterChanged    -= OnRosterChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    private void OnRosterChanged() => RenderTree();
    private void OnStateInitialized() => RenderTree();

    private void OnClosePressed() => CloseRequested?.Invoke();

    // ─── 更地化（カード・コネクタ・Tween を一括解放） ─────────────────────

    /// <summary>
    /// 盤面ノードと開通 Tween の台帳をすべて解放して更地に戻す。再描画（RenderTree）の
    /// 冒頭と退場（_ExitTree）で必ず呼び、ゾンビ Tween・取り残しノードを構造的に根絶する。
    /// </summary>
    private void ClearTree()
    {
        foreach (var tween in _growTweens)
        {
            if (tween is not null && tween.IsValid())
            {
                tween.Kill();
            }
        }
        _growTweens.Clear();

        foreach (var node in _treeNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
        _treeNodes.Clear();

        _cardCenters.Clear();
    }

    // ─── 描画（SoT をその場で読み直し、家系図を組み直す） ─────────────────

    /// <summary>
    /// 血統宇宙と現役 Id 集合を SoT からその場で読み直し、PedigreeBuilder でグラフを
    /// 組み立てて盤面へ描く。冒頭で必ず更地化してから描くため、再描画でも線・カード・
    /// Tween が累積しない（無状態・リークフリー）。
    /// </summary>
    private void RenderTree()
    {
        ClearTree();   // 何より先に更地化（多重描画・リークの構造的封じ込め）

        if (_canvas is null) return;

        // SoT が未取得（Autoload 不在）なら、安全に空表示へ倒す。
        if (_chronicleGlobal is null)
        {
            RenderEmpty("（家系図データを取得できません）");
            return;
        }

        // ── SoT からその場で手繰り寄せる（キャッシュしない） ───────────
        var universe = _chronicleGlobal.GetPedigreeUniverse();
        var currentMemberIds = new HashSet<Guid>();
        foreach (var unit in _chronicleGlobal.BattalionRoster)
        {
            currentMemberIds.Add(unit.Id);
        }

        var graph = PedigreeBuilder.Build(universe, currentMemberIds, TargetUnitId);

        // 副題（本人の氏名）を更新。根が不在なら空表示へ。
        if (!graph.HasNodes)
        {
            if (_subtitleLabel is not null) _subtitleLabel.Text = string.Empty;
            RenderEmpty("（この英霊の血統記録はまだ刻まれていません）");
            return;
        }

        UpdateSubtitle(graph, universe);

        // ── 盤面寸法とカード配置 → コネクタ開通 の順に描く ──────────────
        LayoutCards(graph);
        GrowConnectors(graph);
    }

    /// <summary>根（本人）の氏名を副題へ反映する。解決できない場合は無印で落ち着かせる。</summary>
    private void UpdateSubtitle(PedigreeGraph graph, IReadOnlyDictionary<Guid, Unit> universe)
    {
        if (_subtitleLabel is null) return;
        if (universe.TryGetValue(graph.RootId, out var rootUnit))
        {
            _subtitleLabel.Text = $"本人: {ResolveName(rootUnit)}（{ResolveJob(rootUnit.Job)}）";
        }
        else
        {
            _subtitleLabel.Text = string.Empty;
        }
    }

    /// <summary>
    /// 世代帯ごとにカードを静的割り当てで配置する。各帯は縦に等分し、帯内のカードは横へ
    /// 均等配分する。配置と同時にカード中心を _cardCenters へ控え、後段のコネクタ端点に使う。
    /// </summary>
    private void LayoutCards(PedigreeGraph graph)
    {
        if (_canvas is null) return;

        var viewportSize = GetViewportRect().Size;
        float width  = viewportSize.X > 1.0f ? viewportSize.X : 1280.0f;
        float height = viewportSize.Y > 1.0f ? viewportSize.Y : 720.0f;

        float boardTop = HeaderHeight + BoardPadding;
        float usableHeight = Mathf.Max(height - boardTop - BoardPadding, CardSize.Y * GenerationBandCount);
        float bandHeight = usableHeight / GenerationBandCount;

        // 世代 → その帯に属するノード列（採録順を保つ）。
        var byGeneration = new Dictionary<int, List<PedigreeNode>>();
        foreach (var node in graph.Nodes)
        {
            if (!byGeneration.TryGetValue(node.Generation, out var bucket))
            {
                bucket = new List<PedigreeNode>();
                byGeneration[node.Generation] = bucket;
            }
            bucket.Add(node);
        }

        for (int generation = MinGeneration; generation <= MaxGeneration; generation++)
        {
            if (!byGeneration.TryGetValue(generation, out var bandNodes)) continue;
            if (bandNodes.Count == 0) continue;

            int bandIndex = generation - MinGeneration; // -2→0 ... +2→4
            float centerY = boardTop + (bandIndex + 0.5f) * bandHeight;

            for (int i = 0; i < bandNodes.Count; i++)
            {
                float centerX = width * (i + 1) / (bandNodes.Count + 1);
                var center = new Vector2(centerX, centerY);
                _cardCenters[bandNodes[i].UnitId] = center;
                SpawnCard(bandNodes[i], center);
            }
        }
    }

    /// <summary>
    /// 1 ユニット分の軽量カードを中心座標から配置する。現役は等倍（白）、英霊（去った先祖）は
    /// セピア・半透明（MemorialTint）で「遺影」として沈める。氏名・ジョブは localization で解決。
    /// </summary>
    private void SpawnCard(PedigreeNode node, Vector2 center)
    {
        if (_canvas is null) return;

        var card = new Panel
        {
            Position          = center - CardSize * 0.5f,
            Size              = CardSize,
            CustomMinimumSize = CardSize,
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        if (!node.IsCurrentMember)
        {
            card.Modulate = MemorialTint; // 遺影（子ラベルへも乗算伝播して全体が褪色）
        }
        card.SetMeta(TestIdMetaKey, $"pedigree-{RelationSlug(node.Relation)}-card-{node.UnitId}");

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        card.AddChild(margin);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        // 関係ラベル（クローム）+ 現役/英霊の標識。
        var statusMark = node.IsCurrentMember ? "⚔" : "✝";
        var relationLabel = new Label { Text = $"{statusMark} {RelationLabel(node.Relation)}" };
        relationLabel.AddThemeFontSizeOverride("font_size", 12);
        relationLabel.SetMeta(TestIdMetaKey, $"pedigree-card-relation-{node.UnitId}");
        column.AddChild(relationLabel);

        // 氏名（localization 解決・データ名は直書きしない）。
        var nameLabel = new Label { Text = ResolveName(node.Unit) };
        nameLabel.SetMeta(TestIdMetaKey, $"pedigree-card-name-{node.UnitId}");
        column.AddChild(nameLabel);

        // ジョブ + 年齢（クローム接続詞のみ日本語、ジョブ名は localization 解決）。
        var jobLabel = new Label { Text = $"{ResolveJob(node.Unit.Job)} ・ Age {node.Unit.Age}" };
        jobLabel.AddThemeFontSizeOverride("font_size", 12);
        jobLabel.SetMeta(TestIdMetaKey, $"pedigree-card-job-{node.UnitId}");
        column.AddChild(jobLabel);

        _canvas.AddChild(card);
        _treeNodes.Add(card);
    }

    /// <summary>
    /// 親カード下端 → 子カード上端へ Line2D コネクタを張り、JuiceDirector.GrowLine で
    /// 親から子へ伸びる開通 Tween を叩く。両端のカード中心が揃ったエッジにだけ張るため
    /// 宙吊り線は生じない。世代帯が深いほど僅かに遅らせ、祖先から子孫へ通電させる。
    /// </summary>
    private void GrowConnectors(PedigreeGraph graph)
    {
        if (_canvas is null) return;

        // エッジの開通遅延を親の世代帯から引くための索引。
        var generationById = new Dictionary<Guid, int>();
        foreach (var node in graph.Nodes)
        {
            generationById[node.UnitId] = node.Generation;
        }

        foreach (var edge in graph.Edges)
        {
            if (!_cardCenters.TryGetValue(edge.ParentId, out var parentCenter)) continue;
            if (!_cardCenters.TryGetValue(edge.ChildId, out var childCenter)) continue;

            // 親カード下端中央 → 子カード上端中央（縦系の一本鎖として綺麗に縦に落とす）。
            var from = new Vector2(parentCenter.X, parentCenter.Y + CardSize.Y * 0.5f);
            var to   = new Vector2(childCenter.X,  childCenter.Y - CardSize.Y * 0.5f);

            var connector = new Line2D
            {
                Width        = ConnectorWidth,
                DefaultColor = ConnectorColor,
                ZIndex       = -1,                 // カード（既定 Z=0）より背面へ
                Points       = new[] { from, from }, // 長さ 0 から開始（GrowLine が伸ばす）
            };
            connector.SetMeta(TestIdMetaKey, $"pedigree-tree-connector-{edge.ParentId}-{edge.ChildId}");
            _canvas.AddChild(connector);
            // 念のため描画順でもカードの背面へ送る（Z だけに頼らず二重に保証）。
            _canvas.MoveChild(connector, 0);
            _treeNodes.Add(connector);

            int parentBandIndex = generationById.TryGetValue(edge.ParentId, out var g)
                ? g - MinGeneration
                : 0;
            double delaySeconds = parentBandIndex * ConnectorGrowStaggerSeconds;

            var tween = JuiceDirector.GrowLine(connector, from, to, ConnectorGrowSeconds, delaySeconds);
            if (tween is not null)
            {
                _growTweens.Add(tween);
            }
        }
    }

    /// <summary>
    /// 描くべきノードが無い場合の空表示（中央のクローム文言）を盤面へ置く。台帳に積んで
    /// 次の更地化で消えるようにする。
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
        label.SetMeta(TestIdMetaKey, "pedigree-overlay-empty");
        _canvas.AddChild(label);
        _treeNodes.Add(label);
    }

    // ─── ローカライゼーション / 関係ラベル ────────────────────────────────
    //   氏名・ジョブ名は ChronicleGlobal 経由で localization 解決し、Autoload 不在時は
    //   生キー / enum 名へフォールバックして画面を落とさない（設計憲法①）。関係ラベルは
    //   「データ名」ではなく UI が血縁を説明するクローム語であり、日本語直書きを許容する。

    private string ResolveName(Unit unit)
        => _chronicleGlobal?.ResolveDisplayName(unit)
           ?? (string.IsNullOrEmpty(unit.LastNameKey) ? unit.FirstNameKey : $"{unit.FirstNameKey}{unit.LastNameKey}");

    private string ResolveJob(JobId job)
        => _chronicleGlobal?.ResolveJobName(job) ?? job.ToString();

    /// <summary>関係種別 → testid 用 ASCII スラッグ（機械生成の足場）。</summary>
    private static string RelationSlug(PedigreeRelation relation) => relation switch
    {
        PedigreeRelation.Grandparent => "ancestor",
        PedigreeRelation.Parent      => "parent",
        PedigreeRelation.Self        => "self",
        PedigreeRelation.Spouse      => "spouse",
        PedigreeRelation.Sibling     => "sibling",
        PedigreeRelation.Child       => "child",
        PedigreeRelation.Grandchild  => "grandchild",
        _ => "unit",
    };

    /// <summary>関係種別 → 表示ラベル（クローム日本語。データ名ではない血縁の説明語）。</summary>
    private static string RelationLabel(PedigreeRelation relation) => relation switch
    {
        PedigreeRelation.Grandparent => "祖父母",
        PedigreeRelation.Parent      => "父母",
        PedigreeRelation.Self        => "★ 本人",
        PedigreeRelation.Spouse      => "配偶者",
        PedigreeRelation.Sibling     => "兄弟姉妹",
        PedigreeRelation.Child       => "子",
        PedigreeRelation.Grandchild  => "孫",
        _ => "縁者",
    };
}
