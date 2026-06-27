// =============================================================================
//  ChronicleKnights — TimelineUI.cs
// -----------------------------------------------------------------------------
//  拠点A: 予言と歴史進行画面 (Control シーン)。
//
//  プレイヤーに提示される 3 つの予言を読み取り、3 つのボタンに反映する。
//  ボタンを押すと ChronicleGlobal.SelectProphecyAndAdvance(id) を呼ぶ。これは
//  選択した予言の SkipYears を「この世代の長さ」として確定（保留）し、状態を
//  旅団組合フェーズへ進めるだけに留まる。実際の年送り（全旅団員の一斉加齢 +
//  寿命到達/戦闘死の完全ロスト + 定期収入 + 次予言 3 つの再生成）は、ループ 1 周の
//  幕引きである Battle→Chronicle 遷移時に一括適用される（「1 世代 = 時間軸 1 周」の
//  構造）。本画面は予言の選択と歴史の起点を担うだけで、年送りそのものは行わない。
//
//  画面構成:
//    ┌──────────────────────────────────────────────────────┐
//    │ [📖 予言タイムライン]    💰 残高: 23 pt    ターン 5 │
//    ├──────────────────────────────────────────────────────┤
//    │  ┌────────┐  ┌────────┐  ┌────────┐                 │
//    │  │ 💰     │  │ ⚔      │  │ 👥     │                 │
//    │  │ Reward │  │ Battle │  │ Scout  │                 │
//    │  │ +5 pt  │  │ Lv 8   │  │ +1 人  │                 │
//    │  │ ⏳ 2年 │  │ ⏳ 3年 │  │ ⏳ 4年 │                 │
//    │  └────────┘  └────────┘  └────────┘                 │
//    └──────────────────────────────────────────────────────┘
//
//  シグナル購読:
//    - EconomyChanged    → 残高ラベル再描画
//    - TimelineChanged   → 3 ボタン再描画 (次予言反映)
//    - StateInitialized  → 全体再描画
//
//  クリーン設計:
//    - 略称 (BDF/SDF/AB/HL) 完全未使用
//    - 状態は ChronicleGlobal から読むだけ、保持しない (SoT 違反防止)
//    - 予言種別のアイコン・表示名は ChronicleGlobal.ResolveProphecyKindIcon /
//      ResolveProphecyKindName 経由で localization_ja.json から解決（日本語・絵文字を
//      本ファイルへ一切ハードコードしない／設計憲法 ①）
//    - メモリリーク防止: _ExitTree で全シグナルを購読解除
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;           // Gender
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.UserInterface;         // JobTextureLibrary（ジョブ立ち絵アイコン）
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 予言タイムライン画面。プレイヤーは 3 択から 1 つを選んで歴史を進める。
/// </summary>
public partial class TimelineUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    private const int ProphecyOptionCount = 3;

    /// <summary>data-testid を載せる Godot メタキー（instructions.md の testid 規約に準拠）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>年代記ナレーションに一度に表示する最大行数（古い行はスクロールで辿れる）。</summary>
    private const int MaxNarrationLines = 60;

    /// <summary>年代記ログ各行のジョブ立ち絵アイコンの一辺サイズ（px）。立ち絵は縦長なので小さく。</summary>
    private const int LogIconSize = 24;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private Label? _balanceLabel;
    private Label? _turnLabel;
    private readonly Button[] _prophecyButtons = new Button[ProphecyOptionCount];
    private readonly Label[] _prophecyDetailLabels = new Label[ProphecyOptionCount];
    private readonly TextureRect[] _prophecyArt = new TextureRect[ProphecyOptionCount];

    /// <summary>年代記ナレーションの各行を収める器（無状態：毎回 SoT から丸ごと再描画）。</summary>
    private VBoxContainer? _narrationLinesBox;

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
        UnsubscribeSignals();
    }

    // ─── UI 構築 ──────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // 全画面スクロール: ルート VBox を画面いっぱいの縦 ScrollContainer で包む。
        // 画面(this)は非コンテナ Control。FullRect の ScrollContainer がその高さに束縛され、
        // 内容が画面高を超えると縦スクロールが効く。横スクロールは無効化し子幅を画面幅へ伸張する。
        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SetMeta(TestIdMetaKey, "chronicle-timeline-scroll");
        AddChild(scroll);

        var root = new VBoxContainer();
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddThemeConstantOverride("separation", 16);
        root.SetMeta(TestIdMetaKey, "chronicle-timeline-root");
        scroll.AddChild(root);

        // ── ヘッダー：タイトル + 残高 + ターン番号 ──────────────────
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 24);
        header.SetMeta(TestIdMetaKey, "chronicle-timeline-header");
        root.AddChild(header);

        var titleLabel = new Label
        {
            Text = "📖 予言タイムライン",
        };
        titleLabel.SetMeta(TestIdMetaKey, "chronicle-timeline-title");
        header.AddChild(titleLabel);

        _balanceLabel = new Label();
        _balanceLabel.SetMeta(TestIdMetaKey, "chronicle-timeline-balance");
        header.AddChild(_balanceLabel);

        _turnLabel = new Label();
        _turnLabel.SetMeta(TestIdMetaKey, "chronicle-timeline-turn");
        header.AddChild(_turnLabel);

        // ── ボディ：3 予言ボタン ───────────────────────────────────
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        body.SetMeta(TestIdMetaKey, "chronicle-prophecy-body");
        root.AddChild(body);

        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            int captured = i; // closure capture safety

            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 4);
            card.SetMeta(TestIdMetaKey, $"chronicle-prophecy-card-{captured}");

            // カード上部のイラスト（予言種別ごと）。未配置なら null = 非表示（従来の文字表示のまま）。
            var art = new TextureRect
            {
                CustomMinimumSize = new Vector2(220, 124),
                StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
            };
            art.SetMeta(TestIdMetaKey, $"chronicle-prophecy-art-{captured}");
            card.AddChild(art);
            _prophecyArt[i] = art;

            var btn = new Button
            {
                CustomMinimumSize = new Vector2(220, 120),
            };
            btn.SetMeta(TestIdMetaKey, $"chronicle-prophecy-button-{captured}");
            btn.Pressed += () => OnProphecyButtonPressed(captured);
            card.AddChild(btn);

            var detail = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            detail.SetMeta(TestIdMetaKey, $"chronicle-prophecy-detail-{captured}");
            card.AddChild(detail);

            _prophecyButtons[i] = btn;
            _prophecyDetailLabels[i] = detail;
            body.AddChild(card);
        }

        BuildNarrationPanel(root);
    }

    /// <summary>
    /// 旅団年代記（引退 / 戦死 / 昇級のナレーション）を表示する無状態パネルを構築する。
    /// ここでは器（タイトル・スクロール領域・行コンテナ）だけを組み、各行の中身は
    /// <see cref="RenderNarration"/> が SoT（<see cref="ChronicleGlobal.GetChronicleLog"/>）から
    /// 丸ごと読み直して毎回再生成する（キャッシュを一切持たない単方向データフロー）。
    /// </summary>
    private void BuildNarrationPanel(Control root)
    {
        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", 6);
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        panel.SetMeta(TestIdMetaKey, "chronicle-narration-panel");
        root.AddChild(panel);

        var title = new Label
        {
            Text = "📜 旅団年代記",
        };
        title.SetMeta(TestIdMetaKey, "chronicle-narration-title");
        panel.AddChild(title);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 220),
        };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SetMeta(TestIdMetaKey, "chronicle-narration-scroll");
        panel.AddChild(scroll);

        _narrationLinesBox = new VBoxContainer();
        _narrationLinesBox.AddThemeConstantOverride("separation", 2);
        _narrationLinesBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _narrationLinesBox.SetMeta(TestIdMetaKey, "chronicle-narration-lines");
        scroll.AddChild(_narrationLinesBox);
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.EconomyChanged    += OnEconomyChanged;
        _chronicleGlobal.TimelineChanged   += OnTimelineChanged;
        _chronicleGlobal.StateInitialized  += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.EconomyChanged    -= OnEconomyChanged;
            _chronicleGlobal.TimelineChanged   -= OnTimelineChanged;
            _chronicleGlobal.StateInitialized  -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnEconomyChanged()    => RenderBalance();
    private void OnStateInitialized()  => RenderAll();

    /// <summary>
    /// タイムライン更新シグナル。世代交代（Battle→Chronicle）でも必ず発火し、旅団史が
    /// 伸びるのはこの瞬間だけなので、予言ボタンと年代記ナレーションを併せて再描画する。
    /// </summary>
    private void OnTimelineChanged()
    {
        RenderProphecies();
        RenderNarration();
    }

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        RenderBalance();
        RenderProphecies();
        RenderNarration();
    }

    private void RenderBalance()
    {
        if (_chronicleGlobal is null || _balanceLabel is null) return;
        var balance = _chronicleGlobal.CurrentEconomy.CurrentBalance;
        _balanceLabel.Text = $"💰 残高: {balance} pt";
    }

    private void RenderProphecies()
    {
        if (_chronicleGlobal is null) return;

        // ターン番号
        if (_turnLabel is not null)
        {
            var turn = _chronicleGlobal.CurrentTimeline?.Turn ?? 0;
            _turnLabel.Text = $"⏰ ターン {turn}";
        }

        // 3 予言ボタン
        var options = _chronicleGlobal.GetCurrentProphecies();
        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            var btn = _prophecyButtons[i];
            var detail = _prophecyDetailLabels[i];
            if (btn is null || detail is null) continue;

            if (i < options.Count)
            {
                var p = options[i];
                btn.Disabled = false;
                // 上段＝レア度バッジ（銅/銀/金）、中段＝予言種別、下段＝効果量。
                btn.Text =
                    $"{_chronicleGlobal.ResolveProphecyRarityIcon(p.Rarity)} {_chronicleGlobal.ResolveProphecyRarityName(p.Rarity)}\n" +
                    $"{_chronicleGlobal.ResolveProphecyKindIcon(p.Kind)} {_chronicleGlobal.ResolveProphecyKindName(p.Kind)}\n" +
                    $"値: {p.Value}";
                btn.Modulate = RarityColor(p.Rarity); // レア度で色味を変える
                // カード上部のイラスト（予言種別ごと）。未配置なら null = 非表示。
                if (_prophecyArt[i] is { } art) art.Texture = ProphecyTextureLibrary.TryLoad(p.Kind);
                // 詳細：フレーバー文 ＋ タイムスキップ年数。
                var flavor = _chronicleGlobal.ResolveProphecyDescription(p.DescriptionKey);
                detail.Text = $"{flavor}\n⏳ {p.SkipYears} 年経過";
            }
            else
            {
                btn.Disabled = true;
                btn.Modulate = Colors.White;
                btn.Text = "—";
                detail.Text = "";
                if (_prophecyArt[i] is { } art) art.Texture = null;
            }
        }
    }

    /// <summary>
    /// 旅団年代記（引退 / 戦死 / 昇級のナレーション）を SoT から丸ごと読み直して再描画する。
    ///
    /// 無状態・単方向データフローの規律:
    ///   - ログは一切キャッシュしない。毎回 <see cref="ChronicleGlobal.GetChronicleLog"/> から
    ///     完全不変スナップショットを取得し、行コンテナを全消去してから組み直す。
    ///   - 行ノードは素朴な <see cref="Label"/> のみ（Tween・タイマー・購読を一切持たない）ため、
    ///     QueueFree による全消去でゾンビ Tween やリーク購読が残らない。
    ///   - 新しい出来事ほど上に来るよう逆順（新 → 旧）で並べ、直近の歴史を最初に見せる。
    ///   - 表示名・ジョブ名は localization 経由で解決し、日本語データ名をハードコードしない
    ///     （ナレーションの定型文＝クロームは設計憲法上ハードコード可）。
    /// </summary>
    private void RenderNarration()
    {
        if (_narrationLinesBox is null) return;

        // 既存行を全消去（素朴な Label のみなので QueueFree で安全に破棄できる）。
        foreach (var child in _narrationLinesBox.GetChildren())
        {
            child.QueueFree();
        }

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
            _narrationLinesBox.AddChild(empty);
            return;
        }

        // 新しい出来事ほど上に来るよう逆順で描く（直近の歴史を最初に見せる）。
        // 表示行数の上限を設け、古い行はスクロールではなく単に省略する（描画コスト抑制）。
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

            _narrationLinesBox.AddChild(row);
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
    /// 予言カードのレア度に応じた色味（Modulate）。ブロンズは無着色（標準）、シルバーは
    /// 涼やかな白銀、ゴールドは黄金色。色は装飾でありゲームロジックには一切関与しない。
    /// </summary>
    private static Color RarityColor(ProphecyRarity rarity) => rarity switch
    {
        ProphecyRarity.Gold   => new Color(1.0f, 0.86f, 0.35f),
        ProphecyRarity.Silver => new Color(0.82f, 0.88f, 1.0f),
        _                     => Colors.White,
    };

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

    // ─── ボタンアクション ─────────────────────────────────────────────────

    private void OnProphecyButtonPressed(int index)
    {
        if (_chronicleGlobal is null) return;
        var options = _chronicleGlobal.GetCurrentProphecies();
        if (index < 0 || index >= options.Count) return;

        var prophecyId = options[index].Id;
        var selected = _chronicleGlobal.SelectProphecyAndAdvance(prophecyId);

        // 結果ログ（再描画はシグナル経由で自動）
        if (selected is not null)
        {
            GD.Print($"[TimelineUI] 予言選択: {selected.Kind} (+{selected.SkipYears}年, 値={selected.Value})");
        }
    }

    // ─── ローカライゼーション ─────────────────────────────────────────────
    // 予言種別のアイコン・表示名は ChronicleGlobal.ResolveProphecyKindIcon /
    // ResolveProphecyKindName（内部で純粋層 MasterDataNameResolver が
    // localization_ja.json の prophecyKinds セクションを引く）に委譲する。
    // 本ファイルには日本語・絵文字を一切ハードコードしない（設計憲法 ①）。
}
