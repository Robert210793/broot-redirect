param(
    [Parameter(Mandatory = $true)]
    [string]$AppHost,

    [string]$OutputPath = (Join-Path (Get-Location) "broot-redirect-teams.zip")
)

$ErrorActionPreference = "Stop"

$appHostName = $AppHost -replace '^https?://', '' -replace '/.*$', ''
$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "broot-redirect-teams-$([guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

try
{
    $manifest = Get-Content -Path (Join-Path $PSScriptRoot "manifest.template.json") -Raw -Encoding UTF8
    $manifest = $manifest.Replace("{{APP_HOST}}", $appHostName)

    [System.IO.File]::WriteAllText((Join-Path $stagingDirectory "manifest.json"), $manifest, [System.Text.UTF8Encoding]::new($false))

    Copy-Item -Path (Join-Path $PSScriptRoot "color.png") -Destination $stagingDirectory
    Copy-Item -Path (Join-Path $PSScriptRoot "outline.png") -Destination $stagingDirectory

    Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $OutputPath -Force

    Write-Host "Teams app package for '$appHostName' written to $OutputPath"
}
finally
{
    Remove-Item -Path $stagingDirectory -Recurse -Force
}
