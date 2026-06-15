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
using ChronicleKnights.Core.Managers;
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

    // 兵装スロット（無償脱着）セクションのリストコンテナ
    private VBoxContainer? _equipListContainer;

    // ─── 操作カーソル（盤面状態ではなく「次に配置する控え」を指す一過性の値） ──

    private Guid? _selectedUnitId;

    /// <summary>
    /// 兵装脱着ダイアログを開いている行の対象 Id（一過性の操作カーソル）。null なら全行が
    /// 閉じている。これは盤面状態ではなく「いまどの行のダイアログを開いているか」だけを指す。
    /// Roster 再描画をまたいでも保持する（再描画で誤って閉じないため）。
    /// </summary>
    private Guid? _pendingEquipId;

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
        scroll.SetMeta(TestIdMetaKey, "formation-scroll");
        AddChild(scroll);

        var root = new VBoxContainer();
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddThemeConstantOverride("separation", 16);
        root.SetMeta(TestIdMetaKey, "formation-root");
        scroll.AddChild(root);

        var titleLabel = new Label { Text = "⚔ 大隊編成（V字3×3配置）" };
        titleLabel.SetMeta(TestIdMetaKey, "formation-title");
        root.AddChild(titleLabel);

        _summaryLabel = new Label();
        _summaryLabel.SetMeta(TestIdMetaKey, "formation-summary");
        root.AddChild(_summaryLabel);

        // ── 分隊ローテーション操作 ─────────────────────────────────
        var rotationRow = new HBoxContainer();
        rotationRow.AddThemeConstantOverride("separation", 12);
        rotationRow.SetMeta(TestIdMetaKey, "formation-rotation-row");
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
        var boardSectionLabel = new Label { Text = "── 配置盤面 ──" };
        boardSectionLabel.SetMeta(TestIdMetaKey, "formation-board-section-label");
        root.AddChild(boardSectionLabel);

        _boardContainer = new VBoxContainer();
        _boardContainer.AddThemeConstantOverride("separation", 8);
        _boardContainer.SetMeta(TestIdMetaKey, "formation-board");
        root.AddChild(_boardContainer);

        // ── 控え（未配置の旅団員。RosterChanged ごとに再構築） ─────
        var benchSectionLabel = new Label { Text = "── 控え（未配置の旅団員）──" };
        benchSectionLabel.SetMeta(TestIdMetaKey, "formation-bench-section-label");
        root.AddChild(benchSectionLabel);

        _benchContainer = new VBoxContainer();
        _benchContainer.AddThemeConstantOverride("separation", 4);
        _benchContainer.SetMeta(TestIdMetaKey, "formation-bench");
        root.AddChild(_benchContainer);

        var hintLabel = new Label
        {
            Text = "💡 控えを選び空きマスをクリックで配置 / 埋まったマスをクリックで外す",
        };
        hintLabel.SetMeta(TestIdMetaKey, "formation-hint");
        root.AddChild(hintLabel);

        // ── 兵装スロット（無償脱着・無状態。RosterChanged ごとに再構築） ─────
        // 現役全員（配置済み・控え問わず）に対し、SoT の EquippedItemId を毎描画で読み直して
        // 兵装スロットを並べる。スロット押下で 5 大マスター + 外す の脱着ダイアログを展開し、
        // ChronicleGlobal.EquipItem / UnequipItem（無償）を単方向に呼ぶだけ（UI は状態を持たない）。
        var equipSection = new VBoxContainer();
        equipSection.SetMeta(TestIdMetaKey, "roster-equip-section");
        root.AddChild(equipSection);

        var equipTitle = new Label { Text = "── 🛡 兵装スロット（無償脱着） ──" };
        equipTitle.SetMeta(TestIdMetaKey, "roster-equip-title");
        equipSection.AddChild(equipTitle);

        var equipHint = new Label
        {
            Text = "💡 スロットを押して兵装を着脱（緑の数値が装備中の ATK/DEF/SPD 補正）",
        };
        equipHint.SetMeta(TestIdMetaKey, "roster-equip-hint");
        equipSection.AddChild(equipHint);

        _equipListContainer = new VBoxContainer();
        _equipListContainer.AddThemeConstantOverride("separation", 4);
        _equipListContainer.SetMeta(TestIdMetaKey, "roster-equip-list");
        equipSection.AddChild(_equipListContainer);
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
        RenderEquipmentSlots();
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
            rowGroup.SetMeta(TestIdMetaKey, $"formation-row-{row}");

            var rowLabel = new Label
            {
                Text = $"[{row}]",
                CustomMinimumSize = new Vector2(96, 0),
            };
            rowLabel.SetMeta(TestIdMetaKey, $"formation-row-label-{row}");
            rowGroup.AddChild(rowLabel);

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

    /// <summary>
    /// 兵装スロット（無償脱着）セクションを、現在の生存者から無状態に再構築する。
    ///
    /// 各行は次の 3 つを常設する（SoT を一切キャッシュせず毎描画で読み直す）:
    ///   - 氏名 [ジョブ]      … ① 準拠で Resolve 経由。
    ///   - 兵装スロット        … <see cref="Unit.MainEquipment"/>（= EquippedItemId の出所）を
    ///                            パッシブに読み、装備名 Lv を表示。押下でこの行の脱着ダイアログを開閉。
    ///   - 戦力プレビュー      … <see cref="BattleManager"/> の floor 整数補正（ATK/DEF/SPD）。
    ///                            装備時のみ緑でハイライト（色価言語：プラス補正の発火を一目で）。
    ///
    /// 武装状態（<see cref="_pendingEquipId"/> 一致）の行だけ、5 大マスターの装着ボタン群＋[外す]＋
    /// [やめる] を同じ行へ展開する（行内インラインの脱着ダイアログ）。装着・取り外しは
    /// ChronicleGlobal.EquipItem / UnequipItem（無償）を単方向に呼ぶだけで、UI は状態を書き換えない。
    ///
    /// リークフリー: 再構築の冒頭で既存行を <see cref="ClearChildren"/>（QueueFree）して
    /// ゾンビ行を残さない。購読解除は <see cref="_ExitTree"/> が一括で担う。
    /// </summary>
    private void RenderEquipmentSlots()
    {
        if (_chronicleGlobal is null || _equipListContainer is null) return;

        // 既存行を破棄してから現在の生存者で組み直す（ゾンビ行を残さない＝リークフリー）。
        ClearChildren(_equipListContainer);

        var alive = _chronicleGlobal.GetAliveUnits();
        if (alive.Count == 0)
        {
            var empty = new Label { Text = "（兵装を着脱できる現役がいません）" };
            empty.SetMeta(TestIdMetaKey, "roster-equip-empty");
            _equipListContainer.AddChild(empty);
            return;
        }

        foreach (var unit in alive)
        {
            var capturedId = unit.Id;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.SetMeta(TestIdMetaKey, $"roster-equip-row-{capturedId}");

            // 氏名・ジョブ（① 準拠で Resolve 経由・日本語データ名はハードコードしない）。
            var name = new Label
            {
                Text = $"[{_chronicleGlobal.ResolveJobName(unit.Job)}] "
                       + _chronicleGlobal.ResolveDisplayName(unit),
            };
            name.SetMeta(TestIdMetaKey, $"roster-equip-name-{capturedId}");
            row.AddChild(name);

            // 兵装スロット（無状態：描画の瞬間に SoT の MainEquipment／EquippedItemId を
            // パッシブに読むだけ）。押下でこの行の脱着ダイアログを開閉する操作トリガ。
            var equip = unit.MainEquipment;
            var slotButton = new Button
            {
                Text = equip is null
                    ? "兵装スロット: —"
                    : $"兵装スロット: {ItemName(equip.ItemId)} Lv{equip.Level}",
            };
            slotButton.SetMeta(TestIdMetaKey, $"roster-equip-slot-{capturedId}");
            slotButton.Pressed += () => OnEquipSlotPressed(capturedId);
            row.AddChild(slotButton);

            // 戦力プレビュー：装備由来 ATK/DEF/SPD ボーナス（floor 整数）を即時可視化する。
            // 数値は常に正直（未装備なら 0）。兵装を帯びている時だけ緑でハイライトする。
            var atkBonus = BattleManager.EquipmentAttackBonus(unit);
            var defBonus = BattleManager.EquipmentDefenseBonus(unit);
            var spdBonus = BattleManager.EquipmentSpeedBonus(unit);
            var preview = new Label
            {
                Text = $"ATK +{atkBonus}  DEF +{defBonus}  SPD +{spdBonus}",
            };
            preview.SetMeta(TestIdMetaKey, $"roster-equip-stats-{capturedId}");
            if (equip is not null)
            {
                preview.AddThemeColorOverride("font_color", Colors.LightGreen);
            }
            row.AddChild(preview);

            // 武装状態の行だけ、5 大マスター＋[外す]＋[やめる] の脱着ダイアログを同じ行へ展開する。
            if (_pendingEquipId == capturedId)
            {
                foreach (var item in Enum.GetValues<ItemId>())
                {
                    var capturedItem = item;
                    var pickButton = new Button { Text = ItemName(item) };
                    pickButton.SetMeta(TestIdMetaKey, $"roster-equip-pick-{item}");
                    pickButton.Pressed += () => OnEquipPickPressed(capturedId, capturedItem);
                    row.AddChild(pickButton);
                }

                var unequipButton = new Button { Text = "外す" };
                unequipButton.SetMeta(TestIdMetaKey, $"roster-unequip-btn-{capturedId}");
                unequipButton.Pressed += () => OnUnequipPressed(capturedId);
                row.AddChild(unequipButton);

                var cancelButton = new Button { Text = "やめる" };
                cancelButton.SetMeta(TestIdMetaKey, $"roster-equip-cancel-{capturedId}");
                cancelButton.Pressed += OnEquipCancelPressed;
                row.AddChild(cancelButton);
            }

            _equipListContainer.AddChild(row);
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

    // ─── 兵装スロット（無償脱着）ハンドラ ─────────────────────────────────

    /// <summary>
    /// 兵装スロット押下: この行の脱着ダイアログを開閉する（同じスロット再押下で閉じるトグル）。
    /// これは盤面状態の変更ではなく一過性の操作カーソル更新なので API は呼ばず、当該セクションのみ
    /// 再描画してダイアログの開閉を反映する（盤面・ロスタの真実は不変のまま）。
    /// </summary>
    private void OnEquipSlotPressed(Guid unitId)
    {
        _pendingEquipId = (_pendingEquipId == unitId) ? null : unitId;
        RenderEquipmentSlots();
    }

    /// <summary>
    /// 装着ボタン（5 大マスターのいずれか）押下: ChronicleGlobal.EquipItem で無償装着する。
    /// 成功時は RosterChanged 経由で全体が自動再描画されるため、ここでは武装解除だけ行う。
    /// 失敗（未初期化／対象不在）時はシグナルが飛ばないので手動で再描画してダイアログを畳む。
    /// </summary>
    private void OnEquipPickPressed(Guid unitId, ItemId itemId)
    {
        if (_chronicleGlobal is null) return;

        // 武装は結果に関わらず解除（成功なら行が組み変わり、失敗なら通常表示へ戻る）。
        _pendingEquipId = null;

        var affected = _chronicleGlobal.EquipItem(unitId, itemId);
        if (affected is null)
        {
            GD.Print($"[FormationUI] equip failed (uninitialized or unit not found): Id={unitId} Item={itemId}");
            RenderEquipmentSlots(); // 失敗時はシグナル無し → 手動で武装解除を反映。
            return;
        }

        GD.Print($"[FormationUI] equip {ItemName(itemId)} -> Id={unitId}");
        // 成功時の再描画は RosterChanged シグナル経由で自動（単方向フロー）。
    }

    /// <summary>
    /// [外す] 押下: ChronicleGlobal.UnequipItem で無償取り外しする。元から未装備（RosterMutated=false）
    /// の場合はシグナルが飛ばないため、結果に関わらずここで必ず再描画してダイアログを畳む
    /// （装備ありの成功時はシグナルと二重描画になり得るが、本メソッドは冪等・リークフリーで安全）。
    /// </summary>
    private void OnUnequipPressed(Guid unitId)
    {
        if (_chronicleGlobal is null) return;

        _pendingEquipId = null;

        var affected = _chronicleGlobal.UnequipItem(unitId);
        GD.Print(affected is null
            ? $"[FormationUI] unequip failed (uninitialized or unit not found): Id={unitId}"
            : $"[FormationUI] unequip -> Id={unitId}");

        RenderEquipmentSlots();
    }

    /// <summary>[やめる] 押下: 脱着ダイアログを畳むだけ（何も着けない・外さない）。</summary>
    private void OnEquipCancelPressed()
    {
        _pendingEquipId = null;
        RenderEquipmentSlots();
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

    /// <summary>
    /// 装備種別の表示名を ChronicleGlobal.ResolveItemName（内部で純粋層 MasterDataNameResolver が
    /// localization_ja.json の items.{ItemId}.name を引く）に委譲する。Autoload 未取得時は enum 名
    /// （ASCII）へフォールバックして画面を落とさない（① 準拠・日本語データ名を持たない）。
    /// </summary>
    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();
}
