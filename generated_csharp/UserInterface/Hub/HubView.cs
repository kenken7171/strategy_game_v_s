// =============================================================================
//  ChronicleKnights — UserInterface/Hub/HubView.cs
// -----------------------------------------------------------------------------
//  拠点画面（無状態 UI）。内政の本陣。タイトルから遷移して最初に立つ旅団の本拠。
//
//  ★ 完全な無状態 UI（変数保持の禁止・設計憲法③）:
//    年・婚姻ポイントといったゲーム数理を 1 つもキャッシュしない。表示する値はすべて SoT
//    （ChronicleGlobal）から「その場で」読み直してラベルへ一方通行で流し込む（Push バインド）。
//    SoT が動けば（EconomyChanged / TimelineChanged / StateInitialized）自動で読み直して描き直す。
//
//  ★ 婚姻経済ダッシュボード（脳汁の還流パイプ）:
//    CurrentEconomy の残高（Balance）・累計獲得（Earned）・累計消費（Spent）を常設パネルへ表示。
//    EconomyChanged で値が動いたら JuiceDirector.CountUp で数字をロールアップ（脳汁演出）させる。
//    ロールアップの始点はラベルが今表示している値（= ノードのレンダ状態）を読むため、UI 側に
//    ポイントのキャッシュ変数を持たない（無状態の徹底）。
//
//  ★ リークフリー:
//    シグナル購読は _Ready で張り _ExitTree で確実に解除する。CountUp の Tween は対象ラベル自身へ
//    バインドされ、本ビューの QueueFree で子ごと自動失効する。永続参照キャッシュ・自前 Tween 台帳は持たない。
//
//  ★ 開発憲法①（ASCII 限定）:
//    ノード名・testid・表示文言はすべて ASCII（"Year:" / "Balance:" / "Earned:" / "Spent:" 等）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using ChronicleKnights.UI;
using Godot;

namespace ChronicleKnights.UserInterface.Hub;

/// <summary>
/// SoT（ChronicleGlobal）から現在年と婚姻経済（残高・累計獲得・累計消費）をその場で読み直して
/// Push バインドするだけの、状態を持たない拠点ビュー。経済変動は CountUp でロールアップする。
/// </summary>
public partial class HubView : Godot.Control
{
    /// <summary>data-testid を載せる Godot メタキー。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>
    /// 「MARCH」押下で出撃が要求されたことを上位ルータ（UserInterfaceRoot）へ知らせる。
    /// 戦闘の SoT 起動（盤面着席 + StartBattle）は本ビューが済ませ、遷移だけを購読側へ委譲する
    /// （TitleView.StartRequested と同じ「身体が動かし、頭が遷移する」分離）。
    /// </summary>
    public event Action? BattleRequested;

    /// <summary>拠点の背景色（落ち着いた紺）。</summary>
    private static readonly Color BackdropColor = new(0.06f, 0.07f, 0.12f, 1.0f);

    /// <summary>経済ロールアップ（脳汁カウントアップ）の秒数。</summary>
    private const double RollupSeconds = 0.5;

    /// <summary>GamePhase 一巡の長さ（Chronicle/Guild/Formation/Battle）。Battle 前進ループの安全上限。</summary>
    private const int GamePhaseCycleLength = 4;

    private ChronicleGlobal? _chronicleGlobal;

    // ─── 表示ノード（SoT を流し込む先。ゲーム変数のキャッシュではない） ───────
    private Label? _yearLabel;
    private Label? _balanceValue;
    private Label? _earnedValue;
    private Label? _spentValue;

    /// <summary>現役旅団員カードを生やす土台（中身は再描画ごとに更地化して張り替える）。</summary>
    private VBoxContainer? _rosterContainer;

    /// <summary>動的生成した旅団員カードの台帳（再描画・退場で全 QueueFree）。</summary>
    private readonly List<Node> _rosterNodes = new();

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        SetMeta(TestIdMetaKey, "hub-view-root");

        BuildChrome();
        SubscribeSignals();

        // 初回は SoT を直接読んで Push（ロールアップなし。脳汁は以後の変動で焚く）。
        RenderYear();
        RenderEconomyDirect();
        RenderRoster();
    }

    public override void _ExitTree()
    {
        UnsubscribeSignals();
        ClearRosterNodes();
    }

    // ─── 不変クローム構築 ─────────────────────────────────────────────────

    private void BuildChrome()
    {
        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "hub-view-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        column.SetMeta(TestIdMetaKey, "hub-view-panel");
        margin.AddChild(column);

        var header = new Label { Text = "BASE HUB" };
        header.AddThemeFontSizeOverride("font_size", 32);
        header.SetMeta(TestIdMetaKey, "hub-view-header");
        column.AddChild(header);

        _yearLabel = new Label();
        _yearLabel.AddThemeFontSizeOverride("font_size", 20);
        _yearLabel.SetMeta(TestIdMetaKey, "hub-view-year");
        column.AddChild(_yearLabel);

        // ── 婚姻経済ダッシュボード（残高 / 累計獲得 / 累計消費） ────────────
        var economyPanel = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        economyPanel.AddThemeConstantOverride("separation", 6);
        economyPanel.SetMeta(TestIdMetaKey, "hub-view-economy-panel");
        column.AddChild(economyPanel);

        _balanceValue = AddEconomyRow(economyPanel, "Balance:", "hub-view-balance-value");
        _earnedValue  = AddEconomyRow(economyPanel, "Earned:",  "hub-view-earned-value");
        _spentValue   = AddEconomyRow(economyPanel, "Spent:",   "hub-view-spent-value");

        // ── 商店アクション（兵装購入 / 強化）。押下で Core 経済 API を直接叩く ──
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 12);
        actionRow.SetMeta(TestIdMetaKey, "hub-view-economy-actions");
        economyPanel.AddChild(actionRow);

        var buyButton = new Button { Text = "BUY" };
        buyButton.SetMeta(TestIdMetaKey, "hub-view-buy-button");
        buyButton.Pressed += OnBuyPressed;
        actionRow.AddChild(buyButton);

        var upgradeButton = new Button { Text = "UPGRADE" };
        upgradeButton.SetMeta(TestIdMetaKey, "hub-view-upgrade-button");
        upgradeButton.Pressed += OnUpgradePressed;
        actionRow.AddChild(upgradeButton);

        // ── 予言の赤き警告オーバーレイ（時間の矢 + 章ボス前兆。自前で無状態に SoT を読む） ──
        //    HubView は土台へ載せるだけ。購読・再描画・リークフリー更地化はオーバーレイ自身が司る
        //    （本ビューが QueueFree されれば子として芋づる解放され _ExitTree で購読解除される）。
        var prophecy = new ProphecyTimelineOverlay();
        column.AddChild(prophecy);

        // ── 現役旅団員ロスター（投資の着弾先。GetAliveUnits から台帳方式で動的生成） ──
        var rosterHeader = new Label { Text = "Roster:" };
        rosterHeader.AddThemeFontSizeOverride("font_size", 20);
        rosterHeader.SetMeta(TestIdMetaKey, "hub-view-roster-header");
        column.AddChild(rosterHeader);

        _rosterContainer = new VBoxContainer();
        _rosterContainer.AddThemeConstantOverride("separation", 6);
        _rosterContainer.SetMeta(TestIdMetaKey, "hub-view-roster-container");
        column.AddChild(_rosterContainer);

        // ── 出撃（武装した旅団を盤面へ解き放つ。押下で SoT を起動し戦場へ遷移） ──
        var marchButton = new Button { Text = "MARCH" };
        marchButton.SetMeta(TestIdMetaKey, "hub-view-march-button");
        marchButton.Pressed += OnMarchPressed;
        column.AddChild(marchButton);
    }

    /// <summary>
    /// 「説明（左・伸長）／値（右）」の経済 1 行を組み、値ラベル（ロールアップ対象）を返す。
    /// 値ラベルは数値文字列だけを持ち、その表示テキストが「今表示している値」の唯一の根拠になる。
    /// </summary>
    private Label AddEconomyRow(VBoxContainer panel, string prefixText, string valueTestId)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var prefix = new Label { Text = prefixText };
        prefix.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(prefix);

        var value = new Label
        {
            Text                = "0",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.SetMeta(TestIdMetaKey, valueTestId);
        row.AddChild(value);

        panel.AddChild(row);
        return value;
    }

    // ─── シグナル購読 / 解除（SoT 変更に追従して無状態に描き直す） ──────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.TimelineChanged  += OnTimelineChanged;
        _chronicleGlobal.EconomyChanged   += OnEconomyChanged;
        _chronicleGlobal.RosterChanged    += OnRosterChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.TimelineChanged  -= OnTimelineChanged;
            _chronicleGlobal.EconomyChanged   -= OnEconomyChanged;
            _chronicleGlobal.RosterChanged    -= OnRosterChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（リーク防止）。
        }
    }

    private void OnTimelineChanged() => RenderYear();

    /// <summary>経済変動: 現在表示値 → SoT の新値へロールアップ（脳汁カウントアップ）。</summary>
    private void OnEconomyChanged() => RollupEconomy();

    /// <summary>ロスター変動（採用・解雇・世代交代・装備購入/強化）: 旅団員カードを台帳更地化して張り替え。</summary>
    private void OnRosterChanged() => RenderRoster();

    /// <summary>新規開始（世界初期化）: ロールアップせず SoT を直接 Push（更地からの再起動）。</summary>
    private void OnStateInitialized()
    {
        RenderYear();
        RenderEconomyDirect();
        RenderRoster();
    }

    // ─── 商店アクション（Core 経済 API を直接叩くだけ・無状態） ────────────
    //   成功すると SoT 側が残高を減算し EconomyChanged を発火するため、拠点画面は
    //   OnEconomyChanged 経由で自動的にロールアップ再描画される（一気通貫の環）。

    private void OnBuyPressed()
    {
        if (_chronicleGlobal is null) return;

        var units = _chronicleGlobal.GetAliveUnits();
        if (units.Count == 0) return; // 対象不在なら no-op（API 側でも安全に弾かれる）。

        // 現役筆頭へ新品 Lv1 装備を購入（成功で残高 −BuyCost → EconomyChanged）。
        _chronicleGlobal.ExecuteBuyEquipment(units[0].Id, ItemId.SwordKnight);
    }

    private void OnUpgradePressed()
    {
        if (_chronicleGlobal is null) return;

        var units = _chronicleGlobal.GetAliveUnits();
        if (units.Count == 0) return;

        // 現役筆頭の装備を 1 段階強化（デフレ物価 BaseUpgradeCost=2 が SoT 経由で適用される）。
        _chronicleGlobal.ExecuteUpgradeEquipment(units[0].Id);
    }

    // ─── 出撃（盤面着席 → 現在年敵生成 → 開戦 → 戦場へ遷移要求） ───────────────

    /// <summary>
    /// 武装した旅団を盤面へ着席させ、現在年の時代スケール敵を生成して開戦し、戦場への遷移を要求する。
    /// SoT 起動（着席 + StartBattle）は本ビューが済ませ、画面遷移だけを BattleRequested で上位へ委譲する。
    /// </summary>
    private void OnMarchPressed()
    {
        if (_chronicleGlobal is null) return;

        SeatRosterForMarch();    // 専用編成フェーズが無い間の自動着席（現役筆頭から 9 マスへ）。
        AdvancePhaseToBattle();  // GamePhase 機械を Battle まで前進（決算の年送りが Battle→Chronicle で成立する前提）。

        var enemy = _chronicleGlobal.CreateCurrentYearEnemy(); // 現在年の章ボス/通常敵（正本ファクトリ）。
        _chronicleGlobal.StartBattle(enemy);                    // CurrentBattle を起こし BattleChanged を発火。

        BattleRequested?.Invoke(); // ルータへ「戦場を立てよ」（遷移は購読側に委譲）。
    }

    /// <summary>
    /// GamePhase 機械を Chronicle → Guild → Formation → Battle まで隣接前進させる（TryAdvanceTo は
    /// フェーズを進めるだけで年送り＝世代交代は伴わない）。これにより決算の AdvancePhase が
    /// Battle → Chronicle の年送りを正しく成立させられる。既に Battle なら何もしない（多重前進ガード）。
    /// </summary>
    private void AdvancePhaseToBattle()
    {
        if (_chronicleGlobal is null) return;

        for (var guard = 0; guard < GamePhaseCycleLength; guard++)
        {
            if (_chronicleGlobal.CurrentPhase == GamePhase.Battle) return;
            if (!_chronicleGlobal.TryAdvanceTo(GamePhaseFlow.Next(_chronicleGlobal.CurrentPhase))) return;
        }
    }

    /// <summary>
    /// 現役旅団員を盤面の正準順（Front/RearLeft/RearRight × 列 0..2）へ先頭から最大 9 名着席させる。
    /// CreateInitial は盤面に居る者だけを戦闘参加者に採るため、出撃前にここで盤面を満たす。
    /// </summary>
    private void SeatRosterForMarch()
    {
        if (_chronicleGlobal is null) return;

        var units = _chronicleGlobal.GetAliveUnits();
        var index = 0;

        foreach (var row in FormationBoard.RowOrder)
        {
            for (var column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                if (index >= units.Count) return;
                _chronicleGlobal.PlaceUnitOnFormation(new SlotCoordinate(row, column), units[index].Id);
                index++;
            }
        }
    }

    // ─── 描画（SoT をその場で読み直して Push バインド） ─────────────────────

    private void RenderYear()
    {
        if (_yearLabel is null) return;
        var year = _chronicleGlobal?.CurrentTimeline?.Turn ?? 0;
        _yearLabel.Text = "Year: " + year;
    }

    private void RenderEconomyDirect()
    {
        if (_balanceValue is null || _earnedValue is null || _spentValue is null) return;

        var economy = _chronicleGlobal?.CurrentEconomy;
        if (economy is null) return;

        _balanceValue.Text = FormatValue(economy.CurrentBalance);
        _earnedValue.Text  = FormatValue(economy.TotalEarned);
        _spentValue.Text   = FormatValue(economy.TotalSpent);
    }

    private void RollupEconomy()
    {
        if (_balanceValue is null || _earnedValue is null || _spentValue is null) return;

        var economy = _chronicleGlobal?.CurrentEconomy;
        if (economy is null) return;

        // 始点はラベルが今表示している値（= レンダ状態）を読む。UI に値をキャッシュしない。
        JuiceDirector.CountUp(_balanceValue, ShownValue(_balanceValue), economy.CurrentBalance, FormatValue, RollupSeconds);
        JuiceDirector.CountUp(_earnedValue,  ShownValue(_earnedValue),  economy.TotalEarned,    FormatValue, RollupSeconds);
        JuiceDirector.CountUp(_spentValue,   ShownValue(_spentValue),   economy.TotalSpent,     FormatValue, RollupSeconds);
    }

    // ─── 現役旅団員ロスター（GetAliveUnits から台帳方式で動的生成・更地化） ────

    /// <summary>
    /// SoT（GetAliveUnits）をその場で読み直し、旅団員カードを台帳方式で総張り替えする。
    /// 冒頭で必ず更地化するため、年次進行・採用・解雇・装備変動の再描画でカードが累積しない。
    /// </summary>
    private void RenderRoster()
    {
        if (_rosterContainer is null) return;

        ClearRosterNodes(); // 何より先に更地化（古いカードと購読を一掃）

        var units = _chronicleGlobal?.GetAliveUnits();
        if (units is null) return;

        foreach (var unit in units)
        {
            var card = BuildRosterCard(unit);
            _rosterContainer.AddChild(card);
            _rosterNodes.Add(card); // 台帳へ登録（更地化で一括解放）
        }
    }

    /// <summary>旅団員 1 名のカード（ジョブ・装備スロット・補正戦力）を組む。状態は持たない。</summary>
    private PanelContainer BuildRosterCard(Unit unit)
    {
        var card = new PanelContainer();
        card.SetMeta(TestIdMetaKey, $"hub-view-roster-card-{unit.Id}");

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        card.AddChild(row);

        // ジョブ（役割。enum 名の ASCII。憲法①: 日本語ハードコードなし）。
        var jobLabel = new Label { Text = unit.Job.ToString() };
        jobLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        jobLabel.SetMeta(TestIdMetaKey, $"hub-view-roster-card-job-{unit.Id}");
        row.AddChild(jobLabel);

        // 進化兵装スロット（EquippedItemId から逆算。BUY/UPGRADE 成功 → RosterChanged → 再描画で自動更新）。
        var equipLabel = new Label { Text = FormatEquipSlot(unit) };
        equipLabel.SetMeta(TestIdMetaKey, $"hub-view-roster-equip-slot-{unit.Id}");
        row.AddChild(equipLabel);

        // 装備補正後の現役戦力値（BattleManager の兵装ステータス補正と同式。投資の脳汁フィードバック）。
        var powerLabel = new Label { Text = "POWER: " + ComputeUnitPower(unit) };
        powerLabel.SetMeta(TestIdMetaKey, $"hub-view-roster-power-{unit.Id}");
        row.AddChild(powerLabel);

        // 年齢。
        var ageLabel = new Label { Text = "AGE " + unit.Age };
        ageLabel.SetMeta(TestIdMetaKey, $"hub-view-roster-card-age-{unit.Id}");
        row.AddChild(ageLabel);

        return card;
    }

    /// <summary>
    /// 進化兵装スロットの表示文字列を組む。EquippedItemId（= MainEquipment）から逆算し、
    /// 装備があれば "EQUIP: {item} LV{level}"、無ければ "EQUIP: NONE"（すべて ASCII）。
    /// UPGRADE 成功で MainEquipment.Level が増えると、再描画でこの表示が Lv1 → Lv2 と自動更新される。
    /// </summary>
    private static string FormatEquipSlot(Unit unit)
    {
        var equipment = unit.MainEquipment;
        return equipment is null
            ? "EQUIP: NONE"
            : "EQUIP: " + equipment.ItemId + " LV" + equipment.Level;
    }

    /// <summary>
    /// 装備補正後の現役戦力値（Power）を、Core の単一 SoT 式をそのまま再利用して算出する。
    /// 基礎戦力 = JobMaster の職ステータス（攻撃は配置非依存ゆえ前後の高い方／分隊守護／速度）、
    /// 装備補正 = BattleManager.Equipment{Attack,Defense,Speed}Bonus（ResolveOffenseDamage 等と同式）。
    /// 装備のレベルが上がると装備補正が増えるため、UPGRADE 成功で Power が底上げされる。
    /// </summary>
    private static int ComputeUnitPower(Unit unit)
    {
        var def = JobMaster.Find(unit.Job);
        var baseAttack  = def is null ? 0 : Math.Max(def.Stats.FrontAttack, def.Stats.RearAttack);
        var baseDefense = def is null ? 0 : def.Stats.SquadDefense;
        var baseSpeed   = def is null ? 0 : def.Stats.Speed;

        var equipmentPower = BattleManager.EquipmentAttackBonus(unit)
                           + BattleManager.EquipmentDefenseBonus(unit)
                           + BattleManager.EquipmentSpeedBonus(unit);

        return baseAttack + baseDefense + baseSpeed + equipmentPower;
    }

    /// <summary>台帳の全旅団員カードを一括解放して更地化する（再描画・退場で必ず呼ぶ）。</summary>
    private void ClearRosterNodes()
    {
        foreach (var node in _rosterNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _rosterNodes.Clear();
    }

    /// <summary>整数値を ASCII の数値文字列へ整形する（CountUp の整形デリゲート）。</summary>
    private static string FormatValue(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>ラベルが今表示している整数値を読む（パース不能なら 0）。レンダ状態が唯一の根拠。</summary>
    private static int ShownValue(Label label)
        => int.TryParse(label.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shown) ? shown : 0;
}
