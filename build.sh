#!/usr/bin/env bash
# Builds TATimesheet.exe with Mono (Linux/macOS). On Windows use build.bat instead.
#   ./build.sh            -> dist/TATimesheet.exe
#   ./build.sh test DOCX  -> also builds and runs the smoke test against a real timesheet .docx
set -euo pipefail
cd "$(dirname "$0")"

REFS="-r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Xml.dll -r:System.Xml.Linq.dll -r:System.IO.Compression.dll"
RES="-resource:src/Resources/TimesheetTemplate.docx,TATimesheet.TimesheetTemplate.docx"
# -langversion:5 keeps the code buildable by the csc.exe that ships inside Windows.
FLAGS="-langversion:5 -sdk:4.5 -optimize+ -nologo -warnaserror+"

mkdir -p dist
mcs $FLAGS -target:winexe -win32icon:src/Resources/app.ico $REFS $RES -out:dist/TATimesheet.exe src/*.cs
echo "built dist/TATimesheet.exe"

if [ "${1:-}" = "test" ]; then
  out="${3:-build/test-output}"
  mkdir -p build
  mcs $FLAGS -target:exe $REFS $RES -out:build/SmokeTest.exe $(ls src/*.cs | grep -v -E 'Form|Program') tests/SmokeTest.cs
  mono build/SmokeTest.exe "$2" "$out"
fi
