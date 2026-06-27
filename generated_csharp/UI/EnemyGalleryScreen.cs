// =============================================================================
//  ChronicleKnights — UI/EnemyGalleryScreen.cs
// -----------------------------------------------------------------------------
//  Developer visual-QA screen: renders every EnemyArchetype illustration through
//  the REAL EnemyTextureLibrary (the same loader the battle screen uses) so a
//  human can eyeball all five at once without playing 100 in-game years.
//
//  Each portrait sits on a checkerboard so transparency is obvious, with a status
//  line showing whether the texture loaded (and its pixel size) or is MISSING.
//
//  Not part of the game flow. Launch via the EnemyGallery.tscn scene
//  (./preview_enemies.command). Constitution I: identifiers/strings ASCII only.
// =============================================================================

using System;
using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.UserInterface;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Standalone gallery that previews all enemy archetype textures for visual QA.</summary>
public partial class EnemyGalleryScreen : Control
{
    private const int CellSize = 320;

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
        root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(root);

        root.AddChild(new Label
        {
            Text = "Enemy Gallery -- visual QA  (checkerboard = transparent area)",
        });
        root.AddChild(new Label
        {
            Text = "Loaded via the real EnemyTextureLibrary (same as battle). Close with Cmd+Q.",
        });

        var checker = MakeCheckerTexture();

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        root.AddChild(grid);

        foreach (EnemyArchetype archetype in Enum.GetValues<EnemyArchetype>())
        {
            grid.AddChild(BuildCell(archetype, checker));
        }
    }

    private static Control BuildCell(EnemyArchetype archetype, Texture2D checker)
    {
        var slug = AssetSlugs.ForEnemy(archetype);
        var texture = EnemyTextureLibrary.TryLoad(archetype);

        var cell = new VBoxContainer();
        cell.AddThemeConstantOverride("separation", 4);

        cell.AddChild(new Label
        {
            Text = $"{archetype}  ({slug}.png)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var frame = new Control { CustomMinimumSize = new Vector2(CellSize, CellSize) };
        cell.AddChild(frame);

        var board = new TextureRect
        {
            Texture       = checker,
            StretchMode   = TextureRect.StretchModeEnum.Tile,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        };
        frame.AddChild(board);
        board.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var portrait = new TextureRect
        {
            Texture     = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        frame.AddChild(portrait);
        portrait.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        cell.AddChild(new Label
        {
            Text = texture is null
                ? "MISSING (TryLoad returned null)"
                : $"OK  {texture.GetWidth()}x{texture.GetHeight()}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return cell;
    }

    /// <summary>Generate a small two-tone checkerboard so transparent pixels are visible.</summary>
    private static Texture2D MakeCheckerTexture()
    {
        const int n = 24;            // 24x24 with 12px squares -> tiles cleanly
        const int half = n / 2;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var light = new Color(0.34f, 0.34f, 0.38f);
        var dark = new Color(0.22f, 0.22f, 0.25f);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var even = ((x / half) + (y / half)) % 2 == 0;
                img.SetPixel(x, y, even ? light : dark);
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
