# Puts the exe, the Unity package and the user guides into dist\MimeWalk-<version>.zip.
# Build both first: app\build_exe.ps1 and tools\build_unitypackage.ps1.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
# The version number comes from app\pyproject.toml.
$project = Get-Content (Join-Path $root "app\pyproject.toml") -Raw
if ($project -notmatch 'version = "([^"]+)"') { throw "Could not read the version." }
$version = $Matches[1]

$files = @(
    (Join-Path $dist "MimeWalk.exe"),
    (Join-Path $dist "MimeWalk.unitypackage"),
    (Join-Path $root "docs\USER_GUIDE.md"),
    (Join-Path $root "docs\CHOREOGRAPHY.md")
)
$missing = $files | Where-Object { -not (Test-Path $_) }
if ($missing) { throw "Missing: $($missing -join ', ')" }

$stage = Join-Path $dist "release\MimeWalk-$version"
Remove-Item -Recurse -Force (Join-Path $dist "release") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $files $stage
@"
MimeWalk $version

1. Add MimeWalk.unitypackage to your avatar (needs Modular Avatar or VRCFury, and Poiyomi Toon 10+).
2. Turn on OSC in VRChat: Action Menu > Options > OSC.
3. Run MimeWalk.exe, then use Action Menu > Expressions > MimeWalk > Walk.

Settings are in config.toml, created next to MimeWalk.exe on first run.
Full instructions: USER_GUIDE.md
"@ | Set-Content -Encoding utf8 (Join-Path $stage "README.txt")

$zip = Join-Path $dist "MimeWalk-$version.zip"
Remove-Item -Force $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $stage -DestinationPath $zip
Remove-Item -Recurse -Force (Join-Path $dist "release")
Write-Host "Created $zip"
