@echo off
rem Builds dist\TATimesheet.exe with the C# compiler that ships inside Windows (.NET Framework 4.x).
rem No Visual Studio or downloads needed: just double-click this file.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.
  pause
  exit /b 1
)

if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /out:dist\TATimesheet.exe ^
  /win32icon:src\Resources\app.ico /win32manifest:src\app.manifest ^
  /resource:src\Resources\TimesheetTemplate.docx,TATimesheet.TimesheetTemplate.docx ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll ^
  src\*.cs
if errorlevel 1 goto failed

rem Practice copy: separate data, sample entries, orange TEST banner.
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /define:TEST_BUILD ^
  /out:dist\TATimesheet-Test.exe ^
  /win32icon:src\Resources\app-test.ico /win32manifest:src\app.manifest ^
  /resource:src\Resources\TimesheetTemplate.docx,TATimesheet.TimesheetTemplate.docx ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll ^
  src\*.cs
if errorlevel 1 goto failed

echo.
echo Built dist\TATimesheet.exe and dist\TATimesheet-Test.exe
pause
exit /b 0

:failed
echo.
echo BUILD FAILED
pause
exit /b 1
