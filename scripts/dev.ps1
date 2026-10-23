param([string]$Dotnet = 'dotnet', [switch]$Run)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    $arguments = @('build', 'Koe-Maui/Koe.csproj', '-f', 'net10.0-windows10.0.19041.0')
    if ($Run) { $arguments += '-t:Run' }
    & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }
