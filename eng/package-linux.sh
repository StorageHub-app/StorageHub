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

# -p:Version, which the Windows script has always passed and this one never did. Without it the
# binaries carry VersionPrefix from Directory.Build.props while DEBIAN/control says whatever
# --version asked for, and the updater compares the version the running app reports against the
# feed -- so a 2.0.1 package built this way would report 2.0.0 and offer to install itself for
# ever.
dotnet publish src/StorageHub.Desktop/StorageHub.Desktop.csproj \
  -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true \
  -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true \
  -p:PublishSingleFile=false -p:DebugType=none \
  -o "$APP_DIR" --nologo -v quiet

dotnet publish src/StorageHub.Agent.Host/StorageHub.Agent.Host.csproj \
  -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true \
  -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true \
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
exec /opt/storagehub/StorageHub.Desktop "$@"
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
StartupWMClass=StorageHub.Desktop
LAUNCHER

# Installed at the size it actually is. It used to be copied verbatim into 256x256/, where a 1024
# pixel image is a lie that icon themes believe: a desktop that trusts the directory name scales a
# quarter of the artwork into the slot. hicolor allows any size directory, and every theme engine
# will scale down from here. Pre-rendered 48/64/128/256 belong in assets/branding as committed
# files -- that is a drawing job, not a packaging one, and generating them here would make the
# package depend on whichever image tool the build machine happened to have.
#
# The size is read out of the PNG header rather than assumed or asked of a tool, so the directory
# cannot drift from the artwork again and the answer is the same on every build machine. A PNG
# always opens with an 8 byte signature, a 4 byte length, "IHDR", then the width as a big-endian
# 32 bit integer at offset 16.
ICON_SIZE="$(od -An -tu4 --endian=big -j16 -N4 assets/branding/storagehub-icon.png | tr -d ' ')"
if ! [ "$ICON_SIZE" -gt 0 ] 2>/dev/null; then
  echo "Could not read the icon's width from assets/branding/storagehub-icon.png." >&2
  exit 1
fi
install -d "$STAGE/usr/share/icons/hicolor/${ICON_SIZE}x${ICON_SIZE}/apps"
cp assets/branding/storagehub-icon.png \
  "$STAGE/usr/share/icons/hicolor/${ICON_SIZE}x${ICON_SIZE}/apps/storagehub.png"

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

# What the package depends on, worked out rather than remembered.
#
# dpkg-shlibdeps reads the ELF NEEDED entries of everything shipped and asks dpkg which package
# provides each one, so libc6, libgcc-s1, libstdc++6 and libfontconfig1 come out with the version
# bounds the build distribution actually requires. The hand-written list this replaces named
# zlib1g, which nothing links, and never named fontconfig, without which the desktop does not
# start at all.
#
# ICU and OpenSSL are not in that list and cannot be. .NET dlopens both by soname at run time, so
# neither appears in any NEEDED entry and dpkg-shlibdeps cannot see them; they have to be stated.
# Microsoft's own .NET packages state them the same way and for the same reason. The difference
# here is that the ICU alternation is generated across a range instead of frozen: the frozen one
# stopped at libicu72, and Ubuntu 24.04 -- the current LTS, and what CI would build on -- ships
# libicu74, so the package unpacked and then refused to configure.
echo "==> resolving library dependencies"

SHLIB_ROOT="$STAGE/.shlibdeps"
install -d "$SHLIB_ROOT/debian"
cat > "$SHLIB_ROOT/debian/control" <<'SHLIBCONTROL'
Source: storagehub

Package: storagehub
Architecture: amd64
SHLIBCONTROL

# Everything executable and every shared object, except the LTTng trace provider. That one is
# loaded only when tracing is switched on, and letting it pull liblttng-ust in as a hard
# dependency would make a tracing library mandatory to browse a folder.
SHLIB_TARGETS=()
while IFS= read -r -d '' candidate; do
  case "$candidate" in *libcoreclrtraceptprovider.so) continue ;; esac
  SHLIB_TARGETS+=("$candidate")
done < <(find "$APP_DIR" "$AGENT_DIR" -type f \( -name '*.so' -o -perm -u+x \) -print0)

# --ignore-missing-info because the runtime ships its own shared objects -- libcoreclr, libclrjit,
# libSkiaSharp and the rest -- which belong to no package on the system and would otherwise each be
# a fatal "no dependency information found". Its complaints are kept rather than discarded, so a
# real failure here has something to read.
SHLIB_LOG="$STAGE/.shlibdeps.log"
SHLIB_DEPENDS="$(
  cd "$SHLIB_ROOT" &&
  dpkg-shlibdeps -O --ignore-missing-info "${SHLIB_TARGETS[@]}" 2>"$SHLIB_LOG" |
    sed -n 's/^shlibs:Depends=//p'
)" || true
rm -rf "$SHLIB_ROOT"

if [ -z "$SHLIB_DEPENDS" ]; then
  echo "dpkg-shlibdeps produced no dependencies. Is dpkg-dev installed?" >&2
  sed -n '1,20p' "$SHLIB_LOG" >&2
  exit 1
fi

echo "    $SHLIB_DEPENDS"

# Newest first, so a distribution that has several installed satisfies it with the current one.
ICU_DEPENDS="$(
  for version in $(seq 80 -1 63); do printf 'libicu%s | ' "$version"; done | sed 's/ | $//'
)"

DEPENDS="$SHLIB_DEPENDS, $ICU_DEPENDS, libssl3t64 | libssl3 | libssl1.1"

install -d "$STAGE/DEBIAN"
cat > "$STAGE/DEBIAN/control" <<CONTROL
Package: storagehub
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: StorageHub <noreply@storagehub.app>
Installed-Size: $INSTALLED_KB
Depends: $DEPENDS
Recommends: liblttng-ust1 | liblttng-ust0
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
    # A user unit was just installed, and it is the *user* managers that have to be told. The bare
    # daemon-reload this replaces reloaded the system manager, which never reads
    # /usr/lib/systemd/user and so never saw the file -- meaning that until the next login,
    # "systemctl --user enable storagehub-agent" would answer that no such unit exists.
    #
    # A user manager is reached by running systemctl as that user with their runtime directory.
    if command -v systemctl >/dev/null 2>&1 && command -v loginctl >/dev/null 2>&1; then
        loginctl list-users --no-legend 2>/dev/null | while read -r uid user _; do
            [ -n "$uid" ] && [ -n "$user" ] && [ -d "/run/user/$uid" ] || continue
            runuser -u "$user" -- env "XDG_RUNTIME_DIR=/run/user/$uid" \
                systemctl --user daemon-reload >/dev/null 2>&1 || true
        done
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

        # Stop it for whoever is signed in. This used to be
        # "systemctl --user --machine=<uid>@", which is not valid syntax -- --machine takes
        # <user>@<container>, never a bare uid -- so every call failed and the || true hid it.
        # The agent went on running from a directory that no longer existed until the next logout.
        #
        # loginctl rather than "users", which lists tty sessions and misses a graphical login.
        if command -v loginctl >/dev/null 2>&1; then
            loginctl list-users --no-legend 2>/dev/null | while read -r uid user _; do
                [ -n "$uid" ] && [ -n "$user" ] && [ -d "/run/user/$uid" ] || continue
                runuser -u "$user" -- env "XDG_RUNTIME_DIR=/run/user/$uid" \
                    systemctl --user stop storagehub-agent.service >/dev/null 2>&1 || true
            done
        fi
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

# Every file in the bundle, not just the .deb. The release job validates that each asset it is
# about to publish appears in SHA256SUMS, and release-version.txt is one of those assets -- so a
# bundle hashing only the package was rejected by our own validation before it ever reached anyone.
# The Windows script has always hashed everything it ships.
( cd "$OUTPUT" && find . -maxdepth 1 -type f ! -name SHA256SUMS -printf '%P\n' |
    LC_ALL=C sort | xargs sha256sum > SHA256SUMS )

echo "==> built"
dpkg-deb --info "$PACKAGE" | sed -n '1,12p'
echo
ls -la "$OUTPUT"
