#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the exact same checks CI runs, locally. Use this before every push.

.DESCRIPTION
    Mirrors the two GitHub Actions workflows so a green local run means a green CI:
      1. Core tests   -> dotnet test            (fast, no engine)   [core-tests.yml]
      2. Engine smoke -> headless Godot boot + Core-seam check       [engine-tests.yml]

    Exits non-zero if anything fails, so it works as a gate (e.g. in a git hook).

.PARAMETER Core
    Run only the fast Core unit tests; skip the slower engine smoke test.
    This is the tight inner loop you run constantly while writing Core logic.

.EXAMPLE
    ./tools/test.ps1            # everything — run this before pushing
    ./tools/test.ps1 -Core      # fast loop only (no Godot)
#>
[CmdletBinding()]
param(
    [switch]$Core
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot   # tools/ -> repo root

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Fail($msg)       { Write-Host "`nX  $msg" -ForegroundColor Red; exit 1 }

# ---- 1. Core unit tests (xUnit) -----------------------------------------
Write-Step "Core unit tests (dotnet test)"
dotnet test "$repo/tests/Core.Tests/Core.Tests.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { Fail "Core tests failed." }

if ($Core) {
    Write-Host "`nOK  Core tests passed. (Engine smoke skipped: -Core)" -ForegroundColor Green
    exit 0
}

# ---- 2. Engine smoke test (headless Godot) ------------------------------
Write-Step "Locating Godot"
$godot = $null
foreach ($candidate in @($env:GODOT, 'godotc', 'godot')) {
    if ($candidate -and (Get-Command $candidate -ErrorAction SilentlyContinue)) { $godot = $candidate; break }
}
if (-not $godot) {
    $fallback = "$env:USERPROFILE\bin\godotc.cmd"
    if (Test-Path $fallback) { $godot = $fallback }
}
if (-not $godot) {
    Fail "Could not find Godot. Open a fresh terminal (PATH), set `$env:GODOT, or put 'godotc' on PATH."
}
Write-Host "Using: $godot"

$game = "$repo/game"

Write-Step "Import project (headless)"
& $godot --headless --path $game --import   # benign warnings can set a nonzero code; don't gate on it

Write-Step "Build C# solution (headless)"
& $godot --headless --path $game --build-solutions --quit
if ($LASTEXITCODE -ne 0) { Fail "Godot C# build failed." }

Write-Step "Smoke run (boot main scene, verify Core seam)"
$output = & $godot --headless --path $game --quit-after 120 | Out-String
Write-Host $output
if ($output -match 'Scrapline Core online\.') {
    Write-Host "`nOK  All checks passed - safe to push." -ForegroundColor Green
    exit 0
}
Fail "Engine smoke failed: Core greeting not found (the Godot<->Core seam is broken)."
