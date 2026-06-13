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

    /// <summary>婚姻ポイント獲得の「コインがきらめく」一瞬のフラッシュ色（金）。</summary>
    private static readonly Color CoinFlashColor = new(1.0f, 0.86f, 0.32f);

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

        // ── 世代決算ダッシュボード（婚姻ポイント収支パネル・式の見える化） ──
        RenderEconomyPanel(outer, spoils);

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

    // ─── 世代決算ダッシュボード（婚姻ポイント収支パネル・式の見える化） ───────
    //
    //  自前の状態を 1 ミリも持たないパッシブビュー。BattleSpoils.DescribeMarriagePoints()
    //  という純粋な派生射影（Core の SoT から「その場で」展開される一過性の値）を読むだけで、
    //  「なぜこの点数になったか」の内訳（基礎値・昇級ボーナス・進化ボーナス・ロスト罰・合計）を
    //  更地描画する。経済 SoT（CurrentEconomy）はこの画面の表示時点ではまだ加算されていない
    //  （加算は本画面の Confirmed → BattleUI.AdvancePhase → ApplyBattleSpoils で起きる）ため、
    //  合計値は CalculateEarnedMarriagePoints の「予測（projected）」として提示し、残高行も
    //  「獲得後（予測）」として現残高 + 予測獲得を見せる。

    /// <summary>
    /// 婚姻ポイント収支パネルを構築する。勝利時は内訳の全行を、敗北・未決着時は
    /// 「獲得なし」の注記と合計 0 を描く。最後に勝利かつ獲得 &gt; 0 のときだけ
    /// JuiceDirector で「勝利の脳汁」（合計値ロールアップ + コイン色フラッシュ）を点火する。
    /// </summary>
    private void RenderEconomyPanel(VBoxContainer outer, BattleSpoils spoils)
    {
        var breakdown = spoils.DescribeMarriagePoints();

        var panel = new PanelContainer();
        panel.SetMeta(TestIdMetaKey, "spoils-economy-panel");
        outer.AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.SetMeta(TestIdMetaKey, "spoils-economy-root");
        panel.AddChild(root);

        var header = new Label { Text = "💍 婚姻ポイント決算（収支明細）" };
        header.AddThemeFontSizeOverride("font_size", 18);
        header.SetMeta(TestIdMetaKey, "spoils-economy-title");
        root.AddChild(header);

        // ── 勝利以外: 獲得なしを明記して合計 0 だけを機械可読に出す ───────────
        if (!breakdown.IsVictory)
        {
            var note = new Label { Text = "勝利に至らなかったため、婚姻ポイントの獲得はありません。" };
            note.AddThemeColorOverride("font_color", DeathHighlightColor);
            note.SetMeta(TestIdMetaKey, "spoils-economy-novictory");
            root.AddChild(note);

            AppendEconomyLine(
                root, "獲得婚姻ポイント 合計", $"+{breakdown.Total} pt",
                "spoils-economy-total-line", "spoils-economy-total-val", GainHighlightColor);
            return;
        }

        // ── 加点（基礎値・昇級・進化）と減点（ロスト）を式どおりに展開 ─────────
        AppendEconomyLine(
            root, "基礎値（勝ち切った報酬）", $"+{breakdown.VictoryBase}",
            "spoils-economy-base-line", "spoils-economy-base-val", GainHighlightColor);

        AppendEconomyLine(
            root, $"昇級ボーナス  {breakdown.LevelGainCount} 名 × {breakdown.LevelGainRate}",
            $"+{breakdown.LevelGainBonus}",
            "spoils-economy-bonus-line", "spoils-economy-bonus-val", GainHighlightColor);

        AppendEconomyLine(
            root, $"装備進化ボーナス  {breakdown.EvolutionCount} 件 × {breakdown.EvolutionRate}",
            $"+{breakdown.EvolutionBonus}",
            "spoils-economy-evolution-line", "spoils-economy-evolution-val", GainHighlightColor);

        AppendEconomyLine(
            root, $"完全ロスト罰  {breakdown.LossCount} 名 × {breakdown.LossRate}",
            $"-{breakdown.LossPenalty}",
            "spoils-economy-penalty-line", "spoils-economy-penalty-val", DeathHighlightColor);

        var separator = new HSeparator();
        separator.SetMeta(TestIdMetaKey, "spoils-economy-separator");
        root.AddChild(separator);

        // ── 合計（脳汁ロールアップの対象）─────────────────────────────────────
        var totalValue = AppendEconomyLine(
            root, "獲得婚姻ポイント 合計", $"+{breakdown.Total} pt",
            "spoils-economy-total-line", "spoils-economy-total-val", GainHighlightColor);
        totalValue.AddThemeFontSizeOverride("font_size", 20);

        // ── 残高の射影（現残高 → 獲得後・予測）。SoT はまだ加算前なので予測値を見せる ──
        var economy = _chronicleGlobal?.CurrentEconomy;
        if (economy is not null)
        {
            var current   = economy.CurrentBalance;
            var projected = current + breakdown.Total;
            AppendEconomyLine(
                root, "婚姻ポイント残高（獲得後・予測）", $"{current} → {projected}",
                "spoils-economy-balance-line", "spoils-economy-balance-val", GainHighlightColor);
        }

        // ── 演出点火: 勝利かつ獲得 > 0 のときだけ「勝利の脳汁」を脈動させる ─────
        IgniteEconomyJuice(panel, totalValue, breakdown.Total);
    }

    /// <summary>
    /// 「説明（左・伸長）／値（右）」の 1 行を収支パネルへ足し、機械可読のため行・値の
    /// 双方に ASCII testid を付ける。値ラベルを返すのは、合計行をロールアップ演出の対象へ
    /// 渡すため（呼び出し側が必要に応じて捕捉する）。
    /// </summary>
    private Label AppendEconomyLine(
        VBoxContainer root, string description, string valueText,
        string lineTestId, string valueTestId, Color valueColor)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        line.SetMeta(TestIdMetaKey, lineTestId);

        var desc = new Label { Text = description };
        desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(desc);

        var value = new Label
        {
            Text                = valueText,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.AddThemeColorOverride("font_color", valueColor);
        value.SetMeta(TestIdMetaKey, valueTestId);
        line.AddChild(value);

        root.AddChild(line);
        return value;
    }

    /// <summary>合計値のロールアップ表示に使う整形デリゲート（呼び出し側が文言を所有）。</summary>
    private static string FormatTotalPoints(int value) => $"+{value} pt";

    /// <summary>
    /// 「勝利の脳汁」を点火する。合計値ラベルを 0 → <paramref name="total"/> へカウントアップし、
    /// 収支パネル全体をコイン色へ一瞬フラッシュさせる（被弾シェイク/フラッシュと対をなす
    /// 最高峰のポジティブ・フィードバック）。
    ///
    /// ★ リークフリー: いずれの Tween も対象ノード（合計ラベル / 収支パネル）自身へ
    ///   バインドされる。本画面の QueueFree（OnClosePressed / _ExitTree）でこれらの子が
    ///   cascade 解放されると Tween は Godot 側で自動失効し、CountUp のコールバックは
    ///   IsInstanceValid ガードにより安全に no-op となる（Line2D で実証済みの規律）。
    ///   自前の状態（SoT）・台帳フィールドは一切増設しない。
    /// </summary>
    private void IgniteEconomyJuice(PanelContainer panel, Label totalValue, int total)
    {
        if (total <= 0)
        {
            return; // 獲得なし（敗北・損失過多）なら脳汁は焚かず静かに据え置く。
        }

        // コインの燐光: パネル全体を金色へ一瞬染め、白（恒等）へワンショットで戻す。
        JuiceDirector.Flash(panel, CoinFlashColor, 0.55);

        // 高揚感の本体: 合計値を 0 から total へ整数ロールアップ（わずかな溜めの後に駆け上がる）。
        JuiceDirector.CountUp(totalValue, 0, total, FormatTotalPoints, 0.85, 0.10);
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
