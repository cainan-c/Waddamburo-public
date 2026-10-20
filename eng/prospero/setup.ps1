#requires -Version 7.0
param([switch]$InstallDotNet, [switch]$InstallLinux, [string]$WslDistribution = 'WaddamburoProspero')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$deps = Join-Path $repo '.deps/prospero'
New-Item -ItemType Directory -Force $deps | Out-Null
$pins = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
foreach ($entry in $pins.PSObject.Properties) {
    $destination = Join-Path $deps $entry.Name
    $pin = $entry.Value
    if (-not (Test-Path $destination)) {
        & git clone --config core.autocrlf=false --no-checkout $pin.url $destination
        if ($LASTEXITCODE -ne 0) { throw "Could not clone $($entry.Name)" }
        & git -C $destination checkout --detach $pin.revision
        if ($LASTEXITCODE -ne 0) { throw "Could not select pinned $($entry.Name) revision" }
    }
    $actual = & git -C $destination rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actual -ne $pin.revision) {
        throw "$destination must be at $($pin.revision). Existing files were left in place."
    }
}
if ($InstallDotNet) {
    $version = (Get-Content (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($IsWindows) {
        $installer = Join-Path $deps 'dotnet-install.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Version $version -InstallDir (Join-Path $deps 'dotnet') -NoPath
    } else {
        $installer = Join-Path $deps 'dotnet-install.sh'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.sh' -OutFile $installer
        & bash $installer --version $version --install-dir (Join-Path $deps 'dotnet') --no-path
        if ($LASTEXITCODE -ne 0) { throw '.NET SDK installation failed' }
    }
}
if ($InstallLinux) {
    if (-not $IsWindows) { throw '-InstallLinux is for a Windows WSL build host.' }
    $registered = ((& wsl.exe --list --quiet) -join "`n").Replace("`0", '').Split("`n").Trim()
    if ($registered -notcontains $WslDistribution) {
        & wsl.exe --install -d Debian --name $WslDistribution --location (Join-Path $deps 'wsl') --no-launch
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the isolated Linux build host.' }
    }
    $windowsScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'setup-linux.sh'))
    $linuxScript = '/mnt/' + $windowsScript.Substring(0, 1).ToLowerInvariant() + $windowsScript.Substring(2).Replace('\', '/')
    & wsl.exe --distribution $WslDistribution --user root -- bash $linuxScript
    if ($LASTEXITCODE -ne 0) { throw 'Linux compiler setup failed.' }
}
Write-Host "Dependencies ready in $deps"
Write-Host 'Use build.ps1 -ManagedOnly for host checks, or build.ps1 for the native folder.'
