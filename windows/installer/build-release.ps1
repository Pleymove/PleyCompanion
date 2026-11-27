#Requires -Version 7.4
<#
.SYNOPSIS
    Builds the self-contained Windows x64 app and packages it with Velopack.
.DESCRIPTION
    The installer and its SHA-256 are written to windows\Releases\win-x64.
    The version defaults to windows\Directory.Build.props. Run with PowerShell 7 on Windows x64.
#>
[CmdletBinding()]
param([string] $Version)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Run the packaging script with PowerShell 7 on Windows x64.'
}

$windowsDirectory = Split-Path -Parent $PSScriptRoot
$project = 'src/OpenClicky.Windows/OpenClicky.Windows.csproj'
$publishDirectory = Join-Path $windowsDirectory 'publish/win-x64'
$releaseDirectory = Join-Path $windowsDirectory 'Releases/win-x64'

Push-Location $windowsDirectory
try {
    if (-not $Version) {
        [xml] $buildProps = Get-Content -Raw Directory.Build.props
        $Version = $buildProps.Project.PropertyGroup.Version
    }
    if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[A-Za-z]+\.(0|[1-9][0-9]*))?\z') {
        throw 'Use a version in X.Y.Z or X.Y.Z-tag.N format.'
    }

    dotnet tool restore
    ./scripts/fetch-speech-model.ps1

    foreach ($directory in @($publishDirectory, $releaseDirectory)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    }

    Write-Host "Publishing PleyCompanion $Version (win-x64)"
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" -o $publishDirectory
    Copy-Item -LiteralPath ../LICENSE -Destination $publishDirectory

    dotnet tool run vpk -- --skip-updates pack --packId PleyCompanion.Windows --packVersion $Version `
        --packDir $publishDirectory --mainExe PleyCompanion.exe --packTitle PleyCompanion --packAuthors Pleymove `
        --icon 'src/OpenClicky.Windows/Assets/OpenClicky.ico' --runtime win-x64 --channel pleycompanion-win-x64 `
        --shortcuts 'Desktop,StartMenuRoot' -o $releaseDirectory

    $setup = @(Get-ChildItem -LiteralPath $releaseDirectory -File -Filter '*-Setup.exe')
    if ($setup.Count -ne 1) { throw "Expected one Velopack installer, found $($setup.Count)." }
    $installer = Join-Path $releaseDirectory 'PleyCompanion-Setup.exe'
    if ($setup[0].FullName -ne $installer) { Move-Item -LiteralPath $setup[0].FullName -Destination $installer }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  PleyCompanion-Setup.exe" | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Installer: $installer"
}
finally {
    Pop-Location
}
