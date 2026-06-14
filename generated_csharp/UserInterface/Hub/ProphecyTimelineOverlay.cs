// =============================================================================
//  ChronicleKnights — UserInterface/Hub/ProphecyTimelineOverlay.cs
// -----------------------------------------------------------------------------
//  拠点の「未来を見通す赤き眼」。100 年の時間の矢と章ボス襲来の前兆を映す無状態オーバーレイ。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    タイムラインや前兆の状態を 1 つもキャッシュしない。再描画のたびに SoT（ChronicleGlobal）から
//    現在年（CurrentTimeline.Turn）をその場で読み直し、章ボス襲来年（ChronicleTimelineconfig.Epochs の
//    BossYear = 25/50/75/100）の位置へ警告マーカーをパッシブ配置する。
//
//  ★ 年ベースの前兆（拠点は非戦闘ゆえ）:
//    ForecastEnemyIntentsWithOmens は戦闘中（CurrentBattle 非 null）の脅威先読み API で、拠点では空を
//    返す。よって本オーバーレイは「今が何年か」と「章ボスが何年に来るか」という年ベースの真実
//    （CurrentTimeline.Turn と Epochs の BossYear）だけで時間の矢を描く（同じ年からは同じ絵）。
//
//  ★ リークフリー（_timelineNodes 台帳方式・既存実証済みの規律）:
//    動的に生成したマーカー（および次段の前兆カード）はすべて _timelineNodes 台帳へ記録し、再描画
//    （RenderAll）の冒頭と退場（_ExitTree）で例外なく一括 QueueFree して更地化する。シグナル購読も
//    _ExitTree で完全解除する。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"Timeline:" 等）。日本語・データ名のハードコードを持たない。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.UI;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>
/// SoT から現在年を読み、100 年の時間の矢（プログレスバー）へマッピングし、章ボス襲来年へ
/// 緋色の警告マーカーをパッシブ配置するだけの、状態を持たない予言オーバーレイ。
/// </summary>
public partial class ProphecyTimelineOverlay : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>時間の矢（プログレスバー）の固定幅・高さ。</summary>
    private const float TimelineWidth = 420.0f;
    private const float TimelineHeight = 28.0f;

    /// <summary>前兆カードを出し始める残りターン閾値（残り 10 ターン以内で接近警告を物質化）。</summary>
    private const int OmenThreshold = 10;

    /// <summary>秒読み段階（緋色フラッシュ）の脈動秒数。</summary>
    private const double OmenFlashSeconds = 0.6;

    /// <summary>章ボス警告マーカー/前兆カードの色（緋色 ≒ Colors.Crimson。危急を告げる色価言語）。</summary>
    private static readonly Color OmenColor = new(0.86f, 0.08f, 0.24f);

    private ChronicleGlobal? _chronicleGlobal;
    private ProgressBar? _timelineProgress;
    private Control? _markerLayer;
    private VBoxContainer? _omenSlot;

    /// <summary>動的生成したマーカー/前兆カードの台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _timelineNodes = new();

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetMeta(TestIdMetaKey, "hub-view-prophecy-overlay");

        BuildChrome();
        SubscribeSignals();
        RenderAll();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
        ClearTimelineNodes();
    }

    // ─── 不変クローム構築（バー + マーカー層の土台） ──────────────────────

    private void BuildChrome()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        column.SetMeta(TestIdMetaKey, "hub-view-prophecy-panel");
        AddChild(column);

        var header = new Label { Text = "Timeline:" };
        header.SetMeta(TestIdMetaKey, "hub-view-timeline-header");
        column.AddChild(header);

        // 時間の矢（プログレスバー）と、その上に章ボス位置の警告を重ねる固定幅の土台。
        var stack = new Control { CustomMinimumSize = new Vector2(TimelineWidth, TimelineHeight) };
        column.AddChild(stack);

        _timelineProgress = new ProgressBar
        {
            MinValue       = ChronicleTimelineConfig.FirstYear,
            MaxValue       = ChronicleTimelineConfig.TotalYears,
            ShowPercentage = false,
        };
        _timelineProgress.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _timelineProgress.SetMeta(TestIdMetaKey, "hub-view-timeline-progress");
        stack.AddChild(_timelineProgress);

        // マーカー層（バーの上に章ボス前兆を重ねる。入力は素通りさせる）。
        _markerLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _markerLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _markerLayer.SetMeta(TestIdMetaKey, "hub-view-timeline-markers");
        stack.AddChild(_markerLayer);

        // 前兆カードの収納枠（接近する章ボスがあれば動的にカードを 1 枚生やす土台）。
        _omenSlot = new VBoxContainer();
        _omenSlot.AddThemeConstantOverride("separation", 4);
        _omenSlot.SetMeta(TestIdMetaKey, "hub-view-omen-slot");
        column.AddChild(_omenSlot);
    }

    // ─── シグナル購読 / 解除（年次進行に追従して無状態に描き直す） ──────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.TimelineChanged  += OnTimelineChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.TimelineChanged  -= OnTimelineChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（リーク防止）。
        }
    }

    private void OnTimelineChanged() => RenderAll();
    private void OnStateInitialized() => RenderAll();

    // ─── 更地化（台帳の全マーカーを一括解放） ─────────────────────────────

    private void ClearTimelineNodes()
    {
        foreach (var node in _timelineNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _timelineNodes.Clear();
    }

    // ─── 描画（SoT をその場で読み直し、時間の矢と章ボスマーカーを描く） ─────

    private void RenderAll()
    {
        ClearTimelineNodes(); // 何より先に更地化（再描画でマーカーが累積しない）

        if (_timelineProgress is null || _markerLayer is null) return;

        var year = _chronicleGlobal?.CurrentTimeline?.Turn ?? 0;
        _timelineProgress.Value = year;

        // 章ボス襲来年（25/50/75/100）の位置へ緋色の警告マーカーをパッシブ配置。
        foreach (var epoch in ChronicleTimelineConfig.Epochs)
        {
            PlaceBossMarker(epoch.BossYear);
        }

        // 直近に迫る章ボスがあれば前兆カードを 1 枚動的生成（残り 10 ターン以内）。
        RenderOmenCard(year);
    }

    /// <summary>
    /// 暦（現在年）だけから次の章ボスまでの残りターンを決定論的に弾き、接近圏（OmenThreshold）に
    /// 入っていれば前兆カード（hub-view-omen-card）を 1 枚動的生成して台帳へ登録する。秒読み段階
    /// （先行告知窓 ForewarnLeadTurns 以内）に入ったら緋色フラッシュ＋コンソール警告を焚く。
    /// カードは台帳経由で再描画・退場時に必ず QueueFree される（リークフリー）。
    /// </summary>
    private void RenderOmenCard(int year)
    {
        if (_omenSlot is null) return;

        var omen = ChronicleTimelineConfig.BuildOmenScheduleForYear(year);
        if (!omen.BossApproaching || omen.TurnsUntilArrival > OmenThreshold)
        {
            return; // 接近する章ボスが無い、または残りターンが閾値外なら前兆を出さない。
        }

        var card = new PanelContainer();
        card.SetMeta(TestIdMetaKey, "hub-view-omen-card");

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 2);
        card.AddChild(body);

        var header = new Label { Text = "OMEN" };
        header.AddThemeColorOverride("font_color", OmenColor);
        header.SetMeta(TestIdMetaKey, "hub-view-omen-header");
        body.AddChild(header);

        // 接近中の章ボス原型は enum 名（ASCII）をそのまま表示（憲法①: 日本語ハードコードなし）。
        var bossName = new Label { Text = omen.BossArchetype.ToString() };
        bossName.SetMeta(TestIdMetaKey, "hub-view-omen-boss-name");
        body.AddChild(bossName);

        var countdown = new Label { Text = "BOSS ARRIVAL IN: " + omen.TurnsUntilArrival + " TURNS" };
        countdown.SetMeta(TestIdMetaKey, "hub-view-omen-countdown");
        body.AddChild(countdown);

        _omenSlot.AddChild(card);
        _timelineNodes.Add(card); // 台帳へ登録（更地化で一括解放）

        // 秒読み段階（先行告知窓内）に入ったら緋色フラッシュ＋コンソール警告（Tween はカードへ束縛）。
        if (omen.TurnsUntilArrival <= omen.ForewarnLeadTurns)
        {
            JuiceDirector.Flash(card, OmenColor, OmenFlashSeconds);
            GD.Print($"[OMEN] {omen.BossArchetype} BOSS ARRIVAL IN {omen.TurnsUntilArrival} TURNS");
        }
    }

    private void PlaceBossMarker(int bossYear)
    {
        if (_markerLayer is null) return;

        var x = TimelineFraction(bossYear) * TimelineWidth;
        var marker = new ColorRect
        {
            Color             = OmenColor,
            CustomMinimumSize = new Vector2(3.0f, TimelineHeight),
            Size              = new Vector2(3.0f, TimelineHeight),
            Position          = new Vector2(x - 1.5f, 0.0f),
            MouseFilter       = MouseFilterEnum.Ignore,
        };
        marker.SetMeta(TestIdMetaKey, $"hub-view-timeline-boss-marker-{bossYear}");

        _markerLayer.AddChild(marker);
        _timelineNodes.Add(marker); // 台帳へ登録（更地化で一括解放）
    }

    /// <summary>年を時間の矢の [0,1] 区間へ写す（FirstYear=0、TotalYears=1）。範囲外はクランプ。</summary>
    private static float TimelineFraction(int year)
    {
        var span = (float)(ChronicleTimelineConfig.TotalYears - ChronicleTimelineConfig.FirstYear);
        if (span <= 0.0f)
        {
            return 0.0f;
        }

        var fraction = (year - ChronicleTimelineConfig.FirstYear) / span;
        return Mathf.Clamp(fraction, 0.0f, 1.0f);
    }
}
