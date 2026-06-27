#!/bin/bash
# =============================================================================
#  Chronicle Knights -- Prophecy Card Gallery launcher (macOS)
# -----------------------------------------------------------------------------
#  Visual QA: opens a scene that renders ALL prophecy-kind card illustrations
#  (via the real ProphecyTextureLibrary) with kind names and rarity tints, so you
#  can eyeball every selection card without waiting for the in-game draw.
#
#  Double-click in Finder, or run from a shell:
#      ./preview_prophecies.command
#
#  Reuses play.command (Godot discovery + C# build) and points Godot at the
#  ProphecyGallery scene instead of the main game scene.
#
#  Constitution I: ASCII only.
# =============================================================================
set -e
cd "$(cd "$(dirname "$0")" && pwd)"
exec ./play.command res://ProphecyGallery.tscn
