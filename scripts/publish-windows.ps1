[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = '1.0.0',

    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\VisionaryRouter-$Runtime"))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "VisionaryRouter-$Runtime.zip"))
$installerScript = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'packaging\windows\VisionaryRouter.iss'))
$installerPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "VisionaryRouter-Setup-$Runtime.exe"))
$installerReadmePath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'VisionaryRouter-Installer-README.txt'))
$expectedPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

foreach ($target in @($artifactsRoot, $publishDirectory, $zipPath, $installerPath, $installerReadmePath)) {
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
    if (Test-Path -LiteralPath $installerPath) {
        Remove-Item -LiteralPath $installerPath -Force
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

    if (-not $SkipInstaller) {
        $innoCompilerCandidates = @(
            (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

        $innoCompiler = $innoCompilerCandidates | Select-Object -First 1
        if (-not $innoCompiler) {
            throw 'Inno Setup 6 is required to build the Windows installer. Install it or use -SkipInstaller.'
        }

        & $innoCompiler `
            "/DAppVersion=$Version" `
            "/DSourceDir=$publishDirectory" `
            "/DOutputDir=$artifactsRoot" `
            $installerScript
        if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }

        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\windows\README-FIRST.txt') `
            -Destination $installerReadmePath -Force

        $installer = Get-Item -LiteralPath $installerPath
        Write-Host "Created $($installer.FullName) ($([math]::Round($installer.Length / 1MB, 1)) MB)"
    }
}
finally {
    Pop-Location
}
