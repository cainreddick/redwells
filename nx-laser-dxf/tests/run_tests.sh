#!/usr/bin/env bash
# Builds the journals, compile-checks them as C# 5 against NXOpen stubs, and
# runs the core unit tests. Needs Mono's mcs/mono (apt install mono-mcs mono-runtime).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build
MCS="mcs -noconfig -nologo -langversion:5 -warnaserror+ -nowarn:1591"

python3 tools/build_journal.py

# 1) Core on its own, referencing only mscorlib + System (what any NX journal gets).
$MCS -target:library -r:System.dll -out:build/LaserDxf.Core.dll src/core/*.cs

# 2) Every merged journal against the NXOpen stubs.
$MCS -target:library -r:System.dll -out:build/NXOpenStubs.dll stubs/NXOpenStubs.cs
for j in dist/*.cs; do
  $MCS -target:library -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:build/NXOpenStubs.dll -out:"build/$(basename "$j" .cs).dll" "$j"
  echo "compile-checked $j"
done

# 3) Unit tests.
$MCS -target:exe -r:System.dll -r:build/LaserDxf.Core.dll -out:build/tests.exe tests/*.cs
cp build/LaserDxf.Core.dll build/ 2>/dev/null || true
mono build/tests.exe build/test-output

# 4) Validate the sample DXFs with ezdxf.
python3 tests/validate_dxf.py build/test-output
python3 tests/validate_pdf.py build/test-output

# 5) Keep the sample DXFs in the repo so they can be tried in the laser CAM software.
mkdir -p samples
cp build/test-output/testplate_R2000.dxf build/test-output/irregular_R2000.dxf build/test-output/irregular_R12.dxf samples/
cp build/test-output/irregular_TEXT_R2000.dxf samples/
cp build/test-output/sheet_irregular.pdf build/test-output/sheet_small.pdf samples/

# 6) UI smoke test (preview window + options dialog) when an X server can be faked.
if command -v xvfb-run >/dev/null 2>&1; then
  mcs -noconfig -nologo -langversion:5 -target:exe -main:LaserDxf.Tests.PreviewSmoke -r:System.dll -r:System.Drawing.dll \
    -r:System.Windows.Forms.dll -r:build/LaserDxf.Core.dll -out:build/smoke.exe \
    tests/ui/PreviewSmoke.cs src/nx/PreviewWindow.cs src/nx/OptionsDialog.cs tests/Synth.cs tests/ExportTests.cs tests/CoreTests.cs
  (cd build && timeout 120 xvfb-run -a -s "-screen 0 1300x950x24" mono smoke.exe test-output 2>/dev/null | grep -v "^$")
fi
