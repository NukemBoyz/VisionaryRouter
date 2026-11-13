param([string]$Output = "$PSScriptRoot\..\artifacts\desktop")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& dotnet publish "$root\src\VisionaryRouter.Desktop" -c Release -r win-x64 --self-contained true -o $Output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
& dotnet publish "$root\src\VisionaryRouter.Server" -c Release -r win-x64 --self-contained true -p:PublishProfile=Windows-x64 -o "$Output\server"
if ($LASTEXITCODE -ne 0) { throw 'Engine publish failed' }
$configFile = Join-Path $Output 'server\appsettings.json'
$config = Get-Content $configFile -Raw | ConvertFrom-Json
$config.Router.OpenBrowserOnStart = $false
$config.Router | Add-Member -NotePropertyName DesktopOnly -NotePropertyValue $true -Force
$config | ConvertTo-Json -Depth 8 | Set-Content $configFile
