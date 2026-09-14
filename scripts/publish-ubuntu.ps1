[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$expectedPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$runtimes = @('linux-x64', 'linux-arm64')

if (-not $artifactsRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $artifactsRoot"
}

Push-Location $repositoryRoot
try {
    dotnet test VisionaryRouter.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; Ubuntu packages were not created.' }

    foreach ($runtime in $runtimes) {
        $stageDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\VisionaryRouter-ubuntu-$runtime"))
        $appDirectory = [IO.Path]::GetFullPath((Join-Path $stageDirectory 'app'))
        $archivePath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "VisionaryRouter-ubuntu-$runtime.tar.gz"))

        foreach ($target in @($stageDirectory, $appDirectory, $archivePath)) {
            if (-not $target.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to write outside the repository: $target"
            }
        }

        if (Test-Path -LiteralPath $stageDirectory) {
            Remove-Item -LiteralPath $stageDirectory -Recurse -Force
        }
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }
        New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null

        dotnet publish src/VisionaryRouter.Server/VisionaryRouter.Server.csproj `
            --configuration Release `
            --runtime $runtime `
            --self-contained true `
            --output $appDirectory `
            -p:PublishProfile=Ubuntu
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

        $windowsReadme = Join-Path $appDirectory 'README-FIRST.txt'
        if (Test-Path -LiteralPath $windowsReadme) {
            Remove-Item -LiteralPath $windowsReadme -Force
        }

        Copy-Item -LiteralPath 'packaging/ubuntu/install.sh' -Destination $stageDirectory
        Copy-Item -LiteralPath 'packaging/ubuntu/uninstall.sh' -Destination $stageDirectory
        Copy-Item -LiteralPath 'packaging/ubuntu/visionary-router.service' -Destination $stageDirectory
        Copy-Item -LiteralPath 'packaging/ubuntu/README-FIRST.txt' -Destination $stageDirectory
        Set-Content -LiteralPath (Join-Path $stageDirectory 'PACKAGE-RUNTIME') -Value $runtime -NoNewline

        tar -czf $archivePath -C $stageDirectory .
        if ($LASTEXITCODE -ne 0) { throw "Archive creation failed for $runtime." }

        $archive = Get-Item -LiteralPath $archivePath
        Write-Host "Created $($archive.FullName) ($([math]::Round($archive.Length / 1MB, 1)) MB)"
    }
}
finally {
    Pop-Location
}
