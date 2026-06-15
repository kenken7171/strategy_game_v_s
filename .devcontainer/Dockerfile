# syntax=docker/dockerfile:1
# =============================================================================
#  Chronicle Knights -- Dev Container image
# -----------------------------------------------------------------------------
#  Official Microsoft .NET 8 SDK base (Debian) + Godot Engine .NET (mono) headless.
#  Goal: clone the repo, open in a Dev Container, and run `dotnet build` / `dotnet test`
#  and `godot --headless` with ZERO host-local dependency, on any host architecture.
#
#  Cross-arch defense: TARGETPLATFORM (set automatically by BuildKit per host) selects
#  the matching Godot Linux asset (linux_x86_64 for amd64 / linux_arm64 for arm64),
#  so Intel Mac / Apple Silicon Mac / Windows x86_64 hosts all build a correct image.
#
#  Constitution I: ASCII only. No non-ASCII characters anywhere in this file.
# =============================================================================

ARG DOTNET_SDK_TAG=8.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG}

# Pinned to match the project's Godot.NET.Sdk (4.3.x). Override via build args if needed.
ARG GODOT_VERSION=4.3
ARG GODOT_RELEASE=stable

# Provided automatically by BuildKit (e.g. "linux/amd64", "linux/arm64").
ARG TARGETPLATFORM

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# --- Download tools + Godot headless runtime dependencies (X/GL/audio libs) ---
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        wget \
        unzip \
        ca-certificates \
        libfontconfig1 \
        libx11-6 \
        libxcursor1 \
        libxinerama1 \
        libxrandr2 \
        libxi6 \
        libgl1 \
        libegl1 \
        libgl1-mesa-dri \
        libxkbcommon0 \
        libasound2 \
        libpulse0 \
    && rm -rf /var/lib/apt/lists/*

# --- Fetch the Godot .NET (mono) Linux build per host architecture ---
#  The mono zip extracts to a directory that holds BOTH the executable and the
#  adjacent GodotSharp/ folder; they MUST stay together, so we extract the whole
#  directory under /opt and symlink the executable into /usr/local/bin (PATH).
RUN set -eux; \
    case "${TARGETPLATFORM:-linux/amd64}" in \
        "linux/arm64") GODOT_ARCH="linux_arm64" ;; \
        "linux/amd64") GODOT_ARCH="linux_x86_64" ;; \
        *)             GODOT_ARCH="linux_x86_64" ;; \
    esac; \
    GODOT_PKG="Godot_v${GODOT_VERSION}-${GODOT_RELEASE}_mono_${GODOT_ARCH}"; \
    GODOT_URL="https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-${GODOT_RELEASE}/${GODOT_PKG}.zip"; \
    wget -q "${GODOT_URL}" -O /tmp/godot.zip; \
    unzip -q /tmp/godot.zip -d /opt; \
    rm /tmp/godot.zip; \
    # The mono zip names the executable with a dot before the arch (e.g. ...mono_linux.x86_64) \
    # while its folder uses an underscore; discover the real binary instead of guessing. \
    GODOT_BIN="$(find /opt -maxdepth 2 -type f -name 'Godot_v*mono*' ! -name '*.sh' -print | head -n 1)"; \
    test -n "${GODOT_BIN}"; \
    chmod +x "${GODOT_BIN}"; \
    ln -sf "${GODOT_BIN}" /usr/local/bin/godot; \
    godot --version

# Default workdir is overridden by the Dev Container mount; kept for direct `docker run`.
WORKDIR /workspace
