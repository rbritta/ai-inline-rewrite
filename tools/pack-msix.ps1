<#
.SYNOPSIS
  Packs the published app into an MSIX for the Microsoft Store (or, signed with a test certificate, for a local test).

.DESCRIPTION
  1. dotnet publish (the win-x64 profile: a self-contained folder), unless -NoPublish.
  2. A layout folder: the published files, packaging/Assets, and packaging/AppxManifest.xml with its $...$ values.
  3. resources.pri (makepri), so Windows picks the right icon size, then the .msix (makeappx).
  4. With -CertificatePath, signs it (signtool) for installing on a PC; the Store signs its own copy, so the
     package uploaded to Partner Center is left unsigned.
  makeappx, makepri and signtool come from the Windows SDK when installed, else from the
  Microsoft.Windows.SDK.BuildTools NuGet package, fetched once into artifacts/tools (no admin rights needed).

.EXAMPLE
  ./tools/pack-msix.ps1 -IdentityName 12345rbritta.AIInlineRewrite -Publisher "CN=..." -PublisherDisplayName rbritta
#>
param(
    # Partner Center > the app > Product identity: Package/Identity/Name and Package/Identity/Publisher.
    [string]$IdentityName = 'rbritta.AIInlineRewrite.Test',
    [string]$Publisher = 'CN=AIR Test',
    [string]$PublisherDisplayName = 'rbritta',
    # Four parts, the last one 0 (the Store requires it); by default the csproj's <Version> plus ".0".
    [string]$Version,
    [string]$OutFile,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdkBuildTools = '10.0.28000.2957'

if (-not $Version) {
    $csproj = [xml](Get-Content -Raw -Encoding UTF8 (Join-Path $root 'src/AiInlineRewrite/AiInlineRewrite.csproj'))
    $Version = @($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0].Trim() + '.0'
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must be four numbers ending in .0, not '$Version'" }
if (-not $OutFile) { $OutFile = Join-Path $root "artifacts/AiInlineRewrite-v$($Version -replace '\.0$','')-x64.msix" }

function Find-Tool([string]$name) {
    $kit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($kit) { return $kit.FullName }
    $dir = Join-Path $root "artifacts/tools/sdk-buildtools-$sdkBuildTools"
    if (-not (Test-Path $dir)) {
        $nupkg = "$dir.nupkg"
        New-Item -ItemType Directory -Force (Split-Path $dir) | Out-Null
        Invoke-WebRequest "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/$sdkBuildTools" -OutFile $nupkg
        Expand-Archive $nupkg -DestinationPath $dir
        Remove-Item $nupkg
    }
    $tool = Get-ChildItem "$dir/bin/*/x64/$name" | Select-Object -First 1
    if (-not $tool) { throw "$name not found in the Windows SDK or in Microsoft.Windows.SDK.BuildTools $sdkBuildTools" }
    return $tool.FullName
}

$publish = Join-Path $root 'artifacts/publish/win-x64'
if (-not $NoPublish) {
    # from the repository root, so a global.json in the caller's folder cannot pick another SDK
    Push-Location $root
    try {
        dotnet publish (Join-Path $root 'src/AiInlineRewrite/AiInlineRewrite.csproj') -p:PublishProfile=win-x64 --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    }
    finally { Pop-Location }
}
if (-not (Test-Path (Join-Path $publish 'AiInlineRewrite.exe'))) { throw "No published app in $publish" }

$layout = Join-Path $root 'artifacts/msix/layout'
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item -Recurse "$publish/*" $layout
Copy-Item -Recurse (Join-Path $root 'packaging/Assets') (Join-Path $layout 'Assets')
$manifest = (Get-Content -Raw -Encoding UTF8 (Join-Path $root 'packaging/AppxManifest.xml')).
    Replace('$IdentityName$', $IdentityName).
    Replace('$Publisher$', [Security.SecurityElement]::Escape($Publisher)).
    Replace('$PublisherDisplayName$', [Security.SecurityElement]::Escape($PublisherDisplayName)).
    Replace('$Version$', $Version)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))

$makepri = Find-Tool 'makepri.exe'
$priConfig = Join-Path $root 'artifacts/msix/priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed' }
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri new failed' }

$makeappx = Find-Tool 'makeappx.exe'
& $makeappx pack /d $layout /p $OutFile /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed' }

if ($CertificatePath) {
    $signtool = Find-Tool 'signtool.exe'
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
    if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
    & $signtool @signArgs $OutFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'signtool sign failed' }
}

$size = (Get-Item $OutFile).Length / 1MB
"{0} ({1:0} MB), identity {2}, version {3}{4}" -f $OutFile, $size, $IdentityName, $Version, ($(if ($CertificatePath) { ', signed' } else { ', unsigned' }))
