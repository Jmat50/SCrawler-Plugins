param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host $Description
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

if ($Clean) {
    Remove-Item -LiteralPath '.\build' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath '.\dist' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath '.\SCrawler-Plugin-Installer.spec' -Force -ErrorAction SilentlyContinue
}

Invoke-Step "Upgrading pip..." { python -m pip install --upgrade pip }
Invoke-Step "Installing build requirements..." { python -m pip install -r .\requirements-build.txt }

Invoke-Step "Building EXE with PyInstaller..." {
    python -m PyInstaller `
        --noconfirm `
        --clean `
        --onefile `
        --windowed `
        --name "SCrawler-Plugin-Installer" `
        --collect-all customtkinter `
        --add-data "..\releases;releases" `
        .\installer.py
}

Write-Host "Built EXE: $PSScriptRoot\dist\SCrawler-Plugin-Installer.exe"
