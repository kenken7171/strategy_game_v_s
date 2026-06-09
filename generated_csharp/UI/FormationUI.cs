// =============================================================================
//  ChronicleKnights — FormationUI.cs
// -----------------------------------------------------------------------------
//  編成フェーズ画面（Control シーン）。
//
//  大隊（最大 9 名）の現在の顔ぶれを一覧し、戦闘前の確認を行う最小構成の画面。
//  詳細な V 字配置（3×3 のスロット指定）は将来のフロンティアとして残し、本画面は
//  「現在の大隊員を表示し、上部ディレクターの『次へ』で戦闘解決フェーズへ進む」
//  という循環の結節点を担う。
//
//  ★ 名前解決の実演:
//    各行のユニット名は ChronicleGlobal.ResolveDisplayName(unit) で解決する。
//    複合キー（称号付き）は「称号 ＋ 名前 ＋ 姓」へ自動連結された日本語で表示される。
//
//  シグナル購読:
//    - RosterChanged    → 大隊員リストを再描画
//    - StateInitialized → 全体再描画
//
//  クリーン設計:
//    - 略称（大隊総守護力などの正式名称のみを使う方針）は本ファイルでも完全未使用
//    - 状態は ChronicleGlobal から読むだけで保持しない（SoT 違反防止）
//    - メモリリーク防止: _ExitTree で全シグナルを購読解除
// =============================================================================

using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 編成フェーズ画面。大隊の現在の顔ぶれを一覧する。
/// </summary>
public partial class FormationUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>戦闘に参加可能となる成人年齢（出陣可否表示のしきい値）。</summary>
    private const int BattleEligibleAge = 15;

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素 ──────────────────────────────────────────────────────────

    private Label? _summaryLabel;
    private VBoxContainer? _rosterContainer;

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

        root.AddChild(new Label { Text = "⚔ 大隊編成" });

        _summaryLabel = new Label();
        root.AddChild(_summaryLabel);

        root.AddChild(new Label
        {
            Text = "── 大隊の顔ぶれ ──",
        });

        _rosterContainer = new VBoxContainer();
        _rosterContainer.AddThemeConstantOverride("separation", 4);
        root.AddChild(_rosterContainer);

        root.AddChild(new Label
        {
            Text = "💡 編成を確認したら、上部の『次へ』で戦闘解決フェーズへ進む",
        });
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
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnRosterChanged()    => RenderAll();
    private void OnStateInitialized() => RenderAll();

    // ─── 描画 ─────────────────────────────────────────────────────────────

    private void RenderAll()
    {
        if (_chronicleGlobal is null) return;
        if (_summaryLabel is null || _rosterContainer is null) return;

        // サマリー（生存数 / 在籍数）
        var roster = _chronicleGlobal.BattalionRoster;
        var aliveCount = 0;
        foreach (var unit in roster)
        {
            if (unit.IsAlive) aliveCount++;
        }
        _summaryLabel.Text = $"在籍 {roster.Count} 名 / 生存 {aliveCount} 名";

        // 既存行をクリアして再構築
        foreach (var child in _rosterContainer.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var unit in roster)
        {
            if (!unit.IsAlive) continue;

            var displayName = _chronicleGlobal.ResolveDisplayName(unit);
            var eligibility = unit.Age >= BattleEligibleAge ? "出陣可" : "未成年";
            var row = new Label
            {
                Text =
                    $"・{displayName}  " +
                    $"[{FormatJob(unit.Job)}]  Lv{unit.Level}  Age {unit.Age}  ({eligibility})",
            };
            _rosterContainer.AddChild(row);
        }
    }

    // ─── ローカライゼーション補助（TODO: JSON 化の余白） ──────────────────
    // 将来 localization_ja.json の jobs.{JobId}.name を引く LocalizationService に
    // 置き換える。現状は他 UI（MarriageUI / BattleResultUI）と同じ変換規約に揃える。

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
}
