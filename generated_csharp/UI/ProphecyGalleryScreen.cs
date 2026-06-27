// =============================================================================
//  ChronicleKnights — UI/ProphecyGalleryScreen.cs
// -----------------------------------------------------------------------------
//  Developer visual-QA screen for the prophecy (selection) card illustrations.
//  Renders every ProphecyKind through the REAL ProphecyTextureLibrary (same loader
//  the Chronicle screen uses) as a mock card -- art on top, kind name + the three
//  rarity badges below -- so a human can eyeball all five at once without waiting
//  for the in-game draw.
//
//  Not part of the game flow. Launch via ProphecyGallery.tscn
//  (./preview_prophecies.command). Constitution I: identifiers/strings ASCII only.
// =============================================================================

using System;
using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.UserInterface;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Standalone gallery previewing all prophecy-kind card illustrations for visual QA.</summary>
public partial class ProphecyGalleryScreen : Control
{
    private const int ArtW = 260;
    private const int ArtH = 150;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0.10f, 0.10f, 0.12f) };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer();
        AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(root);

        root.AddChild(new Label
        {
            Text = "Prophecy Card Gallery -- visual QA (art via the real ProphecyTextureLibrary)",
        });
        root.AddChild(new Label
        {
            Text = "Each card: illustration + kind name + Bronze/Silver/Gold tint preview. Close with Cmd+Q.",
        });

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        root.AddChild(grid);

        foreach (ProphecyKind kind in Enum.GetValues<ProphecyKind>())
        {
            grid.AddChild(BuildCard(kind));
        }
    }

    private static Control BuildCard(ProphecyKind kind)
    {
        var slug = AssetSlugs.ForProphecyKind(kind);
        var texture = ProphecyTextureLibrary.TryLoad(kind);

        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 4);

        card.AddChild(new Label
        {
            Text = $"{kind}  ({slug}.png)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var art = new TextureRect
        {
            Texture           = texture,
            CustomMinimumSize = new Vector2(ArtW, ArtH),
            StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        card.AddChild(art);

        // Rarity tint preview row (matches TimelineUI.RarityColor on the real cards).
        var tints = new HBoxContainer();
        tints.AddThemeConstantOverride("separation", 6);
        tints.AddChild(new Label { Text = "rarity:" });
        tints.AddChild(MakeSwatch("Bronze", new Color(0.80f, 0.52f, 0.25f)));
        tints.AddChild(MakeSwatch("Silver", new Color(0.82f, 0.88f, 1.0f)));
        tints.AddChild(MakeSwatch("Gold", new Color(1.0f, 0.86f, 0.35f)));
        card.AddChild(tints);

        card.AddChild(new Label
        {
            Text = texture is null
                ? "MISSING (TryLoad returned null)"
                : $"OK  {texture.GetWidth()}x{texture.GetHeight()}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return card;
    }

    private static ColorRect MakeSwatch(string name, Color color) => new()
    {
        Color             = color,
        CustomMinimumSize = new Vector2(22, 14),
        TooltipText       = name,
    };
}
