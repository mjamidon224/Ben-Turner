#!/usr/bin/env bash
# Builds the exes on Linux/macOS with Microsoft's C# compiler (Roslyn, from the .NET SDK) against
# .NET Framework 4.x reference assemblies (from Mono). On Windows just double-click build.bat.
#   ./build.sh            -> dist/TATimesheet.exe and dist/TATimesheet-Test.exe (practice copy)
#   ./build.sh test DOCX  -> also builds and runs the smoke test (with mono) against a real timesheet .docx
# Needs: dotnet-sdk (for csc.dll), mono-devel (reference assemblies + mono to run the tests).
set -euo pipefail
cd "$(dirname "$0")"

CSC_DLL=$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll /usr/share/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1 || true)
[ -n "$CSC_DLL" ] || { echo "Can't find csc.dll - install the .NET SDK (e.g. apt install dotnet-sdk-8.0)"; exit 1; }
REF=/usr/lib/mono/4.5-api

csc() { dotnet "$CSC_DLL" "$@"; }

REFS="-r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Xml.dll -r:$REF/System.Xml.Linq.dll -r:$REF/System.IO.Compression.dll"
RES="-resource:src/Resources/TimesheetTemplate.docx,TATimesheet.TimesheetTemplate.docx"
# -langversion:5 keeps the code buildable by the csc.exe that ships inside Windows (build.bat).
FLAGS="-nologo -noconfig -nostdlib+ -langversion:5 -optimize+ -debug- -warnaserror+ -platform:anycpu"
WIN="-target:winexe -win32manifest:src/app.manifest"

mkdir -p dist
csc $FLAGS $WIN -win32icon:src/Resources/app.ico $REFS $RES -out:dist/TATimesheet.exe src/*.cs
echo "built dist/TATimesheet.exe"
csc $FLAGS $WIN -define:TEST_BUILD -win32icon:src/Resources/app-test.ico $REFS $RES -out:dist/TATimesheet-Test.exe src/*.cs
echo "built dist/TATimesheet-Test.exe"

if [ "${1:-}" = "test" ]; then
  out="${3:-build/test-output}"
  mkdir -p build
  csc $FLAGS -target:exe $REFS $RES -out:build/SmokeTest.exe $(ls src/*.cs | grep -v -E 'Form|Program') tests/SmokeTest.cs
  mono build/SmokeTest.exe "$2" "$out"
fi
