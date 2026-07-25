[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot "EM.RosterIntel.sln"
$deployValue = if ($Deploy) { "true" } else { "false" }

if (-not (Test-Path -LiteralPath $GameDir)) {
    throw "Game directory does not exist: $GameDir"
}

& dotnet build $solution `
    --configuration $Configuration `
    -p:GameDir="$GameDir" `
    -p:DeployToGame=$deployValue

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}
