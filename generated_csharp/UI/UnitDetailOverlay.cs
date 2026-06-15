// =============================================================================
//  ChronicleKnights — UnitDetailOverlay.cs
// -----------------------------------------------------------------------------
//  Faithful C# trace of the TS UnitDetailModal (packages/frontend/src/
//  components/UnitDetailModal.tsx): a modal that appears when a roster unit is
//  selected, showing the full profile (stats, gender, lineage, current
//  formation slot) plus the shared job description block.
//
//  Stateless, self-collapsing modal (same lifecycle as the other overlays):
//  TargetUnitId is injected before AddChild; CLOSE raises CloseRequested and the
//  GameDirector frees it. All data is read fresh from ChronicleGlobal on _Ready;
//  numeric stats come from JobMaster + BattleManager (single SoT).
//
//  Constitution I: ASCII only. Job / item / display names go through
//  ChronicleGlobal resolvers; English chrome text is literal here.
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Front-most modal showing one unit's full profile + job description.</summary>
public partial class UnitDetailOverlay : Godot.Control
{
    private const string TestIdMetaKey = "data_testid";

    /// <summary>Unit to profile. Injected before AddChild (the modal is stateless).</summary>
    public Guid TargetUnitId { get; set; }

    /// <summary>Raised when the player presses CLOSE (GameDirector frees the modal).</summary>
    public event Action? CloseRequested;

    private ChronicleGlobal? _chronicleGlobal;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta(TestIdMetaKey, "unit-detail-overlay-root");
        BuildUI();
    }

    private void BuildUI()
    {
        // Dim backdrop (inert behind).
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.85f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.SetMeta(TestIdMetaKey, "unit-detail-overlay-backdrop");
        AddChild(backdrop);

        // Centered modal panel.
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(560, 0);
        panel.SetMeta(TestIdMetaKey, "unit-detail-modal-root");
        center.AddChild(panel);

        // The panel content scrolls in case the job description is tall.
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.CustomMinimumSize = new Vector2(560, 560);
        scroll.SetMeta(TestIdMetaKey, "unit-detail-scroll");
        panel.AddChild(scroll);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(body);

        var unit = _chronicleGlobal?.FindUnit(TargetUnitId);
        if (_chronicleGlobal is null || unit is null)
        {
            BuildHeader(body, "UNIT DETAIL");
            var missing = new Label { Text = "Unit not found." };
            missing.SetMeta(TestIdMetaKey, "unit-detail-missing");
            body.AddChild(missing);
            return;
        }

        BuildHeader(body, "UNIT DETAIL");

        // ── Identity ─────────────────────────────────────────────────────────
        var name = new Label { Text = _chronicleGlobal.ResolveDisplayName(unit) };
        name.SetMeta(TestIdMetaKey, "unit-detail-name");
        body.AddChild(name);

        var jobBadge = new Label { Text = $"[{_chronicleGlobal.ResolveJobName(unit.Job)}]  ({unit.Job})" };
        jobBadge.SetMeta(TestIdMetaKey, "unit-detail-job-badge");
        body.AddChild(jobBadge);

        var genderText = unit.Gender == Gender.Male ? "Male (M)" : "Female (F)";
        var bio = new Label { Text = $"Gender: {genderText}   Origin: {unit.Origin}   Age: {unit.Age}   Lv{unit.Level}" };
        bio.SetMeta(TestIdMetaKey, "unit-detail-bio");
        body.AddChild(bio);

        // ── Current formation slot ───────────────────────────────────────────
        var coord = _chronicleGlobal.CurrentFormation.CoordinateOf(TargetUnitId);
        var slotText = coord is { } c
            ? $"Formation slot: {c.Row} slot {c.Column + 1}"
            : "Formation slot: (on the bench)";
        var slot = new Label { Text = slotText };
        slot.SetMeta(TestIdMetaKey, "unit-detail-formation-slot");
        body.AddChild(slot);

        // ── Stats (from JobMaster + equipment bonuses) ───────────────────────
        var statsHeader = new Label { Text = "-- Stats --" };
        statsHeader.SetMeta(TestIdMetaKey, "unit-detail-stats-title");
        body.AddChild(statsHeader);

        var def = JobMaster.Find(unit.Job);
        if (def is not null)
        {
            var s = def.Stats;
            var effective = s.FrontAttack >= s.RearAttack ? s.FrontAttack : s.RearAttack;

            AddStat(body, "unit-detail-stat-hp", $"HP: {s.MaxHp}");
            AddStat(body, "unit-detail-stat-attack",
                $"ATK (FA / RA): {s.FrontAttack} / {s.RearAttack}  (effective {effective})");
            AddStat(body, "unit-detail-stat-speed", $"SPD: {s.Speed}");
        }

        var equip = unit.MainEquipment;
        var equipText = equip is null ? "Equipment: --" : $"Equipment: {ItemName(equip.ItemId)} Lv{equip.Level}";
        AddStat(body, "unit-detail-equipment", equipText);

        var atkBonus = BattleManager.EquipmentAttackBonus(unit);
        var defBonus = BattleManager.EquipmentDefenseBonus(unit);
        var spdBonus = BattleManager.EquipmentSpeedBonus(unit);
        AddStat(body, "unit-detail-equip-bonus",
            $"Equip bonus: ATK +{atkBonus}  DEF +{defBonus}  SPD +{spdBonus}");

        // ── Lineage (gender / marriage / parentage) ──────────────────────────
        var lineageHeader = new Label { Text = "-- Lineage --" };
        lineageHeader.SetMeta(TestIdMetaKey, "unit-detail-lineage-title");
        body.AddChild(lineageHeader);

        if (unit.IsMarried && unit.SpouseId is { } spouseId)
        {
            var spouse = _chronicleGlobal.FindUnit(spouseId);
            var spouseName = spouse is null ? "(lost)" : _chronicleGlobal.ResolveDisplayName(spouse);
            AddStat(body, "unit-detail-married", $"Married to: {spouseName}");
        }

        if (unit.HasParentage && unit.Parentage is { } parentage)
        {
            var father = _chronicleGlobal.FindUnit(parentage.FatherId);
            var mother = _chronicleGlobal.FindUnit(parentage.MotherId);
            var fatherName = father is null ? "(lost)" : _chronicleGlobal.ResolveDisplayName(father);
            var motherName = mother is null ? "(lost)" : _chronicleGlobal.ResolveDisplayName(mother);
            AddStat(body, "unit-detail-heir", $"Heir of: {fatherName} & {motherName}");
        }

        if (!unit.IsMarried && !unit.HasParentage)
        {
            AddStat(body, "unit-detail-no-lineage", "-- No lineage records (founding member) --");
        }

        // ── Job description (shared with the Job Manual) ─────────────────────
        var jobDescHeader = new Label { Text = "-- Job Description --" };
        jobDescHeader.SetMeta(TestIdMetaKey, "unit-detail-job-description-header");
        body.AddChild(jobDescHeader);

        var jobBlock = JobDescriptionView.Build(unit.Job, "unit-detail-job");
        if (jobBlock is not null)
        {
            body.AddChild(jobBlock);
        }
    }

    private void BuildHeader(VBoxContainer body, string title)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "unit-detail-header");
        body.AddChild(header);

        var titleLabel = new Label { Text = title };
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.SetMeta(TestIdMetaKey, "unit-detail-title");
        header.AddChild(titleLabel);

        var closeButton = new Button { Text = "CLOSE" };
        closeButton.SetMeta(TestIdMetaKey, "unit-detail-modal-close-button");
        closeButton.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(closeButton);
    }

    private static void AddStat(VBoxContainer parent, string testId, string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.SetMeta(TestIdMetaKey, testId);
        parent.AddChild(label);
    }

    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();
}
