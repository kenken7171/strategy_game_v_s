#!/bin/bash
# =============================================================================
#  Chronicle Knights -- Background Gallery launcher (macOS)
# -----------------------------------------------------------------------------
#  Visual QA: opens a scene that renders ALL epoch backgrounds (via the real
#  BackgroundTextureLibrary) as 16:9 mock screens with the SAME semi-transparent
#  content card and sample UI text overlaid -- so you can judge dimming and text
#  readability without playing to years 25/50/75/100.
#
#  Double-click in Finder, or run from a shell:
#      ./preview_backgrounds.command
#
#  Reuses play.command (Godot discovery + C# build) and points Godot at the
#  BackgroundGallery scene instead of the main game scene.
#
#  Constitution I: ASCII only.
# =============================================================================
set -e
cd "$(cd "$(dirname "$0")" && pwd)"
exec ./play.command res://BackgroundGallery.tscn
