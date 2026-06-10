// =============================================================================
//  ChronicleKnights — BattleSpoilsScreen.cs
// -----------------------------------------------------------------------------
//  戦果決算スクリーン（拠点へ帰還する直前の「この 1 戦で何が起きたか」の提示）。
//
//  ★ 完全な無状態 UI（設計憲法 ③・単方向データフロー）:
//    本画面は自前の状態を 1 ミリも持たない。_Ready で
//    ChronicleGlobal.LastBattleSpoils（純粋ファクトリ BattleSpoils.FromBattle が
//    開戦時 vs 終了時の Guid 突合で確定した不変レコード）を「読むだけ」で描画する。
//    戦果の算出・確定は一切しない（それは EndBattle のロック内で既に終わっている）。
//
//  ★ 案A のライフサイクル（世代交代の消滅副作用の「前」に提示する）:
//    BattleUI.OnEndPressed が EndBattle() で LastBattleSpoils を原子的に確定・公開した
//    直後、まだ AdvancePhase を呼ばずに本画面を前面展開する。プレイヤーが「次代へ」
//    （battle-spoils-close-button）を押した瞬間に Confirmed を 1 度だけ発火し、それを
//    購読する BattleUI が初めて AdvancePhase を駆動する。これにより、加齢・完全ロストの
//    掃き出しという「消滅」が起きる前に、死者と戦果を安全に記録・提示できる。
//
//    重要: 本画面が表示される時点で、戦死ユニットはまだ正本ロスタに（IsDead マーク付きで）
//    残っている（掃き出しは AdvancePhase → AdvanceGenerationLocked で起きる）。よって
//    FindUnit(Guid) で戦死者の氏名・ジョブを解決でき、「去る者」を名前付きで弔える。
//
//  ★ 日本語ハードコード方針（設計憲法 ①）:
//    ジョブ名・アイテム名・ユニット表示名という「データ名」は一切ハードコードせず、
//    ChronicleGlobal.ResolveJobName / ResolveItemName / ResolveDisplayName（内部で
//    localization の ASCII enum キーを引く）に委譲する。見出し等のクローム文字列は
//    既存 UI（BattleResultUI 等）と同じ方針で日本語を直書きする（憲法はクローム日本語を
//    許容）。Autoload 未取得時は enum 名・生キーへフォールバックして画面を落とさない。
//
//  ★ data-testid 規律:
//    E2E が検証できるよう、全セクション・全戦果行・閉じるボタンに機械生成 ASCII の
//    testid（battle-spoils-levelgain-{Guid} 等）を漏れなく付与する。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// ChronicleGlobal.LastBattleSpoils を単方向に読み取って描画するだけの、状態を持たない
/// 戦果決算モーダル。閉じるボタン押下で <see cref="Confirmed"/> を 1 度だけ発火し、
/// 自身を退場（QueueFree）する。
/// </summary>
public partial class BattleSpoilsScreen : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せるメタキー（Godot ノードメタ）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>モーダル本体の最小サイズ（戦果が多くてもスクロールで収める）。</summary>
    private static readonly Vector2 BodyMinimumSize = new(520, 360);

    /// <summary>暗幕の色（背後の戦闘画面を覆い隠すモーダル背景）。</summary>
    private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.72f);

    /// <summary>完全ロスト（戦死）行の強調色（赤）。「去る者」を際立たせる。</summary>
    private static readonly Color DeathHighlightColor = new(0.86f, 0.20f, 0.20f);

    /// <summary>昇級・装備進化という前向きな戦果の強調色（緑）。</summary>
    private static readonly Color GainHighlightColor = new(0.36f, 0.80f, 0.42f);

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 確定（次代へ）通知 ───────────────────────────────────────────────

    /// <summary>
    /// プレイヤーが「次代へ」を押して決算を見届けたときに 1 度だけ発火する。
    /// BattleUI がこれを購読し、初めて AdvancePhase（世代交代）を駆動する（案A）。
    /// </summary>
    public event Action? Confirmed;

    /// <summary>二重確定（連打・多重発火）を構造的に防ぐ一過性ガード。</summary>
    private bool _confirmed;

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;     // 背後の戦闘画面への入力を遮断（モーダル）
        SetMeta(TestIdMetaKey, "battle-spoils-root");

        BuildUI();
    }

    // ─── UI 構築（LastBattleSpoils を読むだけ・状態は持たない） ────────────

    private void BuildUI()
    {
        // 戦果決算の単一の真実を読む（未取得時は空決算へフォールバックして落ちない）。
        var spoils = _chronicleGlobal?.LastBattleSpoils ?? BattleSpoils.Empty;

        // ── 暗幕（フルレクト・入力遮断） ────────────────────────────
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "battle-spoils-backdrop");
        AddChild(backdrop);

        // ── 中央寄せラッパ → パネル ─────────────────────────────────
        var centerWrap = new CenterContainer();
        centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerWrap);

        var panel = new PanelContainer();
        panel.SetMeta(TestIdMetaKey, "battle-spoils-panel");
        centerWrap.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 12);
        panel.AddChild(outer);

        // ── タイトル + 決着 ─────────────────────────────────────────
        var title = new Label { Text = "⚔ 戦果決算" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.SetMeta(TestIdMetaKey, "battle-spoils-title");
        outer.AddChild(title);

        var outcomeLabel = new Label { Text = DescribeOutcome(spoils.Outcome) };
        outcomeLabel.SetMeta(TestIdMetaKey, "battle-spoils-outcome");
        outer.AddChild(outcomeLabel);

        // ── スクロール可能な本体（戦果が多くても収まる） ───────────
        var scroll = new ScrollContainer { CustomMinimumSize = BodyMinimumSize };
        outer.AddChild(scroll);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.SetMeta(TestIdMetaKey, "battle-spoils-body");
        scroll.AddChild(body);

        RenderSpoils(body, spoils);

        // ── 閉じる（次代へ）ボタン ──────────────────────────────────
        var closeButton = new Button { Text = "▶ 次代へ（OK）" };
        closeButton.SetMeta(TestIdMetaKey, "battle-spoils-close-button");
        closeButton.Pressed += OnClosePressed;
        outer.AddChild(closeButton);
    }

    /// <summary>
    /// 4 種の戦果（昇級 / 完全ロスト / 装備進化 / 装備喪失）を、それぞれ非空のときだけ
    /// セクションとして描く。いずれも無ければ「特筆すべき戦果なし」を優雅に出す。
    /// </summary>
    private void RenderSpoils(VBoxContainer body, BattleSpoils spoils)
    {
        if (!spoils.HasAnySpoils)
        {
            var empty = new Label { Text = "特筆すべき戦果なし" };
            empty.SetMeta(TestIdMetaKey, "battle-spoils-empty");
            body.AddChild(empty);
            return;
        }

        // ── 昇級 ────────────────────────────────────────────────────
        if (!spoils.UnitLevelGains.IsEmpty)
        {
            var section = AppendSection(body, "⬆ 昇級した英雄", "battle-spoils-levelgains-section");
            foreach (var gain in spoils.UnitLevelGains)
            {
                var row = new Label
                {
                    Text = $"{UnitLabel(gain.UnitId)}  Lv{gain.FromLevel} → Lv{gain.ToLevel}",
                };
                row.AddThemeColorOverride("font_color", GainHighlightColor);
                row.SetMeta(TestIdMetaKey, $"battle-spoils-levelgain-{gain.UnitId}");
                section.AddChild(row);
            }
        }

        // ── 装備の進化 ──────────────────────────────────────────────
        if (!spoils.EquipmentEvolutions.IsEmpty)
        {
            var section = AppendSection(body, "🔧 進化した武具", "battle-spoils-evolutions-section");
            foreach (var evolution in spoils.EquipmentEvolutions)
            {
                var row = new Label
                {
                    Text = $"{UnitLabel(evolution.UnitId)}  {ItemName(evolution.ItemId)} "
                           + $"Lv{evolution.FromLevel} → Lv{evolution.ToLevel}",
                };
                row.AddThemeColorOverride("font_color", GainHighlightColor);
                row.SetMeta(TestIdMetaKey, $"battle-spoils-evolution-{evolution.UnitId}");
                section.AddChild(row);
            }
        }

        // ── 完全ロスト（戦死）— 赤で強調 ───────────────────────────
        if (!spoils.PermanentlyLostUnitIds.IsEmpty)
        {
            var section = AppendSection(body, "💀 散った英雄（完全ロスト）", "battle-spoils-losses-section");
            foreach (var lostId in spoils.PermanentlyLostUnitIds)
            {
                var row = new Label { Text = $"💀 {UnitLabel(lostId)} — 永久に失われた" };
                row.AddThemeColorOverride("font_color", DeathHighlightColor);
                row.SetMeta(TestIdMetaKey, $"battle-spoils-loss-{lostId}");
                section.AddChild(row);
            }
        }

        // ── 装備の喪失（Lv5 破壊・戦死消失）— 赤で強調 ─────────────
        if (!spoils.EquipmentLosses.IsEmpty)
        {
            var section = AppendSection(body, "💥 失われた武具", "battle-spoils-equipmentlosses-section");
            foreach (var loss in spoils.EquipmentLosses)
            {
                var row = new Label { Text = $"💥 {UnitLabel(loss.UnitId)}  {ItemName(loss.ItemId)} を喪失" };
                row.AddThemeColorOverride("font_color", DeathHighlightColor);
                row.SetMeta(TestIdMetaKey, $"battle-spoils-equipmentloss-{loss.UnitId}");
                section.AddChild(row);
            }
        }
    }

    /// <summary>見出しラベルを持つ 1 セクションを本体へ足し、行追加用のコンテナを返す。</summary>
    private VBoxContainer AppendSection(VBoxContainer body, string headerText, string testId)
    {
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);
        section.SetMeta(TestIdMetaKey, testId);
        section.AddChild(new Label { Text = headerText });
        body.AddChild(section);
        return section;
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    /// <summary>
    /// 「次代へ」確定。Confirmed を 1 度だけ発火（BattleUI が AdvancePhase を駆動）し、
    /// モーダルは自身を退場させる。二重確定は _confirmed ガードで構造的に防ぐ。
    /// </summary>
    private void OnClosePressed()
    {
        if (_confirmed) return;
        _confirmed = true;

        Confirmed?.Invoke();   // 購読側（BattleUI）が初めて世代交代へ前進する
        QueueFree();           // モーダルはフレーム末で除去（前面から退場）
    }

    // ─── ローカライゼーション（データ名は必ずリゾルバ経由） ────────────────

    /// <summary>ユニットを「氏名 [ジョブ]」で表現する。戦死者も AdvancePhase 前なので解決可能。</summary>
    private string UnitLabel(Guid unitId)
    {
        var unit = _chronicleGlobal?.FindUnit(unitId);
        if (unit is null) return unitId.ToString();
        return $"{ResolveDisplayName(unit)} [{ResolveJobName(unit.Job)}]";
    }

    private string ResolveDisplayName(Unit unit)
        => _chronicleGlobal?.ResolveDisplayName(unit) ?? unit.FirstNameKey;

    private string ResolveJobName(JobId job)
        => _chronicleGlobal?.ResolveJobName(job) ?? job.ToString();

    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();

    /// <summary>決着状態のクローム文言（データ名ではないので日本語直書き・憲法①の許容範囲）。</summary>
    private static string DescribeOutcome(BattleOutcome outcome) => outcome switch
    {
        BattleOutcome.BattalionVictory => "決着: 大隊の勝利",
        BattleOutcome.BattalionDefeat  => "決着: 大隊の敗北",
        _                              => "戦闘終了",
    };
}
