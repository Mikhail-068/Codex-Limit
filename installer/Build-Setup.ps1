param([string] $MakeNsis)

$ErrorActionPreference = 'Stop'
$installerDir = $PSScriptRoot
$root = Split-Path -Parent $installerDir
$project = Join-Path $root 'src\CodexGauge\CodexGauge.csproj'

if ($MakeNsis) {
    $compiler = (Get-Command $MakeNsis -CommandType Application -ErrorAction Stop).Source
} else {
    $compiler = (Get-Command makensis.exe -CommandType Application -ErrorAction SilentlyContinue).Source
    if (-not $compiler) {
        $compiler = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
    }
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'MakeNSIS was not found. Install NSIS 3 or pass -MakeNsis with its executable path.'
}
$nsisLicense = Join-Path (Split-Path -Parent $compiler) 'COPYING'
if (-not (Test-Path -LiteralPath $nsisLicense -PathType Leaf)) {
    throw 'The NSIS installation must include its COPYING file.'
}

$dist = (New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force).FullName
$work = (New-Item -ItemType Directory -Path (Join-Path $dist ('setup-' + [guid]::NewGuid().ToString('N')))).FullName
$setupName = 'CodexGauge-Setup-x64.exe'
$setup = Join-Path $dist $setupName
try {
    $payload = Join-Path $work 'payload'
    $dotnetArtifacts = Join-Path $work 'dotnet'
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $payload --artifacts-path $dotnetArtifacts @(
        '-p:PublishSingleFile=true'
        '-p:IncludeNativeLibrariesForSelfExtract=true'
        '-p:DebugType=none'
    )
    if ($LASTEXITCODE -ne 0) { throw 'The self-contained publish failed.' }

    $exe = Get-Item -LiteralPath (Join-Path $payload 'CodexGauge.exe')
    $version = [version]$exe.VersionInfo.FileVersion
    $licenses = (New-Item -ItemType Directory -Path (Join-Path $work 'licenses')).FullName
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $licenses 'LICENSE.txt')
    Copy-Item -LiteralPath $nsisLicense -Destination (Join-Path $licenses 'nsis-license.txt')

    $assetsPath = Join-Path $dotnetArtifacts 'obj\CodexGauge\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packages = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
    $packs = @(
        @{ Id = 'Microsoft.NETCore.App.Runtime.win-x64'; Files = @{
            'LICENSE.TXT' = 'dotnet-runtime-license.txt'
            'THIRD-PARTY-NOTICES.TXT' = 'dotnet-third-party-notices.txt'
        } }
        @{ Id = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; Files = @{
            'LICENSE' = 'dotnet-desktop-license.txt'
        } }
    )
    foreach ($pack in $packs) {
        $dependency = $packages | Where-Object name -eq $pack.Id | Select-Object -First 1
        if (-not $dependency) { throw "Runtime pack resolution is missing: $($pack.Id)" }
        $runtimeVersion = $dependency.version.TrimStart('[').Split(',')[0].Trim()
        $relative = Join-Path $pack.Id.ToLowerInvariant() $runtimeVersion
        $packPath = $assets.packageFolders.PSObject.Properties.Name |
            ForEach-Object { Join-Path $_ $relative } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
            Select-Object -First 1
        if (-not $packPath) { throw "Runtime pack is missing from the NuGet cache: $relative" }
        foreach ($file in $pack.Files.GetEnumerator()) {
            Copy-Item -LiteralPath (Join-Path $packPath $file.Key) -Destination (Join-Path $licenses $file.Value)
        }
    }

    $candidate = Join-Path $work $setupName
    $arguments = @(
        '/WX'
        "/DOUTPUT_FILE=$candidate"
        "/DPAYLOAD_DIR=$payload"
        "/DLICENSE_DIR=$licenses"
        "/DAPP_VERSION=$($version.ToString(3))"
        "/DPRODUCT_VERSION=$version"
        (Join-Path $installerDir 'CodexGauge.nsi')
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'MakeNSIS failed to produce the installer.'
    }
    if (Test-Path -LiteralPath $setup) {
        [IO.File]::Replace($candidate, $setup, (Join-Path $work 'previous-setup.exe'))
    } else {
        [IO.File]::Move($candidate, $setup)
    }
    Get-FileHash -LiteralPath $setup -Algorithm SHA256
} finally {
    $resolvedWork = (Resolve-Path -LiteralPath $work).ProviderPath
    $resolvedDist = (Resolve-Path -LiteralPath $dist).ProviderPath.TrimEnd('\') + '\'
    if ($resolvedWork.StartsWith($resolvedDist, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
