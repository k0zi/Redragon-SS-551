#!/usr/bin/env bash
# Builds the Redragon SS-551 .deb and .rpm packages into artifacts/packages/.
#
# Versioning: Nerdbank.GitVersioning, version.json holds "<year>.<month>" (e.g. 26.10) and the git
# height becomes the patch number (26.10.0, 26.10.1, ...). When the month changes, version.json is
# updated to the current year/month first (commit it so the next builds count from there).
#
# Build requirements: .NET 10 SDK, cmake, gcc, pkg-config, hidapi headers (libhidapi-dev / hidapi-devel).
#
# Usage: packaging/build-packages.sh [--no-bump] [deb|rpm ...]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/packages"
STAGE="$ROOT/artifacts/stage"
TOOLS="$ROOT/artifacts/tools"
NFPM_VERSION=2.48.0
NFPM_SHA256=6d7f9a28b2f397934403950fcbba83f598b08d02edbe79a0815b3b4897341750

bump=true
formats=()
for arg in "$@"; do
    case "$arg" in
        --no-bump) bump=false ;;
        deb|rpm) formats+=("$arg") ;;
        *) echo "Usage: $0 [--no-bump] [deb|rpm ...]" >&2; exit 2 ;;
    esac
done
[[ ${#formats[@]} -gt 0 ]] || formats=(deb rpm)

missing=()
for cmd in dotnet cmake gcc pkg-config objdump; do
    command -v "$cmd" >/dev/null || missing+=("$cmd")
done
command -v pkg-config >/dev/null && ! pkg-config --exists hidapi-hidraw && missing+=("hidapi-hidraw (pkg-config)")
if [[ ${#missing[@]} -gt 0 ]]; then
    echo "Missing build requirements: ${missing[*]}" >&2
    echo "  Debian/Ubuntu: sudo apt install cmake gcc pkg-config libhidapi-dev  (+ .NET 10 SDK)" >&2
    echo "  Fedora:        sudo dnf install cmake gcc pkgconf hidapi-devel      (+ .NET 10 SDK)" >&2
    echo "  openSUSE:      sudo zypper install cmake gcc pkg-config libhidapi-devel  (+ .NET 10 SDK)" >&2
    exit 1
fi

cd "$ROOT"
dotnet tool restore >/dev/null

# ── version ──
expected="$(date +%y).$(date +%-m)"
current="$(sed -n 's/^ *"version": *"\([^"]*\)".*/\1/p' version.json)"
if [[ "$current" != "$expected" ]]; then
    if $bump; then
        # Not "nbgv set-version": that rewrites version.json and drops the other settings.
        sed -i "s/^\( *\"version\": *\"\)[^\"]*\"/\1$expected\"/" version.json
        echo "version.json: $current -> $expected (commit this change)"
    else
        echo "warning: version.json is $current, current month would be $expected" >&2
    fi
fi

version="$(dotnet nbgv get-version -v SimpleVersion)"
prerelease=""
if [[ "$(dotnet nbgv get-version -v PublicRelease)" != "True" ]]; then
    # Builds outside the main branch / release tags sort before the release of the same version.
    prerelease="g$(dotnet nbgv get-version -v GitCommitIdShort)"
fi
echo "Redragon SS-551 version: $version${prerelease:+~$prerelease}"

# ── native driver (libss551 + ss551-probe) ──
# SS551.Native picks up driver/build/libss551.so* at publish time, so this has to come first.
# $ORIGIN lets ss551-probe find libss551.so.0 next to itself in /usr/lib/ss551.
cmake -S driver -B driver/build -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_BUILD_WITH_INSTALL_RPATH=ON '-DCMAKE_INSTALL_RPATH=$ORIGIN'
cmake --build driver/build --parallel

# ── app ──
rm -rf "$STAGE"
mkdir -p "$STAGE" "$OUT"
dotnet publish "$ROOT/app/src/SS551.App/SS551.App.csproj" \
    -c Release -r linux-x64 --self-contained true -o "$STAGE/app"
find "$STAGE/app" -name '*.pdb' -delete
cp driver/build/ss551-probe "$STAGE/app/"
sed "s|@BIN@|/usr/bin/ss551|" "$ROOT/packaging/ss551.desktop" > "$STAGE/ss551.desktop"
# Autostart on login, hidden in the tray. TryExec keeps a leftover entry inert once the app is gone.
{
    sed "s|@BIN@|/usr/bin/ss551 --hidden|" "$ROOT/packaging/ss551.desktop"
    echo "TryExec=/usr/bin/ss551"
    echo "X-GNOME-Autostart-enabled=true"
} > "$STAGE/ss551-autostart.desktop"
# A umask of 002 would otherwise ship group-writable files.
chmod -R go-w "$STAGE"

# Lowest glibc the package runs on: the newest GLIBC_x.y symbol version any bundled binary needs.
# The .NET runtime needs 2.27; the driver needs whatever the build host's headers redirect to.
# objdump fails on non-ELF files (some executable *.dll), hence the "|| true".
glibc="$({ find "$STAGE/app" -type f \( -name '*.so*' -o -perm -u+x \) -exec objdump -T {} + 2>/dev/null || true; } \
    | grep -o 'GLIBC_[0-9.]*' | sed 's/^GLIBC_//' | sort -uV | tail -n 1)"
[[ -n "$glibc" ]] || { echo "Could not determine the required glibc version (objdump missing?)" >&2; exit 1; }
echo "Requires glibc >= $glibc"

# ── nfpm (single static binary, builds both formats without dpkg/rpmbuild) ──
nfpm="$(command -v nfpm || true)"
if [[ -z "$nfpm" ]]; then
    nfpm="$TOOLS/nfpm-$NFPM_VERSION/nfpm"
    if [[ ! -x "$nfpm" ]]; then
        archive="$TOOLS/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
        mkdir -p "$(dirname "$nfpm")"
        curl -fsSL -o "$archive" \
            "https://github.com/goreleaser/nfpm/releases/download/v$NFPM_VERSION/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz"
        echo "$NFPM_SHA256  $archive" | sha256sum -c --quiet -
        tar -xzf "$archive" -C "$(dirname "$nfpm")" nfpm
        rm "$archive"
    fi
fi

export SS551_VERSION="$version" SS551_PRERELEASE="$prerelease" SS551_GLIBC="$glibc"
for format in "${formats[@]}"; do
    rm -f "$OUT"/ss551*."$format"
    "$nfpm" package --config "$ROOT/packaging/nfpm.yaml" --packager "$format" --target "$OUT/"
done
ls -1 "$OUT"
