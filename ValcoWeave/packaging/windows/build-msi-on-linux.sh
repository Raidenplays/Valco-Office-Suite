#!/usr/bin/env bash
# Cross-builds Valco Weave for Windows on Ubuntu/Kubuntu and packages it as
# ValcoWeave-Setup.msi with the same Vista-style setup wizard as the other Valco apps.
# Needs: sudo apt install mingw-w64 wixl msitools ; rustup target add x86_64-pc-windows-gnu
# (Regenerate the icon + wizard art with: pip install cairosvg pillow && python3 tools/gen_assets.py)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
VERSION="$(node -p "require('./package.json').version")"
npm run build
export CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER=x86_64-w64-mingw32-gcc
export CC_x86_64_pc_windows_gnu=x86_64-w64-mingw32-gcc
(cd src-tauri && cargo build --release --target x86_64-pc-windows-gnu --features tauri/custom-protocol)
OUT="$ROOT/packaging/windows/out"; rm -rf "$OUT"; mkdir -p "$OUT"
REL=src-tauri/target/x86_64-pc-windows-gnu/release
cp "$REL/valco-weave.exe" "$OUT/ValcoWeave.exe"
# webview2-com-sys copies the loader into its build output; where that is depends on the cargo version
LOADER="$(find src-tauri/target -path '*webview2-com-sys*' -path '*x64*' -name WebView2Loader.dll 2>/dev/null | head -1)"
if [ -z "$LOADER" ]; then LOADER="$(find "${CARGO_HOME:-$HOME/.cargo}/registry/src" -path '*webview2-com-sys*/x64/WebView2Loader.dll' 2>/dev/null | head -1)"; fi
[ -n "$LOADER" ] || { echo "WebView2Loader.dll not found"; exit 1; }
cp "$LOADER" "$OUT/"
cp packaging/assets/ValcoWeave.ico "$OUT/icon.ico"
sed 's/$/\r/' LICENSE > "$OUT/LICENSE.txt"
sed "s/ Version=\"[0-9.]*\"/ Version=\"$VERSION.0\"/" packaging/windows/valco-weave.wxs > "$OUT/valco-weave.wxs"
(cd "$OUT" && wixl -a x64 -o ValcoWeave-Setup.msi valco-weave.wxs)
python3 packaging/windows/msi_wixui.py "$OUT/ValcoWeave-Setup.msi"
echo "Built $OUT/ValcoWeave-Setup.msi"
