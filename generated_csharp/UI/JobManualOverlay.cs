// =============================================================================
//  ChronicleKnights — JobManualOverlay.cs
// -----------------------------------------------------------------------------
//  Faithful C# trace of the TS JobManualOverlay (packages/frontend/src/
//  components/JobManualOverlay.tsx): a full-screen catalog browsing all 8 jobs,
//  each rendered with the shared JobDescriptionView block.
//
//  Stateless, self-collapsing overlay (same lifecycle as PedigreeOverlay /
//  ProphecyTimelineOverlay): CLOSE raises CloseRequested; GameDirector mounts it
//  at the very front and frees it on close / _ExitTree. Job data is static, so no
//  SoT subscription is needed. Constitution I: ASCII only.
// =============================================================================

using System;
using ChronicleKnights.Core.Job;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Front-most overlay listing every job with its full description block.</summary>
public partial class JobManualOverlay : Godot.Control
{
    private const string TestIdMetaKey = "data_testid";

    /// <summary>Raised when the player presses CLOSE (GameDirector frees the overlay).</summary>
    public event Action? CloseRequested;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta(TestIdMetaKey, "job-manual-overlay-root");
        BuildUI();
    }

    private void BuildUI()
    {
        // Dim backdrop (catches clicks so the screen behind is inert).
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.88f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.SetMeta(TestIdMetaKey, "job-manual-overlay-backdrop");
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", 12);
        panel.SetMeta(TestIdMetaKey, "job-manual-overlay-panel");
        margin.AddChild(panel);

        // Header: title + close.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "job-manual-header");
        panel.AddChild(header);

        var title = new Label { Text = "ジョブ図鑑 ― 全8職" };
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.SetMeta(TestIdMetaKey, "job-manual-title");
        header.AddChild(title);

        var closeButton = new Button { Text = "閉じる" };
        closeButton.SetMeta(TestIdMetaKey, "job-manual-close-button");
        closeButton.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(closeButton);

        // Scrollable list of every job (vertical scroll; width stretched).
        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SetMeta(TestIdMetaKey, "job-manual-scroll");
        panel.AddChild(scroll);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 16);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        list.SetMeta(TestIdMetaKey, "job-manual-list");
        scroll.AddChild(list);

        foreach (var id in JobMaster.DisplayOrder)
        {
            var card = new PanelContainer();
            card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            card.SetMeta(TestIdMetaKey, $"job-manual-card-{id}");

            var block = JobDescriptionView.Build(id, null, "job-manual");
            if (block is not null)
            {
                card.AddChild(block);
            }
            list.AddChild(card);
        }
    }
}
