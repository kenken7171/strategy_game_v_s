// =============================================================================
//  ChronicleKnights — Core/Assets/SpriteSheetSlicer.cs
// -----------------------------------------------------------------------------
//  Pure, Godot-independent geometry for slicing a horizontal sprite-sheet strip
//  into per-frame rectangles. Used by UI/SpriteSheetAnimator to build AtlasTexture
//  regions for unit attack animations. Kept in Core so the (only) numeric logic is
//  unit-testable without Godot.
//
//  Convention (see docs/UNIT_ATTACK_ANIMATION.md): an attack sheet is a single PNG
//  laid out as a horizontal strip of SQUARE frames (each frame = height x height).
//  The frame count is therefore self-describing: floor(width / height). No sidecar
//  metadata file is needed.
// =============================================================================

using System;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Assets;

/// <summary>One frame's pixel rectangle inside a sprite sheet (all values integer).</summary>
public readonly record struct FrameRect(int X, int Y, int Width, int Height);

/// <summary>Pure geometry: horizontal sprite-sheet strip -> per-frame rectangles.</summary>
public static class SpriteSheetSlicer
{
    /// <summary>
    /// Frame count for a horizontal strip of square frames: floor(width / height).
    /// Returns 1 for non-positive dimensions or when the sheet is narrower than tall
    /// (a single frame). Never returns less than 1.
    /// </summary>
    public static int SquareFrameCount(int sheetWidth, int sheetHeight)
    {
        if (sheetWidth <= 0 || sheetHeight <= 0) return 1;
        return Math.Max(1, sheetWidth / sheetHeight);
    }

    /// <summary>
    /// Slice a sheet into <paramref name="frameCount"/> equal-width frames laid left
    /// to right (each full sheet height). frameWidth = floor(width / frameCount); any
    /// remainder pixels on the right edge are ignored so every frame is identical size.
    /// Returns an empty array for non-positive dimensions or frameCount &lt; 1.
    /// </summary>
    public static ImmutableArray<FrameRect> HorizontalStrip(int sheetWidth, int sheetHeight, int frameCount)
    {
        if (sheetWidth <= 0 || sheetHeight <= 0 || frameCount < 1)
            return ImmutableArray<FrameRect>.Empty;

        var frameWidth = sheetWidth / frameCount;
        if (frameWidth <= 0) return ImmutableArray<FrameRect>.Empty;

        var builder = ImmutableArray.CreateBuilder<FrameRect>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            builder.Add(new FrameRect(i * frameWidth, 0, frameWidth, sheetHeight));
        }
        return builder.MoveToImmutable();
    }
}
