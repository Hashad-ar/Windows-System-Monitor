#!/usr/bin/env bash
set -euo pipefail
# Linux build, tests and Windows cross-publication. Windows execution is still required for acceptance.
source "$(dirname "${BASH_SOURCE[0]}")/setup-cloud.sh"
dotnet publish src/WindowsSystemMonitor/WindowsSystemMonitor.csproj -c Release -r win-x64 \
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false \
    -p:DebugType=None -p:DebugSymbols=false -o dist/portable
cp docs/portable-readme.txt dist/portable/README.txt
makensis -V3 -WX -DAPP_VERSION=1.0.0 installer/WindowsSystemMonitor.nsi
python3 - <<'PY'
from pathlib import Path
import hashlib, struct, zipfile
root = Path.cwd()
exe = root / 'dist/portable/WindowsSystemMonitor.exe'
installer = root / 'dist/WindowsSystemMonitor-Setup.exe'
for file, machine in [(exe, 0x8664), (installer, 0x14c)]:
    data = file.read_bytes()
    if data[:2] != b'MZ': raise SystemExit(f'Missing PE signature: {file}')
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe+4] != b'PE\0\0' or struct.unpack_from('<H', data, pe+4)[0] != machine:
        raise SystemExit(f'Unexpected executable architecture: {file}')
    if struct.unpack_from('<H', data, pe+24+68)[0] != 2:
        raise SystemExit(f'Expected Windows GUI executable: {file}')
archive = root / 'dist/WindowsSystemMonitor-Portable-win-x64.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    z.write(exe, exe.name)
    z.write(root / 'dist/portable/README.txt', 'README.txt')
with zipfile.ZipFile(archive) as z:
    if z.testzip() is not None or z.read(exe.name) != exe.read_bytes():
        raise SystemExit('Portable archive verification failed')
checks = []
for file in [exe, archive, installer]:
    digest = hashlib.sha256(file.read_bytes()).hexdigest()
    checks.append(f'{digest}  {file.relative_to(root / "dist")}')
    print(f'{file.relative_to(root)}: {file.stat().st_size:,} bytes; SHA256 {digest}')
(root / 'dist/SHA256SUMS.txt').write_text('\n'.join(checks) + '\n')
print('PASS: installer/executable PE headers, portable ZIP CRCs and executable equality')
PY
