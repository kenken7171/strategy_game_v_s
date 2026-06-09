// =============================================================================
//  ChronicleKnights — BattleResultUI.cs
// -----------------------------------------------------------------------------
//  拠点C: 戦闘終了後のリザルト + ラストヒット (LH) 解決画面。
//
//  プレイヤーは「誰が今回の戦闘で LH を取ったか」を選び、
//  ChronicleGlobal.ResolveLastHit(unitId) を実行する。戻り値の LastHitResult を
//  解析し、冷徹な演出テキストでプレイヤーに突きつける。
//
//  表示する 4 つの結末:
//
//   ★ 1. ユニット成長 / 上限到達
//      ⬆  Lv1→Lv2 に成長！           （Lv<3 で +1）
//      💪 既に Lv3 — 無駄に流れた...   （オーバーフロー）
//
//   ★ 2. 装備のレベルアップ
//      ⬆ 誓いの聖剣 Lv2 → Lv3 に進化！  （Lv<5 で +1）
//
//   ★ 3. 装備 Lv5 の冷徹な破壊
//      💥 誓いの聖剣 (Lv5) がパリンと砕け散った... （50% に敗北）
//      🍀 誓いの聖剣 (Lv5) は奇跡的に生存！        （50% を乗り切った）
//
//   ★ 4. 強欲の古銭 Lv5 の特殊効果（脳汁演出！）
//      🪙💥 強欲の古銭がパリンと砕け散った...！
//      ✨ 特殊効果により大隊金庫に +1 pt を強奪！
//
//  画面構成:
//    ┌─────────────────────────────────────────────────┐
//    │ ⚔ 戦闘結果 / ラストヒット解決                  │
//    ├─────────────────────────────────────────────────┤
//    │ LH を取ったユニット: [選択 ▼]                  │
//    │ [🔥 確定: ラストヒット解決]                     │
//    ├─────────────────────────────────────────────────┤
//    │ ── 結果 ──                                       │
//    │ {演出メイン: ⬆ Lv2 / 💥 破壊 / ✨ 強奪 等}      │
//    │ {演出詳細: 注釈テキスト}                         │
//    └─────────────────────────────────────────────────┘
//
//  シグナル購読:
//    - RosterChanged    → セレクタ再生成（戦闘で死亡したユニットを除外）
//    - StateInitialized → 全体再描画
//    （EconomyChanged は表示しないため購読しない）
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 戦闘リザルト + ラストヒット解決画面。
/// </summary>
public partial class BattleResultUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>戦闘参加可能となる成人年齢（LH 候補のフィルタに使用）。</summary>
    private const int BattleEligibleAge = 15;

    /// <summary>結果メインラベルの演出アニメーション秒数（往復片道）。</summary>
    private const double ResultPunchDurationSec = 0.15;

    /// <summary>結果メインラベルの演出スケール最大倍率。</summary>
    private const float ResultPunchScalePeak = 1.3f;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private OptionButton? _lastHitUnitSelector;
    private Button? _executeResolveButton;
    private Label? _resultMainLabel;
    private Label? _resultDetailLabel;

    // ─── 内部状態 ─────────────────────────────────────────────────────────

    /// <summary>OptionButton index → Unit.Id のマッピング</summary>
    private readonly List<Guid> _selectableUnitIds = new();

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");
        BuildUI();
        SubscribeSignals();
        RenderSelector();
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

        root.AddChild(new Label { Text = "⚔ 戦闘結果 / ラストヒット解決" });

        // ─ LH 選択行 ────────────────────────────────────────────
        var selectorRow = new HBoxContainer();
        selectorRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(selectorRow);
        selectorRow.AddChild(new Label { Text = "LH を取ったユニット:" });
        _lastHitUnitSelector = new OptionButton();
        selectorRow.AddChild(_lastHitUnitSelector);

        // ─ 確定ボタン ───────────────────────────────────────────
        _executeResolveButton = new Button { Text = "🔥 確定: ラストヒット解決" };
        _executeResolveButton.Pressed += OnExecuteResolvePressed;
        root.AddChild(_executeResolveButton);

        // ─ 結果表示 ────────────────────────────────────────────
        root.AddChild(new Label { Text = "── 結果 ──" });

        _resultMainLabel = new Label
        {
            Text = "(まだ未解決)",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _resultMainLabel.AddThemeFontSizeOverride("font_size", 24);
        root.AddChild(_resultMainLabel);

        _resultDetailLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.AddChild(_resultDetailLabel);
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

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
            // ノードが既に破棄されている場合の安全網
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnRosterChanged()     => RenderSelector();
    private void OnStateInitialized()  => RenderSelector();

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderSelector()
    {
        if (_chronicleGlobal is null || _lastHitUnitSelector is null) return;

        // 既存の選択を保存
        var previousSelectedIndex = _lastHitUnitSelector.Selected;
        Guid? previousId = null;
        if (previousSelectedIndex >= 0 && previousSelectedIndex < _selectableUnitIds.Count)
        {
            previousId = _selectableUnitIds[previousSelectedIndex];
        }

        _lastHitUnitSelector.Clear();
        _selectableUnitIds.Clear();

        foreach (var unit in _chronicleGlobal.BattalionRoster)
        {
            if (!unit.IsAlive) continue;            // 死亡者は LH を取れない
            if (unit.Age < BattleEligibleAge) continue; // 子供は戦闘に出ない
            _lastHitUnitSelector.AddItem(FormatUnitDisplay(unit));
            _selectableUnitIds.Add(unit.Id);
        }

        // 以前の選択を復元
        if (previousId.HasValue)
        {
            for (int i = 0; i < _selectableUnitIds.Count; i++)
            {
                if (_selectableUnitIds[i] == previousId.Value)
                {
                    _lastHitUnitSelector.Selected = i;
                    break;
                }
            }
        }
    }

    // ─── アクションハンドラ ───────────────────────────────────────────────

    private void OnExecuteResolvePressed()
    {
        if (_chronicleGlobal is null) return;
        if (_lastHitUnitSelector is null) return;
        if (_resultMainLabel is null || _resultDetailLabel is null) return;

        int selectedIndex = _lastHitUnitSelector.Selected;
        if (selectedIndex < 0 || selectedIndex >= _selectableUnitIds.Count)
        {
            _resultMainLabel.Text = "❌ ユニットを選択してください";
            _resultDetailLabel.Text = "";
            return;
        }

        var unitId = _selectableUnitIds[selectedIndex];

        // 解決前のスナップショット（演出の比較用）
        var preUnit = _chronicleGlobal.FindUnit(unitId);
        var preEquipment = preUnit?.MainEquipment;

        // コアロジックへの委譲
        var result = _chronicleGlobal.ResolveLastHit(unitId);
        if (result is null)
        {
            _resultMainLabel.Text = "❌ 解決失敗（ユニット不在 / 未初期化）";
            _resultDetailLabel.Text = "";
            return;
        }

        DisplayResult(result, preUnit, preEquipment);
    }

    // ─── 結果演出 ────────────────────────────────────────────────────────

    /// <summary>
    /// LastHitResult を解析し、4 つのシナリオ（ユニット成長 / 装備強化 /
    /// Lv5 破壊（通常 or 強欲）/ Lv5 生存）の対応テキストをメイン・詳細ラベルに反映する。
    /// </summary>
    private void DisplayResult(LastHitResult result, Unit? preUnit, Equipment? preEquipment)
    {
        if (_resultMainLabel is null || _resultDetailLabel is null) return;

        var mainLines = new List<string>();
        var detailLines = new List<string>();

        // ── 1. ユニット成長 ──────────────────────────────────────
        if (result.LevelOverflow)
        {
            mainLines.Add("💪 ユニットは既に Lv3 — 経験値は無駄に流れた...");
        }
        else if (preUnit is not null && result.NewUnit.Level > preUnit.Level)
        {
            mainLines.Add($"⬆ ユニット Lv{preUnit.Level} → Lv{result.NewUnit.Level} に成長！");
        }

        // ── 2. 装備の進化 / 破壊 / 強欲 ─────────────────────────
        if (preEquipment is not null)
        {
            var itemDisplay = FormatItem(preEquipment.ItemId);

            if (result.ItemDestroyed)
            {
                if (result.GreedPointsStolen > 0)
                {
                    // CoinGreed Lv5: 100% 破壊 + ポイント強奪（脳汁演出！）
                    mainLines.Add($"🪙💥 {itemDisplay} がパリンと砕け散った...！");
                    mainLines.Add($"✨ 特殊効果により大隊金庫に +{result.GreedPointsStolen} pt を強奪！");
                    detailLines.Add("(古銭は完全ロスト、復元不可。だが強奪は成功した — ヒリつくぜ)");
                }
                else
                {
                    // 通常装備 Lv5: 50% 破壊
                    mainLines.Add($"💥 {itemDisplay} (Lv5) がパリンと砕け散った...！");
                    detailLines.Add("(50% コイントスに敗北。神器は完全ロスト)");
                }
            }
            else if (result.ItemLevelUpTriggered)
            {
                // Lv1〜4: 確定 +1
                var newLevel = result.NewUnit.MainEquipment?.Level ?? preEquipment.Level;
                mainLines.Add($"⬆ {itemDisplay} Lv{preEquipment.Level} → Lv{newLevel} に進化！");
            }
            else if (preEquipment.Level >= Equipment.MaxEquipmentLevel)
            {
                // Lv5 で itemLevelUpTriggered=false かつ itemDestroyed=false
                // → 50% コイントスを乗り切ったケース
                mainLines.Add($"🍀 {itemDisplay} (Lv5) は奇跡的に生存！");
                detailLines.Add("(50% コイントスを乗り切った — 神器の意志は強い)");
            }
        }

        // ── 何も起こらなかった場合 ─────────────────────────────
        if (mainLines.Count == 0)
        {
            mainLines.Add("(変化なし — トドメは取ったが成長要素なし)");
        }

        _resultMainLabel.Text   = string.Join("\n", mainLines);
        _resultDetailLabel.Text = string.Join("\n", detailLines);

        PunchAnimateMainLabel();
    }

    /// <summary>
    /// 結果メインラベルにパンチ感のあるスケールアニメーションを与える脳汁演出。
    /// Godot ランタイム外（テスト環境）では CreateTween が例外を投げる可能性が
    /// あるため、try/catch で完全に隔離する。
    /// </summary>
    private void PunchAnimateMainLabel()
    {
        if (_resultMainLabel is null) return;
        try
        {
            var tween = CreateTween();
            tween.TweenProperty(
                _resultMainLabel,
                "scale",
                new Vector2(ResultPunchScalePeak, ResultPunchScalePeak),
                ResultPunchDurationSec);
            tween.TweenProperty(
                _resultMainLabel,
                "scale",
                Vector2.One,
                ResultPunchDurationSec);
        }
        catch
        {
            // Godot 非依存テスト環境では演出をスキップ
        }
    }

    // ─── ヘルパー ─────────────────────────────────────────────────────────

    private static string FormatUnitDisplay(Unit unit)
    {
        var equip = unit.MainEquipment is null
            ? "[装備なし]"
            : $"[{FormatItem(unit.MainEquipment.ItemId)} Lv{unit.MainEquipment.Level}]";
        return $"{FormatJob(unit.Job)} Lv{unit.Level} (Age {unit.Age}) {equip}";
    }

    // ─── ローカライゼーション補助（TODO: JSON 化の余白） ──────────────────
    // 将来 localization_ja.json の jobs.{JobId}.name / items.{ItemId}.name を
    // 引く LocalizationService に置き換える。

    private static string FormatJob(JobId job) => job switch
    {
        JobId.IronWallKnight => "鉄壁騎士",
        JobId.HeavyInfantry  => "重装歩兵",
        JobId.StandardBearer => "旗手",
        JobId.Tactician      => "戦術官",
        JobId.Medic          => "衛生兵",
        JobId.Sniper         => "狙撃兵",
        JobId.Sorcerer       => "呪術師",
        JobId.Scout          => "斥候",
        _ => job.ToString(),
    };

    private static string FormatItem(ItemId item) => item switch
    {
        ItemId.SwordKnight  => "🛡️ 誓いの聖剣",
        ItemId.BowSniper    => "🎯 必中の魔弓",
        ItemId.StaffMage    => "⚡ 賢者の破滅杖",
        ItemId.RingPurelove => "💞 絆の誓輪",
        ItemId.CoinGreed    => "🪙 強欲の古銭",
        _ => item.ToString(),
    };
}
