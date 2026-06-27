// =============================================================================
//  ChronicleKnights — UI/BackgroundGalleryScreen.cs
// -----------------------------------------------------------------------------
//  Developer visual-QA screen for the per-epoch battlefield backgrounds. Renders
//  every EpochId background through the REAL BackgroundTextureLibrary (same loader
//  the game uses) as a 16:9 "mock screen", with the SAME semi-transparent content
//  card (0.74 dark + 24px margin) and sample UI text laid on top -- so a human can
//  judge at a glance whether the art reads well when dimmed and whether text stays
//  readable, without playing to years 25/50/75/100.
//
//  Not part of the game flow. Launch via BackgroundGallery.tscn
//  (./preview_backgrounds.command). Constitution I: identifiers/strings ASCII only.
// =============================================================================

using System;
using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.UserInterface;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Standalone gallery previewing every epoch background under the real content card.</summary>
public partial class BackgroundGalleryScreen : Control
{
    // Mirror GameDirector's content card so the dimming/readability match the real game.
    private static readonly Color ContentCardColor = new(0.06f, 0.07f, 0.10f, 0.74f);
    private static readonly Color ContentCardBorderColor = new(1.0f, 1.0f, 1.0f, 0.10f);

    private const int ScreenW = 520;   // 16:9 mock-screen cell
    private const int ScreenH = 293;
    private const int CardMarginPx = 12;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect { Color = new Color(0.08f, 0.08f, 0.10f) };
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
            Text = "Background Gallery -- visual QA (real content card 0.74 dark overlaid; is text readable?)",
        });
        root.AddChild(new Label
        {
            Text = "Loaded via the real BackgroundTextureLibrary (same as the game). Close with Cmd+Q.",
        });

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        root.AddChild(grid);

        foreach (EpochId epoch in Enum.GetValues<EpochId>())
        {
            grid.AddChild(BuildCell(epoch));
        }
    }

    private static Control BuildCell(EpochId epoch)
    {
        var slug = AssetSlugs.ForEpoch(epoch);
        var texture = BackgroundTextureLibrary.TryLoad(epoch);

        var cell = new VBoxContainer();
        cell.AddThemeConstantOverride("separation", 4);

        cell.AddChild(new Label
        {
            Text = $"{epoch}  ({slug}.png)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // The 16:9 "screen": background (KeepAspectCovered, clipped) + content card + sample text.
        var screen = new Control { CustomMinimumSize = new Vector2(ScreenW, ScreenH), ClipContents = true };
        cell.AddChild(screen);

        var bgRect = new TextureRect
        {
            Texture     = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        screen.AddChild(bgRect);
        bgRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var cardMargin = new MarginContainer();
        cardMargin.AddThemeConstantOverride("margin_left", CardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_top", CardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_right", CardMarginPx);
        cardMargin.AddThemeConstantOverride("margin_bottom", CardMarginPx);
        screen.AddChild(cardMargin);
        cardMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var card = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor     = ContentCardColor,
            BorderColor = ContentCardBorderColor,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(14);
        card.AddThemeStyleboxOverride("panel", style);
        cardMargin.AddChild(card);

        var textCol = new VBoxContainer();
        textCol.AddThemeConstantOverride("separation", 6);
        card.AddChild(textCol);
        textCol.AddChild(new Label { Text = $"Chronicle -- {epoch}  (sample title)" });
        textCol.AddChild(new Label { Text = "Sample UI body text over the dimmed background." });
        textCol.AddChild(new Label { Text = "Is this still readable? Edge frame shows the art at full strength." });

        cell.AddChild(new Label
        {
            Text = texture is null
                ? "MISSING (TryLoad returned null)"
                : $"OK  {texture.GetWidth()}x{texture.GetHeight()}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return cell;
    }
}
