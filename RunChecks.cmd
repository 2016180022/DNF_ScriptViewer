@echo off
rem DNF_ScriptViewer check: build, data validation, runtime test list (TestInputs/tests.txt)
rem Double-click to run. The report opens in Notepad (tools\DNF_ScriptViewer.Check\out\report.txt, not committed)
cd /d "%~dp0"
set "OUT=tools\DNF_ScriptViewer.Check\out"
set "REPORT=%OUT%\report.txt"
if not exist "%OUT%" mkdir "%OUT%"

echo [1/2] Build...
dotnet build DNF_ScriptViewer.slnx -v q -nologo > "%REPORT%" 2>&1
if errorlevel 1 (
  echo Build failed. Opening report...
  start "" notepad "%REPORT%"
  exit /b 1
)

echo [2/2] Validation and runtime tests...
dotnet run --project tools\DNF_ScriptViewer.Check --no-build -- --tests > "%REPORT%" 2>&1
start "" notepad "%REPORT%"
