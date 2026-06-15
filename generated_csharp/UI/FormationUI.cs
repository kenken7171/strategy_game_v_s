// =============================================================================
//  ChronicleKnights — FormationUI.cs
// -----------------------------------------------------------------------------
//  Battalion formation phase screen (Control). Faithful trace of the TS web
//  formation screen (packages/frontend/src/phases/BattalionFormation/
//  BattalionFormationPage.tsx): the wedge ("V") layout where the FRONT squad
//  sits centered on top and the two REAR squads sit below, plus drag-and-drop
//  placement (the dormant HubView delta behavior moved into the live screen).
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
using ChronicleKnights.Core.Units;
using ChronicleKnights.UserInterface.Hub; // FormationDragPayload / RosterDragCard / FormationSlotControl
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
            Text = "控えのユニットを枠へドラッグして配置。枠から枠へドラッグで入れ替え。"
                   + "枠の［× 解除］で外す。控えカードの［詳細］でユニット詳細を表示。",
        };
        hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hintLabel.SetMeta(TestIdMetaKey, "formation-hint");
        root.AddChild(hintLabel);

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
        RenderSummary();
        RenderBoard();
        RenderBench();
        RenderEquipmentSlots();
    }

    private void RenderSummary()
    {
        if (_chronicleGlobal is null || _summaryLabel is null) return;
        var board = _chronicleGlobal.CurrentFormation;
        _summaryLabel.Text = $"配置済み {board.OccupiedCount} / {FormationBoard.SlotCount} 枠";
    }

    // ── Wedge board: FRONT centered on top, REAR-L / REAR-R below ──────────────

    private void RenderBoard()
    {
        if (_chronicleGlobal is null || _boardContainer is null) return;

        ClearChildren(_boardContainer);
        var board = _chronicleGlobal.CurrentFormation;

        // Top apex: the FRONT squad, horizontally centered.
        var topCenter = new CenterContainer();
        topCenter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        topCenter.SetMeta(TestIdMetaKey, "formation-wedge-top");
        topCenter.AddChild(BuildSquadBlock(board, SquadRow.Front));
        _boardContainer.AddChild(topCenter);

        // Base: the two REAR squads side by side, centered as a pair.
        var bottomCenter = new CenterContainer();
        bottomCenter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomCenter.SetMeta(TestIdMetaKey, "formation-wedge-bottom");
        var bottomRow = new HBoxContainer();
        bottomRow.AddThemeConstantOverride("separation", 28);
        bottomRow.AddChild(BuildSquadBlock(board, SquadRow.RearLeft));
        bottomRow.AddChild(BuildSquadBlock(board, SquadRow.RearRight));
        bottomCenter.AddChild(bottomRow);
        _boardContainer.AddChild(bottomCenter);
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

    /// <summary>Build a single wedge slot as a drag target / drag source.</summary>
    private Control BuildSlot(FormationBoard board, SlotCoordinate coordinate)
    {
        var occupant = board.OccupantAt(coordinate);

        var slot = new FormationSlotControl
        {
            Coordinate = coordinate,
            OccupantId = occupant ?? Guid.Empty,
            PlaceRequested = OnPlaceRequested,
            SwapRequested  = OnSwapRequested,
        };
        slot.CustomMinimumSize = new Vector2(150, 74);
        slot.SetMeta(TestIdMetaKey, SlotTestId(coordinate));

        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 2);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slot.AddChild(inner);

        if (occupant is { } unitId && _chronicleGlobal is not null)
        {
            var unit = _chronicleGlobal.FindUnit(unitId);

            var nameLabel = new Label
            {
                Text = unit is null ? unitId.ToString() : _chronicleGlobal.ResolveDisplayName(unit),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            nameLabel.SetMeta(TestIdMetaKey, $"formation-slot-name-{coordinate.Row}-{coordinate.Column}");
            inner.AddChild(nameLabel);

            var jobLabel = new Label
            {
                Text = unit is null ? "[?]" : $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            jobLabel.SetMeta(TestIdMetaKey, $"formation-slot-job-{coordinate.Row}-{coordinate.Column}");
            inner.AddChild(jobLabel);

            var removeButton = new Button { Text = "× 解除" };
            removeButton.SetMeta(TestIdMetaKey, $"formation-slot-remove-{coordinate.Row}-{coordinate.Column}");
            var capturedCoord = coordinate;
            removeButton.Pressed += () => OnClearSlotPressed(capturedCoord);
            inner.AddChild(removeButton);
        }
        else
        {
            var empty = new Label
            {
                Text = "（ここへ配置）",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            empty.SetMeta(TestIdMetaKey, $"formation-slot-empty-{coordinate.Row}-{coordinate.Column}");
            inner.AddChild(empty);
        }

        return slot;
    }

    // ── Bench: draggable roster cards (drag source) ───────────────────────────

    private void RenderBench()
    {
        if (_chronicleGlobal is null || _benchContainer is null) return;

        ClearChildren(_benchContainer);

        var board = _chronicleGlobal.CurrentFormation;
        var roster = _chronicleGlobal.BattalionRoster;

        foreach (var unit in roster)
        {
            if (!unit.IsAlive) continue;
            if (board.Contains(unit.Id)) continue;

            var capturedId = unit.Id;
            var eligibility = unit.Age >= BattleEligibleAge ? "出陣可" : "未成年";

            var card = new RosterDragCard { UnitId = capturedId };
            card.SetMeta(TestIdMetaKey, $"formation-bench-card-{capturedId}");

            var rowBox = new HBoxContainer();
            rowBox.AddThemeConstantOverride("separation", 8);
            card.AddChild(rowBox);

            var info = new Label
            {
                Text =
                    $"{_chronicleGlobal.ResolveDisplayName(unit)}  " +
                    $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]  " +
                    $"Lv{unit.Level}  Age {unit.Age}  ({eligibility})",
            };
            info.SetMeta(TestIdMetaKey, $"formation-bench-info-{capturedId}");
            rowBox.AddChild(info);

            // Click "Details" to request the unit detail modal.
            var detailButton = new Button { Text = "詳細" };
            detailButton.SetMeta(TestIdMetaKey, $"formation-bench-detail-{capturedId}");
            detailButton.Pressed += () => UnitInspectRequested?.Invoke(capturedId);
            rowBox.AddChild(detailButton);

            _benchContainer.AddChild(card);
        }
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
