<#
.SYNOPSIS
    Checks a StatePipes service solution: build, tests, no blocking calls in state machine code, formatting.
.DESCRIPTION
    Prints one "FAIL <check>: <detail>" line per failed check and exits 1 if any check failed, otherwise prints
    "PASS" and exits 0. Used by the statepipes-verify skill.
.PARAMETER SolutionDir
    Directory containing the service solution (.slnx) and Tests.runsettings.
.PARAMETER SkipFormat
    Skip the dotnet format check.
.PARAMETER SkipBuildAndTest
    Skip dotnet build and dotnet test (static checks only).
#>
param(
    [Parameter(Mandatory = $true)][string]$SolutionDir,
    [switch]$SkipFormat,
    [switch]$SkipBuildAndTest
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $SolutionDir -PathType Container)) {
    Write-Output "FAIL solution: directory not found: $SolutionDir"
    exit 1
}
$solution = Get-ChildItem -LiteralPath $SolutionDir -Filter *.slnx -File | Select-Object -First 1
if ($null -eq $solution) {
    Write-Output "FAIL solution: no .slnx in $SolutionDir"
    exit 1
}

if (-not $SkipBuildAndTest) {
    $buildOutput = & dotnet build $solution.FullName 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("FAIL build: dotnet build exited $LASTEXITCODE; first error: " + (($buildOutput | Select-String ': error ' | Select-Object -First 1) -as [string]))
    }
    else {
        $settings = Join-Path $SolutionDir 'Tests.runsettings'
        $testArgs = @('test', $solution.FullName, '--no-build')
        if (Test-Path -LiteralPath $settings) { $testArgs += @('--settings', $settings) }
        $testOutput = & dotnet @testArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            $failures.Add("FAIL tests: dotnet test exited $LASTEXITCODE; " + (($testOutput | Select-String 'Failed!|error' | Select-Object -First 1) -as [string]))
        }
    }
}

# Rule: never block on async work inside state machine code.
$stateMachineFiles = Get-ChildItem -LiteralPath $SolutionDir -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -match '[\\/]StateMachines[\\/]' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
foreach ($match in ($stateMachineFiles | Select-String -Pattern '\.Result\b|\.Wait\(\)')) {
    $relative = [System.IO.Path]::GetRelativePath($SolutionDir, $match.Path)
    $failures.Add("FAIL no-blocking-calls: ${relative}:$($match.LineNumber): $($match.Line.Trim())")
}

if (-not $SkipFormat) {
    & dotnet format $solution.FullName --verify-no-changes 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("FAIL format: dotnet format --verify-no-changes exited $LASTEXITCODE; run 'dotnet format $($solution.Name)'")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output $_ }
    exit 1
}
Write-Output 'PASS'
exit 0
