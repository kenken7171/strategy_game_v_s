// =============================================================================
//  ChronicleKnights.Tests — Core/Assets/SpriteSheetSlicerTests.cs
// -----------------------------------------------------------------------------
//  Pure-layer tests for the sprite-sheet slicing geometry (unit attack animations).
//  Covers SquareFrameCount (self-describing frame count = width/height) and
//  HorizontalStrip (equal-width frame rectangles, remainder ignored, guards).
//  Godot-independent, runs headless under xUnit.
// =============================================================================

using ChronicleKnights.Core.Assets;
using Xunit;

namespace ChronicleKnights.Tests.Core.Assets;

public sealed class SpriteSheetSlicerTests
{
    // ─── SquareFrameCount ────────────────────────────────────────────────

    [Theory]
    [InlineData(512, 128, 4)] // 4 square 128px frames
    [InlineData(128, 128, 1)] // single square frame
    [InlineData(768, 128, 6)]
    [InlineData(96, 96, 1)]
    public void SquareFrameCount_IsWidthOverHeight(int w, int h, int expected)
    {
        Assert.Equal(expected, SpriteSheetSlicer.SquareFrameCount(w, h));
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(128, 0)]
    [InlineData(-4, 128)]
    [InlineData(64, 128)] // narrower than tall -> clamp to 1
    public void SquareFrameCount_NeverBelowOne(int w, int h)
    {
        Assert.Equal(1, SpriteSheetSlicer.SquareFrameCount(w, h));
    }

    // ─── HorizontalStrip ─────────────────────────────────────────────────

    [Fact]
    public void HorizontalStrip_ProducesEqualWidthFramesLeftToRight()
    {
        var frames = SpriteSheetSlicer.HorizontalStrip(512, 128, 4);

        Assert.Equal(4, frames.Length);
        for (int i = 0; i < frames.Length; i++)
        {
            Assert.Equal(i * 128, frames[i].X);
            Assert.Equal(0, frames[i].Y);
            Assert.Equal(128, frames[i].Width);
            Assert.Equal(128, frames[i].Height);
        }
    }

    [Fact]
    public void HorizontalStrip_SingleFrame_IsWholeSheet()
    {
        var frames = SpriteSheetSlicer.HorizontalStrip(128, 128, 1);
        var only = Assert.Single(frames);
        Assert.Equal(new FrameRect(0, 0, 128, 128), only);
    }

    [Fact]
    public void HorizontalStrip_RemainderPixels_AreIgnored_FramesStayEqual()
    {
        // 130 / 4 = 32 (2px remainder dropped); all frames 32 wide, last starts at 96.
        var frames = SpriteSheetSlicer.HorizontalStrip(130, 128, 4);
        Assert.Equal(4, frames.Length);
        Assert.All(frames, f => Assert.Equal(32, f.Width));
        Assert.Equal(96, frames[3].X);
    }

    [Theory]
    [InlineData(0, 128, 4)]
    [InlineData(512, 0, 4)]
    [InlineData(512, 128, 0)]
    [InlineData(512, 128, -1)]
    public void HorizontalStrip_InvalidInputs_ReturnEmpty(int w, int h, int count)
    {
        Assert.Empty(SpriteSheetSlicer.HorizontalStrip(w, h, count));
    }

    [Fact]
    public void HorizontalStrip_FrameCountLargerThanWidth_ReturnsEmpty()
    {
        // frameWidth would be 0 -> guarded to empty (no zero-width frames).
        Assert.Empty(SpriteSheetSlicer.HorizontalStrip(3, 128, 4));
    }
}
