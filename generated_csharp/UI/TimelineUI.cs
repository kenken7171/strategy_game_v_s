// =============================================================================
//  ChronicleKnights — TimelineUI.cs
// -----------------------------------------------------------------------------
//  拠点A: 予言と歴史進行画面 (Control シーン)。
//
//  プレイヤーに提示される 3 つの予言を読み取り、3 つのボタンに反映する。
//  ボタンを押すと ChronicleGlobal.SelectProphecyAndAdvance(id) を呼んで
//  歴史を一気に進める（タイムスキップ + 定期収入 + 次予言再生成）。
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
//    - 日本語ラベルは localization_ja.json から引く設計の余白として、
//      FormatProphecyKind / FormatBalance 等のヘルパーに集約 (TODO 化)
//    - メモリリーク防止: _ExitTree で全シグナルを購読解除
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Timeline;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 予言タイムライン画面。プレイヤーは 3 択から 1 つを選んで歴史を進める。
/// </summary>
public partial class TimelineUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    private const int ProphecyOptionCount = 3;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private Label? _balanceLabel;
    private Label? _turnLabel;
    private readonly Button[] _prophecyButtons = new Button[ProphecyOptionCount];
    private readonly Label[] _prophecyDetailLabels = new Label[ProphecyOptionCount];

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
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 16);
        AddChild(root);

        // ── ヘッダー：タイトル + 残高 + ターン番号 ──────────────────
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 24);
        root.AddChild(header);

        var titleLabel = new Label
        {
            Text = "📖 予言タイムライン",
        };
        header.AddChild(titleLabel);

        _balanceLabel = new Label();
        header.AddChild(_balanceLabel);

        _turnLabel = new Label();
        header.AddChild(_turnLabel);

        // ── ボディ：3 予言ボタン ───────────────────────────────────
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        root.AddChild(body);

        for (int i = 0; i < ProphecyOptionCount; i++)
        {
            int captured = i; // closure capture safety

            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 4);

            var btn = new Button
            {
                CustomMinimumSize = new Vector2(220, 120),
            };
            btn.Pressed += () => OnProphecyButtonPressed(captured);
            card.AddChild(btn);

            var detail = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            card.AddChild(detail);

            _prophecyButtons[i] = btn;
            _prophecyDetailLabels[i] = detail;
            body.AddChild(card);
        }
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
    private void OnTimelineChanged()   => RenderProphecies();
    private void OnStateInitialized()  => RenderAll();

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        RenderBalance();
        RenderProphecies();
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
                btn.Text =
                    $"{FormatProphecyKindIcon(p.Kind)}\n" +
                    $"{FormatProphecyKindLabel(p.Kind)}\n" +
                    $"値: {p.Value}";
                detail.Text = $"⏳ {p.SkipYears} 年経過";
            }
            else
            {
                btn.Disabled = true;
                btn.Text = "—";
                detail.Text = "";
            }
        }
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

    // ─── ローカライゼーション補助（TODO: JSON 化の余白） ──────────────────
    // 将来 localization_ja.json をロードする LocalizationService を作成したら、
    // 以下のヘルパーは LocalizationService.Get(key) に置き換える。

    private static string FormatProphecyKindLabel(ProphecyKind kind) => kind switch
    {
        ProphecyKind.RewardPoints  => "報酬獲得",
        ProphecyKind.Battle        => "戦闘発生",
        ProphecyKind.ScoutReward   => "新人加入",
        ProphecyKind.EquipmentDrop => "装備入手",
        ProphecyKind.Rest          => "休息",
        _ => kind.ToString(),
    };

    private static string FormatProphecyKindIcon(ProphecyKind kind) => kind switch
    {
        ProphecyKind.RewardPoints  => "💰",
        ProphecyKind.Battle        => "⚔",
        ProphecyKind.ScoutReward   => "👥",
        ProphecyKind.EquipmentDrop => "📦",
        ProphecyKind.Rest          => "💤",
        _ => "❓",
    };
}
