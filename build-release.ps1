param(
    [string[]]$Projects = @(
        "plugins\Coomer\SCrawler.Plugin.Coomer\SCrawler.Plugin.Coomer.vbproj",
        "plugins\YouPorn\SCrawler.Plugin.YouPorn\SCrawler.Plugin.YouPorn.vbproj",
        "plugins\XNXX\SCrawler.Plugin.XNXX\SCrawler.Plugin.XNXX.vbproj",
        "plugins\Motherless\SCrawler.Plugin.Motherless\SCrawler.Plugin.Motherless.vbproj",
        "plugins\EFUKT\SCrawler.Plugin.EFUKT\SCrawler.Plugin.EFUKT.vbproj",
        "plugins\RedTube\SCrawler.Plugin.RedTube\SCrawler.Plugin.RedTube.vbproj",
        "plugins\Imgur\SCrawler.Plugin.Imgur\SCrawler.Plugin.Imgur.vbproj",
        "plugins\Mastodon\SCrawler.Plugin.Mastodon\SCrawler.Plugin.Mastodon.vbproj",
        "plugins\VK\SCrawler.Plugin.VK\SCrawler.Plugin.VK.vbproj",
        "plugins\Tumblr\SCrawler.Plugin.Tumblr\SCrawler.Plugin.Tumblr.vbproj",
        "plugins\DeviantArt\SCrawler.Plugin.DeviantArt\SCrawler.Plugin.DeviantArt.vbproj"
    ),
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Resolve-MSBuild {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools or Visual Studio with MSBuild.'
}

$msbuild = Resolve-MSBuild
Write-Host "Using MSBuild: $msbuild"

function Invoke-MSBuildStep {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Target
    )

    & $msbuild $ProjectPath "/t:$Target" /p:Configuration=Release /m /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild target '$Target' failed for project: $ProjectPath"
    }
}

$releaseRoot = Join-Path $root 'releases'
if (-not (Test-Path $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

foreach ($project in $Projects) {
    $projectPath = Join-Path $root $project
    if (-not (Test-Path $projectPath)) {
        throw "Project not found: $project"
    }

    $projectDir = Split-Path -Parent $projectPath

    if ($Clean) {
        Remove-Item -LiteralPath (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $dllPath = Join-Path $projectDir ("bin\\Release\\$projectName.dll")
    if (Test-Path $dllPath) {
        Remove-Item -LiteralPath $dllPath -Force -ErrorAction Stop
    }

    Invoke-MSBuildStep -ProjectPath $projectPath -Target 'Restore'
    Invoke-MSBuildStep -ProjectPath $projectPath -Target 'Build'

    if (-not (Test-Path $dllPath)) {
        throw "Expected build output was not found: $dllPath"
    }

    $normalizedProject = ($project -replace '/', '\')
    $parts = $normalizedProject.Split('\') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($parts.Length -ge 2 -and $parts[0].Equals('plugins', [System.StringComparison]::OrdinalIgnoreCase)) {
        $siteFolder = $parts[1]
    } else {
        $siteFolder = Split-Path $project -Parent | Split-Path -Parent
        if ([string]::IsNullOrWhiteSpace($siteFolder)) {
            $siteFolder = $projectName
        }
    }

    $releaseSiteDir = Join-Path $releaseRoot $siteFolder
    New-Item -ItemType Directory -Path $releaseSiteDir -Force | Out-Null

    $releaseDll = Join-Path $releaseSiteDir ([System.IO.Path]::GetFileName($dllPath))
    Copy-Item -LiteralPath $dllPath -Destination $releaseDll -Force

    Write-Host "Built: $dllPath"
    Write-Host "Packaged: $releaseDll"
}
