# Builds dist\MimeWalk.exe: runs the tests, then packs mimewalk.py into one exe that opens a console window.
# Run: powershell -ExecutionPolicy Bypass -File app\build_exe.ps1
$ErrorActionPreference = "Stop"
$app = $PSScriptRoot
# A private copy of Python in app\.venv, so nothing is installed for the whole computer.
$python = Join-Path $app ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) { python -m venv (Join-Path $app ".venv") }
& $python -m pip install --quiet python-osc pytest pyinstaller
& $python -m pytest -q $app
if ($LASTEXITCODE -ne 0) { throw "Tests failed; not building." }
& $python -m PyInstaller --noconfirm --clean --onefile --console --name MimeWalk `
    --distpath (Join-Path $app "..\dist") --workpath (Join-Path $app "build") --specpath (Join-Path $app "build") `
    (Join-Path $app "mimewalk.py")
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed." }
