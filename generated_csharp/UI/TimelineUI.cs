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
//    - TimelineChanged   → 3 ボタン再描画 (次予言反映)
//    - StateInitialized  → 全体再描画
//  （ポイント残高・年代は GameDirector の固定ヘッダへ集約。本画面では扱わない）
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
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.UserInterface;         // ProphecyTextureLibrary（予言カードのイラスト）
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

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private readonly Label[] _prophecyDetailLabels = new Label[ProphecyOptionCount];
    private readonly TextureRect[] _prophecyArt = new TextureRect[ProphecyOptionCount];
    // 画像そのものに巻く枠（レア度色＋選択強調）のスタイル。RenderProphecies / ApplyPendingHighlight が差し替える。
    private readonly StyleBoxFlat[] _prophecyCardStyles = new StyleBoxFlat[ProphecyOptionCount];
    private readonly Label[] _prophecySelectedBadges = new Label[ProphecyOptionCount];

    /// <summary>選択待ち（1 回目クリック済み）のカード番号。-1 = 未選択。2 回目の同カードで確定。</summary>
    private int _pendingProphecyIndex = -1;

    /// <summary>縦長カードのイラスト枠サイズ（約 3:4 のポートレート）。</summary>
    private const int CardArtWidth = 190;
    private const int CardArtHeight = 250;

    /// <summary>予言カード 3 枚を横に並べるときのカード間隔（px）。広めに空けて 1 枚ずつを際立たせる。</summary>
    private const int ProphecyCardSeparationPx = 40;

    /// <summary>カードの通常／選択中の地色（選択中は明るく浮かせる）。</summary>
    private static readonly Color CardBgColor = new(0.13f, 0.14f, 0.19f, 0.98f);
    private static readonly Color CardSelectedBgColor = new(0.26f, 0.30f, 0.40f, 1.0f);
    private const int CardBorderWidth = 3;
    private const int CardSelectedBorderWidth = 5;

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
        // ポイント残高・年代（ターン）・旅団年代記は GameDirector の固定ヘッダへ集約済み
        // （旅団年代記はヘッダの「📜 旅団年代記」ボタンから ChronicleLogOverlay として開く）。

        // ── ボディ：3 予言ボタン ───────────────────────────────────
        //  画面幅いっぱいの HBox 内でカード群を中央寄せ（左端詰めをやめる）。間隔はパラメータ化。
        var body = new HBoxContainer();
        body.Alignment = BoxContainer.AlignmentMode.Center;
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", ProphecyCardSeparationPx);
        body.SetMeta(TestIdMetaKey, "chronicle-prophecy-body");
        root.AddChild(body);

        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            int captured = i; // closure capture safety

            // カード自体は枠なし・透明の縦並び。枠（レア度色＋選択強調）は画像そのものに付ける。
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 6);
            card.SetMeta(TestIdMetaKey, $"chronicle-prophecy-card-{captured}");
            card.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin; // 縦に伸ばさず中身ぴったりの縦長カードに

            // 画像のフレーム：レア度色の枠を画像そのものに巻く PanelContainer。描画時に枠色・太さ・
            // 地色（選択強調）を差し替える。地色を少し覗かせる余白（ContentMargin）で枠が読みやすい。
            var artStyle = new StyleBoxFlat
            {
                BgColor     = CardBgColor,
                BorderColor = new Color(1.0f, 1.0f, 1.0f, 0.20f), // 既定枠色。描画時にレア度色へ差し替え。
            };
            artStyle.SetBorderWidthAll(CardBorderWidth);
            artStyle.SetCornerRadiusAll(12);
            artStyle.SetContentMarginAll(6);
            _prophecyCardStyles[i] = artStyle;

            // 画像フレームがクリックの的（選択は画像で行う）。枠＝レア度色、押下で OnProphecyCardSelected。
            var artFrame = new PanelContainer();
            artFrame.AddThemeStyleboxOverride("panel", artStyle);
            artFrame.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; // 枠を画像幅にぴったり・中央寄せ
            artFrame.MouseFilter = Control.MouseFilterEnum.Stop;                // クリックを拾う的
            artFrame.MouseDefaultCursorShape = Control.CursorShape.PointingHand; // 押せると分かるカーソル
            artFrame.GuiInput += (InputEvent ev) => OnProphecyArtGuiInput(captured, ev);
            artFrame.SetMeta(TestIdMetaKey, $"chronicle-prophecy-art-frame-{captured}");

            // カードのイラスト（予言種別ごと・縦長）。未配置なら null = 非表示（従来の文字表示のまま）。
            // クリックは親フレームへ通すため MouseFilter=Ignore。
            var art = new TextureRect
            {
                CustomMinimumSize = new Vector2(CardArtWidth, CardArtHeight),
                StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter       = Control.MouseFilterEnum.Ignore,
            };
            art.SetMeta(TestIdMetaKey, $"chronicle-prophecy-art-{captured}");
            artFrame.AddChild(art);
            card.AddChild(artFrame);
            _prophecyArt[i] = art;

            // 1 回目クリックで選択中になったことを示すバッジ（既定は非表示）。画像クリックで切り替わる。
            var badge = new Label
            {
                Text                = "👆 画像をもう一度クリックで決定",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize   = new Vector2(CardArtWidth, 0),
                Visible             = false,
            };
            badge.AddThemeColorOverride("font_color", new Color(1.0f, 0.93f, 0.55f));
            badge.SetMeta(TestIdMetaKey, $"chronicle-prophecy-selected-badge-{captured}");
            card.AddChild(badge);
            _prophecySelectedBadges[i] = badge;

            // 画像の下の情報（種別＋効果量＋タイムスキップ年数）。レア度は枠色で示すため明記しない。
            var detail = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize   = new Vector2(CardArtWidth, 0),
            };
            detail.SetMeta(TestIdMetaKey, $"chronicle-prophecy-detail-{captured}");
            card.AddChild(detail);

            _prophecyDetailLabels[i] = detail;
            body.AddChild(card);
        }
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.TimelineChanged   += OnTimelineChanged;
        _chronicleGlobal.StateInitialized  += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.TimelineChanged   -= OnTimelineChanged;
            _chronicleGlobal.StateInitialized  -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnStateInitialized()  => RenderAll();

    /// <summary>
    /// タイムライン更新シグナル。世代交代（Battle→Chronicle）でも必ず発火するので予言ボタンを
    /// 描き直す。旅団年代記の表示はヘッダの ChronicleLogOverlay へ移管済み（本画面は持たない）。
    /// </summary>
    private void OnTimelineChanged()
    {
        RenderProphecies();
    }

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        RenderProphecies();
    }

    private void RenderProphecies()
    {
        if (_chronicleGlobal is null) return;

        // 新しい予言を描き直すたびに、選択待ち状態はリセットする（前ターンの選択を持ち越さない）。
        _pendingProphecyIndex = -1;

        // 3 予言（選択は画像クリック・レア度は枠色で表現）
        var options = _chronicleGlobal.GetCurrentProphecies();
        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            var detail = _prophecyDetailLabels[i];
            if (detail is null) continue;

            if (i < options.Count)
            {
                var p = options[i];
                // 画像の下：予言種別＋効果量＋タイムスキップ年数（レア度は明記せず枠色で示す）。
                detail.Text =
                    $"{_chronicleGlobal.ResolveProphecyKindIcon(p.Kind)} {_chronicleGlobal.ResolveProphecyKindName(p.Kind)}\n" +
                    $"値: {p.Value}\n" +
                    $"⏳ {p.SkipYears} 年経過";
                // レア度（銅/銀/金）は画像を巻く枠の色で示す。選択中強調はリセット。
                if (_prophecyCardStyles[i] is { } style)
                {
                    style.BorderColor = RarityColor(p.Rarity);
                    style.BgColor = CardBgColor;
                    style.SetBorderWidthAll(CardBorderWidth);
                }
                if (_prophecySelectedBadges[i] is { } badge) badge.Visible = false;
                // カードのイラスト（予言種別ごと）。未配置なら null = 非表示。
                if (_prophecyArt[i] is { } art) art.Texture = ProphecyTextureLibrary.TryLoad(p.Kind);
            }
            else
            {
                detail.Text = "—";
                if (_prophecyCardStyles[i] is { } style)
                {
                    style.BorderColor = new Color(1.0f, 1.0f, 1.0f, 0.12f);
                    style.BgColor = CardBgColor;
                    style.SetBorderWidthAll(CardBorderWidth);
                }
                if (_prophecySelectedBadges[i] is { } badge) badge.Visible = false;
                if (_prophecyArt[i] is { } art) art.Texture = null;
            }
        }
    }

    /// <summary>
    /// 予言カードのレア度に応じた枠色。ブロンズは銅色（茶）、シルバーは涼やかな白銀、ゴールドは
    /// 黄金色。色は装飾でありゲームロジックには一切関与しない。未知レア度は白へ安全フォールバック。
    /// </summary>
    private static Color RarityColor(ProphecyRarity rarity) => rarity switch
    {
        ProphecyRarity.Gold   => new Color(1.0f, 0.86f, 0.35f),
        ProphecyRarity.Silver => new Color(0.82f, 0.88f, 1.0f),
        ProphecyRarity.Bronze => new Color(0.80f, 0.52f, 0.25f),
        _                     => Colors.White,
    };

    // ─── 選択アクション（画像クリック＝選択の的） ─────────────────────────────

    /// <summary>
    /// 予言カードの画像（フレーム）への GUI 入力ハンドラ。左クリックの押下のみを選択操作として拾い、
    /// 二段階選択（1 回目＝選択中／2 回目＝確定）の <see cref="OnProphecyCardSelected"/> へ橋渡しする。
    /// クリックは画像で行う（画像下のラベルは情報表示のみで、クリックの的ではない）。
    /// </summary>
    private void OnProphecyArtGuiInput(int index, InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            OnProphecyCardSelected(index);
        }
    }

    private void OnProphecyCardSelected(int index)
    {
        if (_chronicleGlobal is null) return;
        var options = _chronicleGlobal.GetCurrentProphecies();
        if (index < 0 || index >= options.Count) return;

        // 1 回目クリック（または別カードへの切替）＝「選択中」にするだけで、まだ次へ進めない。
        if (_pendingProphecyIndex != index)
        {
            _pendingProphecyIndex = index;
            ApplyPendingHighlight(index);
            return;
        }

        // 2 回目（同じカード）クリック＝確定して次へ進む。
        _pendingProphecyIndex = -1;
        var prophecyId = options[index].Id;
        var selected = _chronicleGlobal.SelectProphecyAndAdvance(prophecyId);

        // 結果ログ（再描画はシグナル経由で自動）
        if (selected is not null)
        {
            GD.Print($"[TimelineUI] 予言確定: {selected.Kind} (+{selected.SkipYears}年, 値={selected.Value})");
        }
    }

    /// <summary>
    /// 選択中（1 回目クリック済み）のカードを地色・枠太さ・バッジで強調し、他カードは通常表示へ戻す。
    /// 「一度クリックされたことが一目で分かる」ための視覚フィードバック。
    /// </summary>
    private void ApplyPendingHighlight(int selectedIndex)
    {
        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            bool on = i == selectedIndex;
            if (_prophecyCardStyles[i] is { } style)
            {
                style.BgColor = on ? CardSelectedBgColor : CardBgColor;
                style.SetBorderWidthAll(on ? CardSelectedBorderWidth : CardBorderWidth);
            }
            if (_prophecySelectedBadges[i] is { } badge) badge.Visible = on;
        }
    }

    // ─── ローカライゼーション ─────────────────────────────────────────────
    // 予言種別のアイコン・表示名は ChronicleGlobal.ResolveProphecyKindIcon /
    // ResolveProphecyKindName（内部で純粋層 MasterDataNameResolver が
    // localization_ja.json の prophecyKinds セクションを引く）に委譲する。
    // 本ファイルには日本語・絵文字を一切ハードコードしない（設計憲法 ①）。
}
