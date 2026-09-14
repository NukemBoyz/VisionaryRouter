[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\VisionaryRouter-$Runtime"))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "VisionaryRouter-$Runtime.zip"))
$expectedPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

foreach ($target in @($artifactsRoot, $publishDirectory, $zipPath)) {
    if (-not $target.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside the repository: $target"
    }
}

Push-Location $repositoryRoot
try {
    dotnet test VisionaryRouter.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; release package was not created.' }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

    dotnet publish src/VisionaryRouter.Server/VisionaryRouter.Server.csproj `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        -p:PublishProfile=Windows-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

    $zip = Get-Item -LiteralPath $zipPath
    Write-Host "Created $($zip.FullName) ($([math]::Round($zip.Length / 1MB, 1)) MB)"
}
finally {
    Pop-Location
}
