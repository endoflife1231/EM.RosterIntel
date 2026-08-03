[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "src\EM.RosterIntel\EM.RosterIntel.csproj"
$artifactRoot = Join-Path $repositoryRoot "artifacts"
$stageRoot = Join-Path $artifactRoot "stage"
$pluginFolder = Join-Path $stageRoot "EM.RosterIntel"
$zipPath = Join-Path $artifactRoot "EM.RosterIntel-v$Version.zip"

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

& dotnet build $project `
    --configuration Release `
    -p:GameDir="$GameDir" `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$dllPath = Join-Path $repositoryRoot "src\EM.RosterIntel\bin\Release\net6.0\EM.RosterIntel.dll"
if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "Built DLL was not found: $dllPath"
}

Copy-Item $dllPath (Join-Path $pluginFolder "EM.RosterIntel.dll")
Copy-Item (Join-Path $repositoryRoot "docs\RELEASE_README.md") (Join-Path $pluginFolder "README.md")
Copy-Item (Join-Path $repositoryRoot "docs\RELEASE_README-RU.md") (Join-Path $pluginFolder "README-RU.md")
Copy-Item (Join-Path $repositoryRoot "LICENSE") (Join-Path $pluginFolder "LICENSE.txt")

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path $pluginFolder -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force

Write-Host "Created $zipPath"
