#requires -Version 7.0
param([switch]$ManagedOnly, [ValidateSet('Folder','Package')][string]$Output = 'Folder',
      [string]$WslDistribution = 'WaddamburoProspero')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$savedModules = $env:PROSPERO_MODULES
$sdk = if ($env:SHARPPROSPERO_ROOT) { $env:SHARPPROSPERO_ROOT } else { Join-Path $repo '.deps/prospero/SharpProspero' }
$localDotNet = Join-Path $repo '.deps/prospero/dotnet'
if (Test-Path $localDotNet) {
    $env:DOTNET_ROOT = $localDotNet
    $env:PATH = $localDotNet + [IO.Path]::PathSeparator + $env:PATH
}
function Assert-ProsperoDisplayMetadata([string]$Path) {
    $metadata = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    # 0x40 declares high-frame-rate support; keep the existing 0x40000 profile,
    # without the alternate 0x80000 VRR-required declaration.
    if (($metadata.attribute3 -band 0xC0040) -ne 0x40040) {
        throw "PS5 high-refresh metadata is missing or changed in $Path; expected attribute3 mask 0x40040."
    }
}
Assert-ProsperoDisplayMetadata (Join-Path $repo 'src/Waddamburo.App.Prospero/sce_sys/param.json')
Push-Location $repo
try {
    $version = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $version -ne '10.0.201') { throw 'Install .NET SDK 10.0.201 or run eng/prospero/setup.ps1 -InstallDotNet.' }
    if (-not (Test-Path (Join-Path $sdk 'build/Prospero.App.targets'))) { throw 'Run eng/prospero/setup.ps1 first.' }
    & dotnet run --project tests/Waddamburo.Prospero.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Prospero host checks failed.' }
    $project = Join-Path $repo 'src/Waddamburo.App.Prospero/Waddamburo.App.Prospero.csproj'
    & dotnet build $project -c Release "-p:SharpProsperoRoot=$sdk"
    if ($LASTEXITCODE -ne 0) { throw 'Prospero managed build failed.' }
    if ($ManagedOnly) { Write-Host 'Managed checks passed; no PS5 executable was produced.'; return }
    if ($IsWindows) {
        # The SDK calls wsl.exe without a distro. A scoped wrapper selects this build's
        # distro for those calls without changing the user's system-wide WSL default.
        $prosperoWslExecutable = (Get-Command wsl.exe -CommandType Application | Select-Object -First 1).Source
        function wsl.exe { & $prosperoWslExecutable --distribution $WslDistribution @args }
        # Diagnose WSL before invoking the lengthy NativeAOT/link/package pipeline.
        & wsl.exe -e bash -lc 'dotnet --version'
        if ($LASTEXITCODE -ne 0) { throw "Linux compile host $WslDistribution cannot start or has no .NET SDK. The managed build remains available." }
    }
    if (-not $env:PROSPERO_MODULES) {
        $vulkan = Join-Path $repo '.deps/prospero/PS5_Vulkan'
        $runtime = Join-Path $vulkan 'runtime/libc.prx'
        if (-not (Test-Path $runtime)) {
            $generator = Join-Path $vulkan 'tools/rebuild-libc.sh'
            if ($IsWindows) {
                $generator = '/mnt/' + $generator.Substring(0, 1).ToLowerInvariant() + $generator.Substring(2).Replace('\', '/')
                & wsl.exe -e bash $generator
            } else { & bash $generator }
            if ($LASTEXITCODE -ne 0) { throw 'Source-generated runtime companion build failed.' }
        }
        $expected = 'e6ff45d16adf687855cc3b33b0c8a4132b6504360b221e0a34c7e99fb3ba0036'
        if ((Get-FileHash $runtime -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
            throw 'The generated runtime companion does not match the pinned source digest.'
        }
        $env:PROSPERO_MODULES = Split-Path -Parent $runtime
    }
    $nativeBuild = Join-Path $PSScriptRoot 'build-native.sh'
    if ($IsWindows) {
        $nativeBuild = '/mnt/' + $nativeBuild.Substring(0, 1).ToLowerInvariant() + $nativeBuild.Substring(2).Replace('\', '/')
        & wsl.exe -e bash $nativeBuild
    } else { & bash $nativeBuild }
    if ($LASTEXITCODE -ne 0) { throw 'The full PS5 native engine build failed.' }
    $gameOutput = Join-Path $repo 'out/prospero-game'
    & (Join-Path $sdk 'build/build-app.ps1') -SdkRoot $sdk -ProjectPath $project -Output $Output -OutputFolder $gameOutput
    if ($LASTEXITCODE -ne 0) { throw 'PS5 NativeAOT, link, or packaging failed.' }
    Assert-ProsperoDisplayMetadata (Join-Path $gameOutput 'module/sce_sys/param.json')
    $dataOutput = Join-Path $gameOutput 'module/Data'
    New-Item -ItemType Directory -Force $dataOutput | Out-Null
    Copy-Item (Join-Path $repo 'src/Waddamburo.App.Prospero/Data/README.txt') $dataOutput
} finally { $env:PROSPERO_MODULES = $savedModules; Pop-Location }
