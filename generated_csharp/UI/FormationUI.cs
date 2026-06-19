// =============================================================================
//  ChronicleKnights — FormationUI.cs
// -----------------------------------------------------------------------------
//  Battalion formation phase screen (Control). Faithful trace of the TS web
//  formation screen (packages/frontend/src/phases/BattalionFormation/
//  BattalionFormationPage.tsx): the wedge ("V") layout where the FRONT squad
//  sits centered on top and the two REAR squads sit below, plus drag-and-drop
//  placement (the delta behavior now lives here in the live screen; the former
//  dormant HubView prototype has been removed).
//
//  The Core board stays a 3x3 FormationBoard (Front / RearLeft / RearRight x
//  col 0..2 = 9 slots). The wedge is purely a UI PRESENTATION over those 9
//  slots, so rotation / BattleResolver / golden balance / the 626 xUnit tests
//  are untouched.
//
//  Single SoT + one-way data flow (Constitution III): the UI never caches board
//  state. Drag-drop / clear / rotate only CALL ChronicleGlobal APIs; the screen
//  re-reads CurrentFormation and re-renders on FormationChanged / RosterChanged.
//
//  Constitution I: ASCII only for identifiers, node names, testids and logs.
//  Job / item display names are resolved via ChronicleGlobal (localization).
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;          // Gender (job art read)
using ChronicleKnights.Core.Units;
using ChronicleKnights.UserInterface;         // JobTextureLibrary
using ChronicleKnights.UserInterface.Hub;     // FormationDragPayload / RosterDragCard / FormationSlotControl
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// Formation phase screen. Renders the 9 board slots as a wedge (FRONT squad on
/// top, REAR-L / REAR-R below) and accepts drag-and-drop: roster card -> slot
/// (place), slot -> slot (swap). The board truth is always re-read from
/// ChronicleGlobal.CurrentFormation; this screen holds no placement state.
/// </summary>
public partial class FormationUI : Godot.Control
{
    // ─── Constants ──────────────────────────────────────────────────────────

    /// <summary>Adult age at which a unit may march (eligibility hint).</summary>
    private const int BattleEligibleAge = 15;

    /// <summary>Meta key that carries the data-testid on each node.</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>Canonical wedge row order (FRONT on top, then the two REAR squads).</summary>
    private static readonly SquadRow[] RowOrder =
        { SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight };

    /// <summary>Bench (unplaced roster) grid width: rich cards laid out 4 across.</summary>
    private const int BenchColumns = 4;

    // ─── Autoload ───────────────────────────────────────────────────────────

    private ChronicleGlobal? _chronicleGlobal;

    // ─── UI nodes (created in _Ready) ─────────────────────────────────────────

    private Label? _summaryLabel;
    private VBoxContainer? _boardContainer;
    private VBoxContainer? _benchContainer;
    private VBoxContainer? _equipListContainer;

    /// <summary>
    /// Unit whose detail modal is requested by a roster-card click. Set by the
    /// formation screen when a card is tapped; consumed by the GameDirector-level
    /// modal in a later stage. Here it only drives the (Stage C) detail window.
    /// </summary>
    public event Action<Guid>? UnitInspectRequested;

    /// <summary>
    /// Equipment dock dialog target id (a transient operation cursor, not board
    /// state). null = all rows collapsed. Preserved across roster re-renders.
    /// </summary>
    private Guid? _pendingEquipId;

    /// <summary>
    /// Click/controller selection cursor (a transient operation cursor, not board
    /// state): the unit "picked up" from the bench or a slot, awaiting a target
    /// slot. null = nothing selected. This powers the consumer (PS5) hybrid:
    /// select a unit, then click a slot to place it. Drag-and-drop is unaffected.
    /// </summary>
    private Guid? _selectedUnitId;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

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

    // ─── UI scaffolding ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Full-screen vertical scroll (matches the all-screens scroll fix).
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

        var titleLabel = new Label { Text = "大隊編成（▲ウェッジ陣形）" };
        titleLabel.SetMeta(TestIdMetaKey, "formation-title");
        root.AddChild(titleLabel);

        _summaryLabel = new Label();
        _summaryLabel.SetMeta(TestIdMetaKey, "formation-summary");
        root.AddChild(_summaryLabel);

        var hintLabel = new Label
        {
            Text = "操作①ドラッグ＆ドロップ：控え→枠で配置、枠→枠で入替。"
                   + "操作②選択式（コントローラ可）：控えの［選択］→配置先の枠の［ここへ］で一撃配属。"
                   + "枠の［× 解除］で外す。控えの［詳細］でユニット詳細。",
        };
        hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hintLabel.SetMeta(TestIdMetaKey, "formation-hint");
        root.AddChild(hintLabel);

        // ※ 今年の行動（出撃／休息）の選択は、編成より上流の拠点フェーズ（MarriageUI）へ移設済み。
        //   この編成画面へ入る時点で「出撃」は確定している（休息は編成画面自体を表示しない）。

        // Squad rotation controls.
        var rotationRow = new HBoxContainer();
        rotationRow.AddThemeConstantOverride("separation", 12);
        rotationRow.SetMeta(TestIdMetaKey, "formation-rotation-row");
        root.AddChild(rotationRow);

        var rotateCcw = new Button { Text = "反時計回り" };
        rotateCcw.SetMeta(TestIdMetaKey, "formation-rotate-counter-clockwise");
        rotateCcw.Pressed += () => OnRotatePressed(RotationDirection.CounterClockwise);
        rotationRow.AddChild(rotateCcw);

        var rotateCw = new Button { Text = "時計回り" };
        rotateCw.SetMeta(TestIdMetaKey, "formation-rotate-clockwise");
        rotateCw.Pressed += () => OnRotatePressed(RotationDirection.Clockwise);
        rotationRow.AddChild(rotateCw);

        // Wedge board (FormationChanged rebuilds the 9 slots).
        var boardSectionLabel = new Label { Text = "― 配置盤面（▲ウェッジ）―" };
        boardSectionLabel.SetMeta(TestIdMetaKey, "formation-board-section-label");
        root.AddChild(boardSectionLabel);

        _boardContainer = new VBoxContainer();
        _boardContainer.AddThemeConstantOverride("separation", 12);
        _boardContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _boardContainer.SetMeta(TestIdMetaKey, "formation-board");
        root.AddChild(_boardContainer);

        // Bench (unplaced living members; RosterChanged rebuilds).
        var benchSectionLabel = new Label { Text = "― 控え（未配置の旅団員）―" };
        benchSectionLabel.SetMeta(TestIdMetaKey, "formation-bench-section-label");
        root.AddChild(benchSectionLabel);

        _benchContainer = new VBoxContainer();
        _benchContainer.AddThemeConstantOverride("separation", 4);
        _benchContainer.SetMeta(TestIdMetaKey, "formation-bench");
        root.AddChild(_benchContainer);

        // Equipment dock (free swap, stateless; RosterChanged rebuilds).
        var equipSection = new VBoxContainer();
        equipSection.SetMeta(TestIdMetaKey, "roster-equip-section");
        root.AddChild(equipSection);

        var equipTitle = new Label { Text = "― 兵装スロット（無償脱着）―" };
        equipTitle.SetMeta(TestIdMetaKey, "roster-equip-title");
        equipSection.AddChild(equipTitle);

        var equipHint = new Label
        {
            Text = "スロットを押して兵装を着脱（緑の数値が装備中の 攻撃/防御/俊敏 補正）。",
        };
        equipHint.SetMeta(TestIdMetaKey, "roster-equip-hint");
        equipSection.AddChild(equipHint);

        _equipListContainer = new VBoxContainer();
        _equipListContainer.AddThemeConstantOverride("separation", 4);
        _equipListContainer.SetMeta(TestIdMetaKey, "roster-equip-list");
        equipSection.AddChild(_equipListContainer);
    }

    // ─── Signals ──────────────────────────────────────────────────────────────

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
            // Safety net if the node was already disposed (leak prevention).
        }
    }

    private void OnFormationChanged() => RenderAll();
    private void OnRosterChanged()    => RenderAll();
    private void OnStateInitialized() => RenderAll();

    // ─── Render (re-read SoT every time; never cache) ──────────────────────────

    private void RenderAll()
    {
        if (_chronicleGlobal is null) return;
        ValidateSelection();
        RenderSummary();
        RenderBoard();
        RenderBench();
        RenderEquipmentSlots();
    }

    /// <summary>
    /// Drop the selection cursor if the picked-up unit is no longer real/alive
    /// (roster or board changed under us). Keeps the cursor self-consistent with SoT.
    /// </summary>
    private void ValidateSelection()
    {
        if (_chronicleGlobal is null || _selectedUnitId is not { } id) return;
        var unit = _chronicleGlobal.FindUnit(id);
        if (unit is null || !unit.IsAlive)
        {
            _selectedUnitId = null;
        }
    }

    private void RenderSummary()
    {
        if (_chronicleGlobal is null || _summaryLabel is null) return;
        var board = _chronicleGlobal.CurrentFormation;
        var text = $"配置済み {board.OccupiedCount} / {FormationBoard.SlotCount} 枠";
        if (_selectedUnitId is { } sel)
        {
            var u = _chronicleGlobal.FindUnit(sel);
            var name = u is null ? sel.ToString() : _chronicleGlobal.ResolveDisplayName(u);
            text += $"　／　選択中: {name} ▶ 配置先の枠を選んでください";
        }
        _summaryLabel.Text = text;
    }

    // ── Wedge board: FRONT centered on top, REAR-L / REAR-R below ──────────────

    private void RenderBoard()
    {
        if (_chronicleGlobal is null || _boardContainer is null) return;

        ClearChildren(_boardContainer);
        var board = _chronicleGlobal.CurrentFormation;

        // ▲ウェッジ陣形（逆三角形）: FRONT（先鋒）を中央上にせり出させ、REAR-L / REAR-R を
        // 下段の左右へ広げる。各分隊ブロック（分隊名 + 横並び3スロット）は共通の BuildSquadBlock
        // が払い出す。配置の真実は CurrentFormation を毎回読み直す（無状態・単一SoT）。
        var wedge = new VBoxContainer();
        wedge.AddThemeConstantOverride("separation", 12);
        wedge.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        wedge.SetMeta(TestIdMetaKey, "formation-wedge");
        _boardContainer.AddChild(wedge);

        // 上段: FRONT を中央に。
        var frontRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        frontRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        frontRow.SetMeta(TestIdMetaKey, "formation-wedge-front-row");
        frontRow.AddChild(BuildSquadBlock(board, SquadRow.Front));
        wedge.AddChild(frontRow);

        // 下段: REAR-L / REAR-R を中央寄せで左右に広げる（逆三角形の底辺）。
        var rearRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        rearRow.AddThemeConstantOverride("separation", 28);
        rearRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rearRow.SetMeta(TestIdMetaKey, "formation-wedge-rear-row");
        rearRow.AddChild(BuildSquadBlock(board, SquadRow.RearLeft));
        rearRow.AddChild(BuildSquadBlock(board, SquadRow.RearRight));
        wedge.AddChild(rearRow);
    }

    /// <summary>Build one squad column: header label + 3 drag-drop slots.</summary>
    private Control BuildSquadBlock(FormationBoard board, SquadRow row)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 4);
        block.SetMeta(TestIdMetaKey, $"formation-squad-{row}");

        var header = new Label { Text = RowLabel(row), HorizontalAlignment = HorizontalAlignment.Center };
        header.SetMeta(TestIdMetaKey, $"formation-squad-label-{row}");
        block.AddChild(header);

        var slotRow = new HBoxContainer();
        slotRow.AddThemeConstantOverride("separation", 6);
        slotRow.SetMeta(TestIdMetaKey, $"formation-slot-row-{row}");
        for (int column = 0; column < FormationBoard.ColumnsPerRow; column++)
        {
            slotRow.AddChild(BuildSlot(board, new SlotCoordinate(row, column)));
        }
        block.AddChild(slotRow);

        return block;
    }

    /// <summary>
    /// Build a single wedge slot: a rich card showing the occupant's job art
    /// (gendered) + name + job, plus a focusable primary button (click / controller
    /// hybrid) and a remove button. The slot remains a drag target / drag source so
    /// drag-and-drop is fully preserved.
    /// </summary>
    private Control BuildSlot(FormationBoard board, SlotCoordinate coordinate)
    {
        var occupant = board.OccupantAt(coordinate);
        var capturedCoord = coordinate;

        var slot = new FormationSlotControl
        {
            Coordinate = coordinate,
            OccupantId = occupant ?? Guid.Empty,
            PlaceRequested = OnPlaceRequested,
            SwapRequested  = OnSwapRequested,
        };
        slot.CustomMinimumSize = new Vector2(132, 188);
        slot.SetMeta(TestIdMetaKey, SlotTestId(coordinate));

        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 2);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slot.AddChild(inner);

        if (occupant is { } unitId && _chronicleGlobal is not null)
        {
            var unit = _chronicleGlobal.FindUnit(unitId);

            var art = BuildJobArt(unit, 96);
            if (art is not null)
            {
                art.SetMeta(TestIdMetaKey, $"formation-slot-art-{coordinate.Row}-{coordinate.Column}");
                inner.AddChild(art);
            }

            var nameLabel = new Label
            {
                Text = unit is null ? unitId.ToString() : _chronicleGlobal.ResolveDisplayName(unit),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            nameLabel.SetMeta(TestIdMetaKey, $"formation-slot-name-{coordinate.Row}-{coordinate.Column}");
            // Picked-up (selected) occupant glows gold so the player sees what is moving.
            if (_selectedUnitId == unitId)
            {
                nameLabel.AddThemeColorOverride("font_color", Colors.Gold);
            }
            inner.AddChild(nameLabel);

            var jobLabel = new Label
            {
                Text = unit is null ? "[?]" : $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            jobLabel.SetMeta(TestIdMetaKey, $"formation-slot-job-{coordinate.Row}-{coordinate.Column}");
            inner.AddChild(jobLabel);

            inner.AddChild(BuildSlotPrimaryButton(capturedCoord, occupied: true));

            var removeButton = new Button { Text = "× 解除" };
            removeButton.SetMeta(TestIdMetaKey, $"formation-slot-remove-{coordinate.Row}-{coordinate.Column}");
            removeButton.Pressed += () => OnClearSlotPressed(capturedCoord);
            inner.AddChild(removeButton);
        }
        else
        {
            var empty = new Label
            {
                Text = "（空き）",
                CustomMinimumSize = new Vector2(0, 96),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            empty.SetMeta(TestIdMetaKey, $"formation-slot-empty-{coordinate.Row}-{coordinate.Column}");
            inner.AddChild(empty);

            inner.AddChild(BuildSlotPrimaryButton(capturedCoord, occupied: false));
        }

        return slot;
    }

    /// <summary>
    /// Build the slot's focusable primary action button for the click/controller
    /// hybrid: with a unit selected it places it here ("▶ ここへ"); with nothing
    /// selected it picks up this slot's occupant to move ("選択"); an empty slot
    /// with no selection is inert.
    /// </summary>
    private Button BuildSlotPrimaryButton(SlotCoordinate coordinate, bool occupied)
    {
        var button = new Button();
        button.SetMeta(TestIdMetaKey, $"formation-slot-place-{coordinate.Row}-{coordinate.Column}");

        if (_selectedUnitId is not null)
        {
            button.Text = occupied ? "▶ ここへ（置換）" : "▶ ここへ";
        }
        else if (occupied)
        {
            button.Text = "選択";
        }
        else
        {
            button.Text = "（空き）";
            button.Disabled = true;
        }

        var capturedCoord = coordinate;
        button.Pressed += () => OnSlotPrimaryPressed(capturedCoord);
        return button;
    }

    /// <summary>
    /// Build a gendered job-illustration TextureRect, or null when art is absent.
    /// MouseFilter = Ignore so the parent slot/card keeps ownership of the drag.
    /// </summary>
    private static TextureRect? BuildJobArt(Unit? unit, int size)
    {
        if (unit is null) return null;
        var texture = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (texture is null) return null;

        return new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    // ── Bench: draggable roster cards (drag source) ───────────────────────────

    private void RenderBench()
    {
        if (_chronicleGlobal is null || _benchContainer is null) return;

        ClearChildren(_benchContainer);

        var board = _chronicleGlobal.CurrentFormation;
        var roster = _chronicleGlobal.BattalionRoster;

        // 未編成（控え）は「横4体」のリッチカード格子で見せる（縦スクロールで快適に閲覧・選択）。
        // 各カードは ①男女別ジョブ画像 ②ユニット名 ③ジョブ名 ④詳細ボタン を縦に内包する。
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 280);
        scroll.SetMeta(TestIdMetaKey, "formation-bench-scroll");
        _benchContainer.AddChild(scroll);

        var grid = new GridContainer { Columns = BenchColumns };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.SetMeta(TestIdMetaKey, "formation-bench-grid");
        scroll.AddChild(grid);

        int benchCount = 0;
        foreach (var unit in roster)
        {
            if (!unit.IsAlive) continue;
            if (board.Contains(unit.Id)) continue;

            grid.AddChild(BuildBenchCard(unit));
            benchCount++;
        }

        if (benchCount == 0)
        {
            var empty = new Label { Text = "（控えの旅団員はいません。全員が配置済みです）" };
            empty.SetMeta(TestIdMetaKey, "formation-bench-empty");
            grid.AddChild(empty);
        }
    }

    /// <summary>
    /// Build one rich bench card (a drag source) laid out vertically: gendered job art,
    /// unit name, job name, a Lv/age/eligibility line, and a [select]/[details] button row.
    /// The card stays a RosterDragCard so drag-and-drop onto a slot is fully preserved;
    /// the hybrid click/controller select and the detail modal are wired exactly as before.
    /// </summary>
    private Control BuildBenchCard(Unit unit)
    {
        var capturedId = unit.Id;
        var eligibility = unit.Age >= BattleEligibleAge ? "出陣可" : "未成年";
        var isSelected = _selectedUnitId == capturedId;

        var card = new RosterDragCard { UnitId = capturedId };
        card.CustomMinimumSize = new Vector2(150, 0);
        card.SetMeta(TestIdMetaKey, $"formation-bench-card-{capturedId}");
        // Selected card glows so the player sees which unit is "in hand".
        card.SelfModulate = isSelected ? new Color(1f, 0.92f, 0.55f) : Colors.White;

        var cardBox = new VBoxContainer();
        cardBox.AddThemeConstantOverride("separation", 2);
        cardBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.AddChild(cardBox);

        // ① 男女別ジョブ画像。MouseFilter=Ignore so the card keeps the drag.
        var art = BuildJobArt(unit, 96);
        if (art is not null)
        {
            art.SetMeta(TestIdMetaKey, $"formation-bench-art-{capturedId}");
            cardBox.AddChild(art);
        }

        // ② ユニット名（選択中は金字）。
        var nameLabel = new Label
        {
            Text = _chronicleGlobal!.ResolveDisplayName(unit),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        nameLabel.SetMeta(TestIdMetaKey, $"formation-bench-name-{capturedId}");
        if (isSelected)
        {
            nameLabel.AddThemeColorOverride("font_color", Colors.Gold);
        }
        cardBox.AddChild(nameLabel);

        // ③ ジョブ名。
        var jobLabel = new Label
        {
            Text = $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        jobLabel.SetMeta(TestIdMetaKey, $"formation-bench-job-{capturedId}");
        cardBox.AddChild(jobLabel);

        // 補助情報（レベル / 年齢 / 出陣可否）。
        var metaLabel = new Label
        {
            Text = $"Lv{unit.Level}  {unit.Age}歳  {eligibility}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        metaLabel.SetMeta(TestIdMetaKey, $"formation-bench-info-{capturedId}");
        cardBox.AddChild(metaLabel);

        // ボタン行（選択 / ④詳細）。
        var buttonRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttonRow.AddThemeConstantOverride("separation", 4);
        cardBox.AddChild(buttonRow);

        // Hybrid select (click / controller): pick this unit up to place by tapping a slot.
        var selectButton = new Button { Text = isSelected ? "選択中" : "選択" };
        if (isSelected)
        {
            selectButton.AddThemeColorOverride("font_color", Colors.Gold);
        }
        selectButton.SetMeta(TestIdMetaKey, $"formation-bench-select-{capturedId}");
        selectButton.Pressed += () => OnBenchSelectPressed(capturedId);
        buttonRow.AddChild(selectButton);

        // ④ Click "Details" to request the unit detail modal.
        var detailButton = new Button { Text = "詳細" };
        detailButton.SetMeta(TestIdMetaKey, $"formation-bench-detail-{capturedId}");
        detailButton.Pressed += () => UnitInspectRequested?.Invoke(capturedId);
        buttonRow.AddChild(detailButton);

        return card;
    }

    // ─── Equipment dock (free swap, stateless) ─────────────────────────────────

    private void RenderEquipmentSlots()
    {
        if (_chronicleGlobal is null || _equipListContainer is null) return;

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

            var name = new Label
            {
                Text = $"[{_chronicleGlobal.ResolveJobName(unit.Job)}] "
                       + _chronicleGlobal.ResolveDisplayName(unit),
            };
            name.SetMeta(TestIdMetaKey, $"roster-equip-name-{capturedId}");
            row.AddChild(name);

            var equip = unit.MainEquipment;
            var slotButton = new Button
            {
                Text = equip is null
                    ? "兵装: なし"
                    : $"兵装: {ItemName(equip.ItemId)} Lv{equip.Level}",
            };
            slotButton.SetMeta(TestIdMetaKey, $"roster-equip-slot-{capturedId}");
            slotButton.Pressed += () => OnEquipSlotPressed(capturedId);
            row.AddChild(slotButton);

            var atkBonus = BattleManager.EquipmentAttackBonus(unit);
            var defBonus = BattleManager.EquipmentDefenseBonus(unit);
            var spdBonus = BattleManager.EquipmentSpeedBonus(unit);
            var preview = new Label { Text = $"攻撃 +{atkBonus}  防御 +{defBonus}  俊敏 +{spdBonus}" };
            preview.SetMeta(TestIdMetaKey, $"roster-equip-stats-{capturedId}");
            if (equip is not null)
            {
                preview.AddThemeColorOverride("font_color", Colors.LightGreen);
            }
            row.AddChild(preview);

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

    // ─── Drag-drop / clear / rotate handlers (call SoT only) ───────────────────

    /// <summary>Roster card dropped on a slot: deploy that unit to the coordinate.</summary>
    private void OnPlaceRequested(SlotCoordinate coordinate, Guid unitId)
        => _chronicleGlobal?.PlaceUnitOnFormation(coordinate, unitId);

    /// <summary>Slot dropped on another slot: swap the two coordinates.</summary>
    private void OnSwapRequested(SlotCoordinate source, SlotCoordinate target)
        => _chronicleGlobal?.SwapFormationSlots(source, target);

    /// <summary>[x remove] pressed on a filled slot: clear that coordinate.</summary>
    private void OnClearSlotPressed(SlotCoordinate coordinate)
        => _chronicleGlobal?.ClearFormationSlot(coordinate);

    /// <summary>Rotation button: request a squad-wise rotation.</summary>
    private void OnRotatePressed(RotationDirection direction)
        => _chronicleGlobal?.RotateFormation(direction);

    // ─── Hybrid select-to-place handlers (click / controller; D&D unaffected) ──

    /// <summary>Bench "選択" pressed: toggle this unit as the picked-up selection.</summary>
    private void OnBenchSelectPressed(Guid unitId)
    {
        _selectedUnitId = (_selectedUnitId == unitId) ? null : unitId;
        RenderAll();
    }

    /// <summary>
    /// Slot primary button (click / controller hybrid). With a unit selected, deploy
    /// it here (Core auto-evicts duplicates and replaces any occupant). With nothing
    /// selected, pick up this slot's occupant to move it. Always re-renders so the
    /// selection highlight stays consistent even on a no-op placement.
    /// </summary>
    private void OnSlotPrimaryPressed(SlotCoordinate coordinate)
    {
        if (_chronicleGlobal is null) return;

        if (_selectedUnitId is { } selected)
        {
            _chronicleGlobal.PlaceUnitOnFormation(coordinate, selected);
            _selectedUnitId = null;
            RenderAll();
            return;
        }

        var occupant = _chronicleGlobal.CurrentFormation.OccupantAt(coordinate);
        if (occupant is { } id)
        {
            _selectedUnitId = id;
            RenderAll();
        }
    }

    // ─── Equipment dock handlers ───────────────────────────────────────────────

    private void OnEquipSlotPressed(Guid unitId)
    {
        _pendingEquipId = (_pendingEquipId == unitId) ? null : unitId;
        RenderEquipmentSlots();
    }

    private void OnEquipPickPressed(Guid unitId, ItemId itemId)
    {
        if (_chronicleGlobal is null) return;
        _pendingEquipId = null;

        var affected = _chronicleGlobal.EquipItem(unitId, itemId);
        if (affected is null)
        {
            GD.Print($"[FormationUI] equip failed (uninitialized or unit not found): Id={unitId} Item={itemId}");
            RenderEquipmentSlots();
            return;
        }
        GD.Print($"[FormationUI] equip {ItemName(itemId)} -> Id={unitId}");
    }

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

    private void OnEquipCancelPressed()
    {
        _pendingEquipId = null;
        RenderEquipmentSlots();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>ASCII squad row label for the wedge headers.</summary>
    private static string RowLabel(SquadRow row) => row switch
    {
        SquadRow.Front     => "前衛（先鋒）",
        SquadRow.RearLeft  => "後衛-左",
        SquadRow.RearRight => "後衛-右",
        _ => row.ToString(),
    };

    /// <summary>Stable ASCII testid built mechanically from the coordinate.</summary>
    private static string SlotTestId(SlotCoordinate coordinate)
        => $"formation-slot-{coordinate.Row}-{coordinate.Column}";

    private static void ClearChildren(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            child.QueueFree();
        }
    }

    /// <summary>Item display name via ChronicleGlobal localization (ASCII fallback).</summary>
    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();
}
