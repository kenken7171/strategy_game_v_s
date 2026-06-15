#!/bin/bash
# =============================================================================
#  Chronicle Knights -- local launcher (macOS)
# -----------------------------------------------------------------------------
#  Run the game natively on this Mac. Double-click in Finder, or from a shell:
#      ./play.command
#  Pass -e (or --editor) to open the Godot editor instead of playing:
#      ./play.command -e
#
#  Requires the .NET (mono) build of Godot 4.3 reachable as `godot` on PATH
#  (a symlink to /Applications/Godot_mono.app/Contents/MacOS/Godot).
#  The project targets net8.0 but its runtimeconfig rolls forward to the
#  newest installed .NET runtime, so no .NET 8 install is required.
#
#  Constitution I: ASCII only.
# =============================================================================
set -e

# Resolve to this script's own directory (the Godot project root).
cd "$(cd "$(dirname "$0")" && pwd)"

GODOT_BIN="$(command -v godot || true)"
if [ -z "${GODOT_BIN}" ]; then
  GODOT_BIN="/Applications/Godot_mono.app/Contents/MacOS/Godot"
fi

if [ ! -x "${GODOT_BIN}" ]; then
  echo "ERROR: Godot (.NET/mono) not found."
  echo "Install the .NET build of Godot 4.3 and link it as 'godot', e.g.:"
  echo "  ln -sf /Applications/Godot_mono.app/Contents/MacOS/Godot /usr/local/bin/godot"
  exit 1
fi

echo "Godot: ${GODOT_BIN}"
"${GODOT_BIN}" --version

# Ensure the C# assembly exists (first run / after pull). Harmless if current.
if command -v dotnet >/dev/null 2>&1; then
  echo "Building C# (ChronicleKnights.csproj)..."
  dotnet build ChronicleKnights.csproj -c Debug --nologo --verbosity quiet
fi

echo "Launching Chronicle Knights..."
exec "${GODOT_BIN}" --path . "$@"
