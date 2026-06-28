// =============================================================================
//  ChronicleKnights — ChronicleLogOverlay.cs
// -----------------------------------------------------------------------------
//  Front-most overlay that shows the brigade chronicle (旅団年代記): the running
//  narration of retirements / deaths in action / level-ups / dismissals across
//  generations. Previously this panel lived inline on the Chronicle screen
//  (TimelineUI); it now opens on demand from the persistent header bar so it is
//  reachable from every phase (the header's "📜 旅団年代記" button).
//
//  Stateless, self-collapsing overlay (same lifecycle as JobManualOverlay /
//  PedigreeOverlay): CLOSE raises CloseRequested; GameDirector mounts it at the
//  very front and frees it on close / _ExitTree. The log is a snapshot read once
//  on _Ready (it only grows at generation turnover, not while this is open), so
//  no SoT subscription is needed. Constitution I: identifiers/strings ASCII only
//  except player-facing display text.
// =============================================================================

using System.Collections.Immutable;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Chronicle;        // ChronicleLogEntry / ChronicleEventKind
using ChronicleKnights.Core.Job;              // JobId
using ChronicleKnights.Core.Naming;           // Gender
using ChronicleKnights.UserInterface;         // JobTextureLibrary（ジョブ立ち絵アイコン）
using Godot;
using System;

namespace ChronicleKnights.UI;

/// <summary>Front-most overlay listing the brigade chronicle (newest first).</summary>
public partial class ChronicleLogOverlay : Godot.Control
{
    private const string TestIdMetaKey = "data_testid";

    /// <summary>一度に表示する最大行数（古い行はスクロールではなく省略・描画コスト抑制）。</summary>
    private const int MaxNarrationLines = 60;

    /// <summary>各行のジョブ立ち絵アイコンの一辺サイズ（px）。立ち絵は縦長なので小さく。</summary>
    private const int LogIconSize = 24;

    /// <summary>Raised when the player presses CLOSE (GameDirector frees the overlay).</summary>
    public event Action? CloseRequested;

    private ChronicleGlobal? _chronicleGlobal;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta(TestIdMetaKey, "chronicle-log-overlay-root");
        BuildUI();
    }

    private void BuildUI()
    {
        // Dim backdrop (catches clicks so the screen behind is inert).
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.88f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.SetMeta(TestIdMetaKey, "chronicle-log-overlay-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", 12);
        panel.SetMeta(TestIdMetaKey, "chronicle-log-overlay-panel");
        margin.AddChild(panel);

        // Header: title + close.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "chronicle-log-header");
        panel.AddChild(header);

        var title = new Label { Text = "📜 旅団年代記" };
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SetMeta(TestIdMetaKey, "chronicle-log-title");
        header.AddChild(title);

        var closeButton = new Button { Text = "閉じる" };
        closeButton.SetMeta(TestIdMetaKey, "chronicle-log-close-button");
        closeButton.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(closeButton);

        // Scrollable list of log lines (vertical scroll; width stretched).
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SetMeta(TestIdMetaKey, "chronicle-log-scroll");
        panel.AddChild(scroll);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        list.SetMeta(TestIdMetaKey, "chronicle-log-lines");
        scroll.AddChild(list);

        RenderNarration(list);
    }

    /// <summary>
    /// 旅団年代記（引退 / 戦死 / 昇級 / 解雇のナレーション）を SoT から丸ごと読み取り、
    /// 新しい出来事ほど上に来るよう逆順（新 → 旧）で描く。ログはキャッシュせず開いた瞬間の
    /// スナップショットを 1 度だけ描画する（年代記が伸びるのは世代交代の瞬間だけで、本オーバーレイ
    /// が開いている間は変化しない）。各行は素朴な Label のみ（Tween・購読を持たない）。
    /// </summary>
    private void RenderNarration(Control list)
    {
        var log = _chronicleGlobal?.GetChronicleLog() ?? ImmutableArray<ChronicleLogEntry>.Empty;

        // 無履歴: 空状態ラベルを 1 つだけ置く（testid 付与・突合可能に）。
        if (log.IsEmpty)
        {
            var empty = new Label
            {
                Text = "（旅団の歴史はまだ刻まれていない）",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            empty.SetMeta(TestIdMetaKey, "chronicle-log-empty");
            list.AddChild(empty);
            return;
        }

        int shown = 0;
        for (int i = log.Length - 1; i >= 0 && shown < MaxNarrationLines; i--, shown++)
        {
            var entry = log[i];

            // 1 行 = 小さなジョブ立ち絵アイコン（性別別）＋ ナレーション文。
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            // testid は「新しい行ほど小さい番号」で安定させる（0 = 最新）。行コンテナに付与。
            row.SetMeta(TestIdMetaKey, $"chronicle-log-line-{shown}");

            var icon = MakeJobIcon(entry.Job, entry.Gender);
            if (icon is not null) row.AddChild(icon);

            var line = new Label
            {
                Text = FormatEntry(entry),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            line.SetMeta(TestIdMetaKey, $"chronicle-log-text-{shown}");
            row.AddChild(line);

            list.AddChild(row);
        }
    }

    /// <summary>年代記ログ用の小さなジョブ立ち絵アイコン（ジョブ×性別）。資産欠落時のみ null。</summary>
    private static TextureRect? MakeJobIcon(JobId job, Gender gender)
    {
        var tex = JobTextureLibrary.TryLoad(job, gender);
        if (tex is null) return null;
        return new TextureRect
        {
            Texture           = tex,
            CustomMinimumSize  = new Vector2(LogIconSize, LogIconSize),
            StretchMode        = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode         = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsVertical  = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>
    /// 1 件の年代記イベントを 1 行のナレーション文へ整形する。ユニット名・ジョブ名は
    /// localization 経由（<see cref="ChronicleGlobal.ResolveDisplayName"/> /
    /// <see cref="ChronicleGlobal.ResolveJobName"/>）で解決し、日本語データ名をハードコードしない。
    /// 解決器が無い（テスト等で未注入）場合は ASCII キー / enum 名へ安全にフォールバックする。
    /// </summary>
    private string FormatEntry(ChronicleLogEntry entry)
    {
        var name = _chronicleGlobal?.ResolveDisplayName(entry.UnitFirstNameKey, entry.UnitLastNameKey)
                   ?? entry.UnitFirstNameKey;
        var jobName = _chronicleGlobal?.ResolveJobName(entry.Job)
                      ?? entry.Job.ToString();

        return entry.Kind switch
        {
            ChronicleEventKind.Retired =>
                $"📜 〈ターン{entry.Generation}〉{name}（{jobName}・{entry.Age}歳）は天寿を全うし、静かに旅団を去った。",
            ChronicleEventKind.KilledInAction =>
                $"⚔️ 〈ターン{entry.Generation}〉{name}（{jobName}・{entry.Age}歳）は戦野に斃れ、その名は伝説となった。",
            ChronicleEventKind.LevelGained =>
                $"⬆️ 〈ターン{entry.Generation}〉{name}（{jobName}）は研鑽の末、Lv{entry.FromLevel} から Lv{entry.ToLevel} へと成長した。",
            ChronicleEventKind.Dismissed =>
                $"🛡️ 〈ターン{entry.Generation}〉{name}（{jobName}・{entry.Age}歳・Lv{entry.FromLevel}）は編成判断により旅団を去った。",
            _ =>
                $"・〈ターン{entry.Generation}〉{name}（{jobName}）",
        };
    }
}
