#!/usr/bin/env bash
set -euo pipefail
# Development setup for this Linux cloud checkout; end users only need the Windows release.
workspace_tools=/workspace/.tools
sdk_root="$workspace_tools/dotnet"
sdk_version=10.0.403
sdk_archive="$workspace_tools/downloads/dotnet-sdk-$sdk_version-linux-x64.tar.gz"
mkdir -p "$sdk_root" "$workspace_tools/downloads"
if [[ ! -x "$sdk_root/dotnet" ]] || [[ "$("$sdk_root/dotnet" --version)" != "$sdk_version" ]]; then
    curl --fail --location --silent --show-error --retry 2 \
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdk_version/dotnet-sdk-$sdk_version-linux-x64.tar.gz" \
        -o "$sdk_archive"
    # SHA-512 from Microsoft's official 10.0 release metadata, verified during initial setup.
    python3 - "$sdk_archive" <<'PY'
import hashlib, sys
expected = 'b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5'
with open(sys.argv[1], 'rb') as archive:
    actual = hashlib.file_digest(archive, 'sha512').hexdigest()
if actual != expected:
    raise SystemExit('SDK checksum mismatch; installation stopped.')
PY
    tar -xzf "$sdk_archive" -C "$sdk_root"
fi
# NSIS cross-compiles a conventional Windows installer; no system packages are installed.
nsis_root="$workspace_tools/nsis"
if [[ ! -x "$nsis_root/usr/bin/makensis" ]]; then
    apt_state="$workspace_tools/apt"
    mkdir -p "$apt_state/lists/partial" "$apt_state/archives/partial" "$apt_state/empty" "$nsis_root"
    cat > "$apt_state/config" <<APT
Dir::Etc::Parts "$apt_state/empty";
Dir::Etc::main "$apt_state/empty.conf";
Dir::Etc::sourcelist "$apt_state/sources.list";
Dir::Etc::sourceparts "$apt_state/empty";
Dir::State::lists "$apt_state/lists";
Dir::Cache::archives "$apt_state/archives";
Acquire::Languages "none";
APT
    : > "$apt_state/empty.conf"
    cat > "$apt_state/sources.list" <<'SOURCES'
deb [signed-by=/usr/share/keyrings/debian-archive-keyring.gpg] https://deb.debian.org/debian trixie main
SOURCES
    APT_CONFIG="$apt_state/config" https_proxy="${HTTPS_PROXY:-}" apt-get update
    (
        cd "$apt_state/archives"
        APT_CONFIG="$apt_state/config" https_proxy="${HTTPS_PROXY:-}" apt-get download nsis nsis-common
        for package in nsis_*.deb nsis-common_*.deb; do
            dpkg-deb -x "$package" "$nsis_root"
        done
    )
fi
export NSISDIR="$nsis_root/usr/share/nsis"
export PATH="$nsis_root/usr/bin:$PATH"
makensis -VERSION
export DOTNET_ROOT="$sdk_root"
export PATH="$sdk_root:$PATH"
export DOTNET_CLI_HOME="$workspace_tools/dotnet-home"
export NUGET_PACKAGES="$workspace_tools/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd /workspace/Windows-System-Monitor
dotnet build src/WindowsSystemMonitor/WindowsSystemMonitor.csproj -c Release
dotnet run --project tests/WindowsSystemMonitor.Tests -c Release
dotnet run --project tests/WindowsSystemMonitor.ProcessTests -c Release
