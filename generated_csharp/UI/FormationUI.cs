// =============================================================================
//  ChronicleKnights — FormationUI.cs
// -----------------------------------------------------------------------------
//  編成フェーズ画面（Control シーン）。V字 3×3 編成盤面の操作受け皿。
//
//  本画面は ChronicleGlobal が常駐保持する唯一の真実 (SoT) である
//  CurrentFormation（FormationBoard）を「丸ごと読み直して」9 マスへ描画するだけの
//  純粋な描画層であり、配置状態のキャッシュを一切持たない（設計憲法 ③）。
//
//  ★ 単方向データフロー（UI は状態を書き換えず、API を呼ぶだけ）:
//
//      クリック操作（マス／控え／回転ボタン）
//          │  ChronicleGlobal.PlaceUnitOnFormation / ClearFormationSlot /
//          │  SwapFormationSlots / RotateFormation を呼ぶだけ
//          ▼
//      ChronicleGlobal が _stateLock 内で盤面を不変差し替え
//          │  ロック解放後に FormationChanged を SafeEmit
//          ▼
//      本画面が FormationChanged を受信し CurrentFormation を読み直して再描画
//
//  ★ data-testid 規律:
//    各スロットには座標オブジェクトから機械的に組み立てた ASCII 文字列
//    （formation-slot-{Row}-{Column}）を data-testid メタとして付与する。
//    控えカード・回転ボタンも同様に formation-* の ASCII 命名を機械生成する。
//
//  シグナル購読:
//    - FormationChanged → 盤面（9 マス）と控えを再描画
//    - RosterChanged    → 控え（未配置の旅団員）を再描画（盤面整合も反映）
//    - StateInitialized → 全体再描画
//
//  クリーン設計:
//    - 略称（正式名称のみを使う方針）は本ファイルでも完全未使用
//    - 状態は ChronicleGlobal から読むだけで保持しない（_selectedUnitId は
//      「次に配置する控えユニット」を指す一過性の操作カーソルであり、盤面状態の
//      キャッシュではない。毎描画でロスタ／盤面に対して有効性を検証し直す）
//    - メモリリーク防止: _ExitTree で全シグナルを購読解除
//    - ジョブ名は ChronicleGlobal.ResolveJobName 経由（日本語ハードコード禁止・①）
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 編成フェーズ画面。V字 3×3 盤面への配置・除去・回転をクリックで受け付け、
/// 盤面の真実は常に ChronicleGlobal から読み直して描画する。
/// </summary>
public partial class FormationUI : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>戦闘に参加可能となる成人年齢（出陣可否表示のしきい値）。</summary>
    private const int BattleEligibleAge = 15;

    /// <summary>data-testid を載せるメタキー（Godot ノードメタ）。</summary>
    private const string TestIdMetaKey = "data_testid";

    // ─── Autoload 参照 ────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI 要素（_Ready でプログラマティック生成） ──────────────────────

    private Label? _summaryLabel;
    private VBoxContainer? _boardContainer;
    private VBoxContainer? _benchContainer;

    // ─── 操作カーソル（盤面状態ではなく「次に配置する控え」を指す一過性の値） ──

    private Guid? _selectedUnitId;

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

        root.AddChild(new Label { Text = "⚔ 大隊編成（V字3×3配置）" });

        _summaryLabel = new Label();
        root.AddChild(_summaryLabel);

        // ── 分隊ローテーション操作 ─────────────────────────────────
        var rotationRow = new HBoxContainer();
        rotationRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(rotationRow);

        var rotateCcw = new Button { Text = "⟲ 反時計回り" };
        rotateCcw.SetMeta(TestIdMetaKey, "formation-rotate-counter-clockwise");
        rotateCcw.Pressed += () => OnRotatePressed(RotationDirection.CounterClockwise);
        rotationRow.AddChild(rotateCcw);

        var rotateCw = new Button { Text = "⟳ 時計回り" };
        rotateCw.SetMeta(TestIdMetaKey, "formation-rotate-clockwise");
        rotateCw.Pressed += () => OnRotatePressed(RotationDirection.Clockwise);
        rotationRow.AddChild(rotateCw);

        // ── 配置盤面（9 マス。FormationChanged ごとに再構築） ──────
        root.AddChild(new Label { Text = "── 配置盤面 ──" });

        _boardContainer = new VBoxContainer();
        _boardContainer.AddThemeConstantOverride("separation", 8);
        root.AddChild(_boardContainer);

        // ── 控え（未配置の旅団員。RosterChanged ごとに再構築） ─────
        root.AddChild(new Label { Text = "── 控え（未配置の旅団員）──" });

        _benchContainer = new VBoxContainer();
        _benchContainer.AddThemeConstantOverride("separation", 4);
        root.AddChild(_benchContainer);

        root.AddChild(new Label
        {
            Text = "💡 控えを選び空きマスをクリックで配置 / 埋まったマスをクリックで外す",
        });
    }

    // ─── シグナル購読 / 解除 ──────────────────────────────────────────────

    private void SubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        _chronicleGlobal.FormationChanged += OnFormationChanged;
        _chronicleGlobal.RosterChanged    += OnRosterChanged;
        _chronicleGlobal.StateInitialized += OnStateInitialized;
    }

    private void UnsubscribeSignals()
    {
        if (_chronicleGlobal is null) return;
        try
        {
            _chronicleGlobal.FormationChanged -= OnFormationChanged;
            _chronicleGlobal.RosterChanged    -= OnRosterChanged;
            _chronicleGlobal.StateInitialized -= OnStateInitialized;
        }
        catch
        {
            // ノードが既に破棄されている場合の安全網（メモリリーク防止）
        }
    }

    // ─── シグナルハンドラ ─────────────────────────────────────────────────

    private void OnFormationChanged() => RenderAll();
    private void OnRosterChanged()    => RenderAll();
    private void OnStateInitialized() => RenderAll();

    // ─── 描画（すべて SoT を読み直して再構築。UI はキャッシュしない） ─────

    private void RenderAll()
    {
        if (_chronicleGlobal is null) return;

        // 操作カーソルの有効性を毎回 SoT に照らして検証し、無効なら捨てる。
        ValidateSelection();

        RenderSummary();
        RenderBoard();
        RenderBench();
    }

    /// <summary>
    /// 選択中の控えユニットが今も「実在・生存・未配置」かを検証し、
    /// 条件を満たさなければカーソルを解除する（盤面/ロスタ変化への自己整合）。
    /// </summary>
    private void ValidateSelection()
    {
        if (_chronicleGlobal is null || _selectedUnitId is not { } id) return;

        var unit = _chronicleGlobal.FindUnit(id);
        var placed = _chronicleGlobal.CurrentFormation.Contains(id);
        if (unit is null || !unit.IsAlive || placed)
        {
            _selectedUnitId = null;
        }
    }

    private void RenderSummary()
    {
        if (_chronicleGlobal is null || _summaryLabel is null) return;

        var board = _chronicleGlobal.CurrentFormation;
        var placed = board.OccupiedCount;
        _summaryLabel.Text = $"配置済み {placed} / {FormationBoard.SlotCount} マス";
    }

    private void RenderBoard()
    {
        if (_chronicleGlobal is null || _boardContainer is null) return;

        ClearChildren(_boardContainer);

        var board = _chronicleGlobal.CurrentFormation;

        // 行（分隊）ごとに「行ラベル + 3 マス」の横並びを縦に積む（正準順）。
        foreach (var row in FormationBoard.RowOrder)
        {
            var rowGroup = new HBoxContainer();
            rowGroup.AddThemeConstantOverride("separation", 8);

            rowGroup.AddChild(new Label
            {
                Text = $"[{row}]",
                CustomMinimumSize = new Vector2(96, 0),
            });

            for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
            {
                var coordinate = new SlotCoordinate(row, column);
                rowGroup.AddChild(BuildSlotButton(board, coordinate));
            }

            _boardContainer.AddChild(rowGroup);
        }
    }

    private Button BuildSlotButton(FormationBoard board, SlotCoordinate coordinate)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(160, 56),
        };
        button.SetMeta(TestIdMetaKey, SlotTestId(coordinate));

        var occupant = board.OccupantAt(coordinate);
        if (occupant is { } unitId)
        {
            button.Text = DescribeUnit(unitId);
        }
        else
        {
            button.Text = "＋";
        }

        button.Pressed += () => OnSlotPressed(coordinate);
        return button;
    }

    private void RenderBench()
    {
        if (_chronicleGlobal is null || _benchContainer is null) return;

        ClearChildren(_benchContainer);

        var board = _chronicleGlobal.CurrentFormation;
        var roster = _chronicleGlobal.BattalionRoster;

        foreach (var unit in roster)
        {
            // 生存していて、かつ盤面未配置の旅団員のみを控えに並べる。
            if (!unit.IsAlive) continue;
            if (board.Contains(unit.Id)) continue;

            var isSelected = _selectedUnitId == unit.Id;
            var marker = isSelected ? "▶ " : string.Empty;
            var eligibility = unit.Age >= BattleEligibleAge ? "出陣可" : "未成年";

            var benchButton = new Button
            {
                Text =
                    $"{marker}{_chronicleGlobal.ResolveDisplayName(unit)}  " +
                    $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]  " +
                    $"Lv{unit.Level}  Age {unit.Age}  ({eligibility})",
            };
            benchButton.SetMeta(TestIdMetaKey, $"formation-bench-card-{unit.Id}");

            var capturedId = unit.Id; // closure capture safety
            benchButton.Pressed += () => OnBenchPressed(capturedId);
            _benchContainer.AddChild(benchButton);
        }
    }

    // ─── クリック操作（すべて ChronicleGlobal の API を呼ぶだけ） ─────────

    /// <summary>
    /// 控えカード押下: その控えを「次に配置するユニット」として選択／選択解除する。
    /// これは盤面状態の変更ではないため API は呼ばず、ローカルに再描画して
    /// 選択マーカーを反映するに留める（盤面の真実は不変のまま）。
    /// </summary>
    private void OnBenchPressed(Guid unitId)
    {
        _selectedUnitId = (_selectedUnitId == unitId) ? null : unitId;
        RenderAll();
    }

    /// <summary>
    /// マス押下: 控えを選択中なら配置（上書き時は旧占有者が自動退去して控えに戻る）、
    /// 未選択なら除去（空席なら no-op）。いずれも API を呼ぶだけで、再描画は
    /// FormationChanged を経由して自動的に行われる（単方向フロー）。
    /// </summary>
    private void OnSlotPressed(SlotCoordinate coordinate)
    {
        if (_chronicleGlobal is null) return;

        if (_selectedUnitId is { } unitId)
        {
            _chronicleGlobal.PlaceUnitOnFormation(coordinate, unitId);
            _selectedUnitId = null; // 配置したらカーソルを離す
        }
        else
        {
            _chronicleGlobal.ClearFormationSlot(coordinate);
        }
    }

    /// <summary>回転ボタン押下: 分隊単位ローテーションを依頼するだけ。</summary>
    private void OnRotatePressed(RotationDirection direction)
    {
        _chronicleGlobal?.RotateFormation(direction);
    }

    // ─── 補助 ─────────────────────────────────────────────────────────────

    /// <summary>占有者 ID から「氏名 [ジョブ]」表示を組み立てる（ジョブ名は ① 準拠）。</summary>
    private string DescribeUnit(Guid unitId)
    {
        if (_chronicleGlobal is null) return unitId.ToString();

        var unit = _chronicleGlobal.FindUnit(unitId);
        if (unit is null) return unitId.ToString();

        return $"{_chronicleGlobal.ResolveDisplayName(unit)}\n[{_chronicleGlobal.ResolveJobName(unit.Job)}]";
    }

    /// <summary>座標から data-testid 用 ASCII 文字列を機械的に組み立てる。</summary>
    private static string SlotTestId(SlotCoordinate coordinate)
        => $"formation-slot-{coordinate.Row}-{coordinate.Column}";

    /// <summary>コンテナの全子ノードを破棄する（再構築前のクリア）。</summary>
    private static void ClearChildren(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            child.QueueFree();
        }
    }

    // ─── ローカライゼーション ─────────────────────────────────────────────
    // ジョブの表示名は ChronicleGlobal.ResolveJobName（内部で純粋層
    // MasterDataNameResolver が localization_ja.json の jobs.{JobId}.name を引く）に
    // 委譲する。本ファイルには日本語の「データ名」を一切ハードコードしない（① 準拠）。
    // 分隊行ラベルは現状 enum 名（ASCII）で表示しており、squadRows セクションの
    // ローカライズ解決は次段の拡張余地として残している。
}
