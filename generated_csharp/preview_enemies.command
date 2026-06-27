#!/bin/bash
# =============================================================================
#  Chronicle Knights -- Enemy Gallery launcher (macOS)
# -----------------------------------------------------------------------------
#  Visual QA: opens a scene that renders ALL enemy archetype illustrations
#  (via the real EnemyTextureLibrary) on a checkerboard, so you can eyeball
#  every enemy at once without playing through 100 in-game years.
#
#  Double-click in Finder, or run from a shell:
#      ./preview_enemies.command
#
#  Reuses play.command (Godot discovery + C# build) and just points Godot at
#  the EnemyGallery scene instead of the main game scene.
#
#  Constitution I: ASCII only.
# =============================================================================
set -e
cd "$(cd "$(dirname "$0")" && pwd)"
exec ./play.command res://EnemyGallery.tscn
