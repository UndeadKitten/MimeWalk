<#
.SYNOPSIS
    Builds dist\MimeWalk.unitypackage from unity\MimeWalkDev. Unity runs without opening a window (about a minute).
    -ReimportTest  then imports the package into a fresh copy of the project (%TEMP%\MimeWalkReimportTest),
                   and checks that it has no code and that both prefabs load and work.
    -Unity         path to Unity.exe 2022.3.22f1, if it isn't in Unity Hub's default folder.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build_unitypackage.ps1 -ReimportTest
#>
param([string]$Unity = "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe", [switch]$ReimportTest)
$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Repo "unity\MimeWalkDev"
$Package = Join-Path $Repo "dist\MimeWalk.unitypackage"
$Logs = Join-Path $Repo "unity\Logs"
New-Item -ItemType Directory -Force $Logs | Out-Null

# Runs Unity without a window, shows MimeWalk's log lines, and stops on code errors or a failed build.
function Invoke-Unity([string]$ProjectPath, [string]$Log, [string[]]$Extra) {
    $unityArgs = @("-batchmode", "-nographics", "-quit", "-projectPath", "`"$ProjectPath`"", "-logFile", "`"$Log`"") + $Extra
    $exit = (Start-Process $Unity -ArgumentList $unityArgs -NoNewWindow -Wait -PassThru).ExitCode
    $lines = Get-Content $Log
    $lines | Select-String -SimpleMatch "[MimeWalk]" | ForEach-Object { Write-Host "  $($_.Line)" }
    $compiler = $lines | Select-String -Pattern "(error|warning) CS\d+" | Where-Object { $_.Line -match "Assets[/\\]MimeWalk|error CS" }
    if ($compiler) { $compiler.Line | Select-Object -Unique | Write-Host -ForegroundColor Red; throw "Compiler errors or warnings. Log: $Log" }
    if ($exit -ne 0) { throw "Unity exited with code $exit (is the project open in Unity?). Log: $Log" }
}

Invoke-Unity $Project (Join-Path $Logs "build.log") @("-executeMethod", "MimeWalkBuild.BuildPackage.Run")
Write-Host "Built $Package ($((Get-Item $Package).Length) bytes)" -ForegroundColor Green

if ($ReimportTest) {
    $Scratch = Join-Path $env:TEMP "MimeWalkReimportTest"
    if (Test-Path $Scratch) { Remove-Item -Recurse -Force $Scratch }
    # Copy Library so the import is fast, but not its compiled code, so everything compiles fresh.
    foreach ($dir in "Packages", "ProjectSettings", "Library") {
        robocopy (Join-Path $Project $dir) (Join-Path $Scratch $dir) /E /XD Bee ScriptAssemblies /NFL /NDL /NJH /NJS /NP | Out-Null
    }
    New-Item -ItemType Directory -Force (Join-Path $Scratch "Assets") | Out-Null
    Invoke-Unity $Scratch (Join-Path $Logs "reimport.log") @("-importPackage", "`"$Package`"")
    $scripts = Get-ChildItem -Recurse (Join-Path $Scratch "Assets") -Include *.cs, *.asmdef, *.dll
    if ($scripts) { throw "The package contains scripts: $($scripts.FullName -join ', ')" }
    # The checks live in the build script, so copy it in after the import.
    Copy-Item -Recurse (Join-Path $Project "Assets\MimeWalkBuild") (Join-Path $Scratch "Assets\MimeWalkBuild")
    Invoke-Unity $Scratch (Join-Path $Logs "reimport-check.log") @("-executeMethod", "MimeWalkBuild.BuildPackage.CheckImport")
    Write-Host "Re-import test passed: no scripts, both prefabs load." -ForegroundColor Green
}
exit 0 # report success; robocopy's "files copied" code (1) would otherwise look like a failure
