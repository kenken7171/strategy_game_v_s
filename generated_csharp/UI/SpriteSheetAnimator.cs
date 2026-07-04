// =============================================================================
//  ChronicleKnights — UI/SpriteSheetAnimator.cs
// -----------------------------------------------------------------------------
//  A TextureRect that plays a horizontal sprite-sheet strip as a one-shot frame
//  animation (used for unit attack motions). The sheet is a horizontal strip of
//  SQUARE frames (frame count = width / height, self-describing); slicing geometry
//  lives in the pure Core.Assets.SpriteSheetSlicer so it is unit-tested.
//
//  Lifecycle:
//    - Configure(sheet, fps): slice into AtlasTexture frames, show frame 0 (the
//      right-facing idle), disable _Process (idle = no per-frame work).
//    - PlayAttack(): step frames 0..N-1 once at fps, then settle back on frame 0.
//    - Single frame (or no sheet) => PlayAttack is a no-op (stays static).
//  TextureFilter is forced to Nearest so pixel art stays crisp when scaled.
// =============================================================================

using System;
using ChronicleKnights.Core.Assets;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>One-shot horizontal sprite-sheet player rendered as a TextureRect.</summary>
public sealed partial class SpriteSheetAnimator : TextureRect
{
    /// <summary>Default playback speed (frames per second) for an attack motion.</summary>
    public const double DefaultFps = 10.0;

    private Texture2D[] _frames = Array.Empty<Texture2D>();
    private double _fps = DefaultFps;
    private double _elapsed;
    private int _index;
    private bool _playing;

    /// <summary>
    /// Slice the sheet into frames and settle on the idle frame (0). Safe to call once
    /// after construction. fps &lt;= 0 falls back to <see cref="DefaultFps"/>.
    /// </summary>
    public void Configure(Texture2D sheet, double fps = DefaultFps)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        _fps = fps > 0 ? fps : DefaultFps;
        _frames = SliceFrames(sheet);
        _index = 0;
        _playing = false;
        _elapsed = 0;

        TextureFilter = TextureFilterEnum.Nearest; // crisp pixels when scaled
        if (_frames.Length > 0) Texture = _frames[0];
        SetProcess(false); // idle costs nothing until PlayAttack
    }

    /// <summary>Play the attack strip once (0..N-1) then return to the idle frame.</summary>
    public void PlayAttack()
    {
        if (_frames.Length <= 1) return; // nothing to animate (static sprite)
        _index = 0;
        _elapsed = 0;
        _playing = true;
        Texture = _frames[0];
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!_playing) return;

        _elapsed += delta;
        var frameDuration = 1.0 / _fps;

        // Advance as many frames as elapsed time allows (robust to frame drops).
        while (_elapsed >= frameDuration)
        {
            _elapsed -= frameDuration;
            _index++;
            if (_index >= _frames.Length)
            {
                // One-shot finished: settle on idle and stop processing.
                _index = 0;
                _playing = false;
                Texture = _frames[0];
                SetProcess(false);
                return;
            }
            Texture = _frames[_index];
        }
    }

    private static Texture2D[] SliceFrames(Texture2D sheet)
    {
        var width = sheet.GetWidth();
        var height = sheet.GetHeight();
        var count = SpriteSheetSlicer.SquareFrameCount(width, height);
        var rects = SpriteSheetSlicer.HorizontalStrip(width, height, count);

        var frames = new Texture2D[rects.Length];
        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            frames[i] = new AtlasTexture
            {
                Atlas  = sheet,
                Region = new Rect2(r.X, r.Y, r.Width, r.Height),
            };
        }
        return frames;
    }
}
