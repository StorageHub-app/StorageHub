#!/usr/bin/env bash
#
# Builds the StorageHub .deb.
#
# The package is system-wide because that is what a Debian package is; the agent it installs is
# not. StorageHub runs one agent per user under `systemd --user`, so this ships the unit as a
# template under /usr/lib/systemd/user and leaves enabling it to each user - which is the step the
# desktop's own settings page performs. Installing needs root once; nothing StorageHub does
# afterwards does.
#
# Usage: eng/package-linux.sh [--version 2.0.0] [--output artifacts/linux]

set -euo pipefail

VERSION=""
OUTPUT="artifacts/linux"
CONFIGURATION="Release"
RUNTIME="linux-x64"

while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO"

if [ -z "$VERSION" ]; then
  if [ -f release-version.txt ]; then
    VERSION="$(tr -d '[:space:]' < release-version.txt)"
  else
    echo "No --version given and release-version.txt is absent." >&2
    exit 2
  fi
fi

# Debian versions may not carry a '+build' suffix; the informational version keeps it.
DEB_VERSION="${VERSION%%+*}"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APP_DIR="$STAGE/opt/storagehub"
AGENT_DIR="$APP_DIR/agent"

echo "==> publishing $CONFIGURATION/$RUNTIME"
mkdir -p "$APP_DIR" "$AGENT_DIR"

dotnet publish src/StorageHub.Desktop.Avalonia/StorageHub.Desktop.Avalonia.csproj \
  -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=none \
  -o "$APP_DIR" --nologo -v quiet

dotnet publish src/StorageHub.Agent.Host/StorageHub.Agent.Host.csproj \
  -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=none \
  -o "$AGENT_DIR" --nologo -v quiet

# The one native asset that resolves only for a RID-specific publish, and the first thing to go
# missing in a package that was built the wrong way. PagedListingIndex needs it, and the symptom
# without it is the browser failing to open rather than anything mentioning SQLite.
if [ ! -f "$AGENT_DIR/libe_sqlite3.so" ]; then
  echo "libe_sqlite3.so is missing from the agent publish; the package would not browse." >&2
  exit 1
fi

echo "==> laying out the package"

install -d "$STAGE/usr/bin"
cat > "$STAGE/usr/bin/storagehub" <<'LAUNCHER'
#!/bin/sh
exec /opt/storagehub/StorageHub.Desktop.Avalonia "$@"
LAUNCHER
chmod 0755 "$STAGE/usr/bin/storagehub"

install -d "$STAGE/usr/share/applications"
cat > "$STAGE/usr/share/applications/storagehub.desktop" <<'LAUNCHER'
[Desktop Entry]
Type=Application
Name=StorageHub
Comment=Browse, transfer and synchronise local and remote storage
Exec=/usr/bin/storagehub %U
Icon=storagehub
Terminal=false
Categories=Utility;FileTools;FileTransfer;
StartupWMClass=StorageHub.Desktop.Avalonia
LAUNCHER

install -d "$STAGE/usr/share/icons/hicolor/256x256/apps"
cp assets/branding/storagehub-icon.png "$STAGE/usr/share/icons/hicolor/256x256/apps/storagehub.png"

# A user unit, not a system one. Enabling it is per-user and needs no privilege, which is what
# keeps "runs when nobody is signed in" available through lingering rather than through root.
# It has to match what SystemdUserAutostart writes, because either may have registered it.
install -d "$STAGE/usr/lib/systemd/user"
cat > "$STAGE/usr/lib/systemd/user/storagehub-agent.service" <<'UNIT'
[Unit]
Description=StorageHub Agent
PartOf=graphical-session.target

[Service]
Type=simple
ExecStart=/opt/storagehub/agent/StorageHub.Agent.Host
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT

install -d "$STAGE/usr/share/doc/storagehub"
cp LICENSE "$STAGE/usr/share/doc/storagehub/copyright" 2>/dev/null || true

INSTALLED_KB="$(du -ks "$STAGE" | cut -f1)"

install -d "$STAGE/DEBIAN"
cat > "$STAGE/DEBIAN/control" <<CONTROL
Package: storagehub
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: StorageHub <noreply@storagehub.app>
Installed-Size: $INSTALLED_KB
Depends: libc6 (>= 2.31), libstdc++6, zlib1g, libicu72 | libicu71 | libicu70 | libicu67 | libicu66
Description: Browse, transfer and synchronise local and remote storage
 StorageHub manages local, UNC, S3, FTP, FTPS and SFTP storage from one window,
 with a background agent that carries out transfers and scheduled synchronisation.
 .
 The agent runs per user under systemd --user. Enable it from StorageHub's own
 settings, or with: systemctl --user enable --now storagehub-agent
CONTROL

# The agent is not started here. It belongs to a user, and a package's postinst runs as root with
# no user to attach it to; starting it from here would run one agent as root against a data root
# no user owns.
cat > "$STAGE/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e

if [ "$1" = "configure" ]; then
    if command -v systemctl >/dev/null 2>&1; then
        systemctl daemon-reload >/dev/null 2>&1 || true
    fi
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -qtf /usr/share/icons/hicolor || true
    fi
fi

exit 0
POSTINST
chmod 0755 "$STAGE/DEBIAN/postinst"

# Removing the package stops the agent for whoever is signed in. It deliberately leaves each
# user's data alone: ~/.local/share/storagehub holds their connections and vault, and a package
# removal is not a request to delete those. Purging does not remove them either, because they
# belong to users rather than to the package.
cat > "$STAGE/DEBIAN/prerm" <<'PRERM'
#!/bin/sh
set -e

if [ "$1" = "remove" ] || [ "$1" = "upgrade" ]; then
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --global disable storagehub-agent.service >/dev/null 2>&1 || true
        for uid in $(users 2>/dev/null | tr ' ' '\n' | sort -u | while read -r u; do id -u "$u" 2>/dev/null; done); do
            [ -n "$uid" ] || continue
            systemctl --user --machine="$uid@" stop storagehub-agent.service >/dev/null 2>&1 || true
        done
    fi
fi

exit 0
PRERM
chmod 0755 "$STAGE/DEBIAN/prerm"

mkdir -p "$OUTPUT"
PACKAGE="$OUTPUT/storagehub_${DEB_VERSION}_amd64.deb"

echo "==> building $PACKAGE"
# --root-owner-group so the package does not carry the build account's uid, which would otherwise
# install files owned by a uid that means something else on the target machine.
dpkg-deb --root-owner-group --build "$STAGE" "$PACKAGE" >/dev/null

echo "$VERSION" > "$OUTPUT/release-version.txt"
( cd "$OUTPUT" && sha256sum "$(basename "$PACKAGE")" > SHA256SUMS )

echo "==> built"
dpkg-deb --info "$PACKAGE" | sed -n '1,12p'
echo
ls -la "$OUTPUT"
