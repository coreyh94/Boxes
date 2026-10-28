# Builds the release downloads into .\release:
#   Boxes-Setup-<version>.exe   installer (needs Inno Setup 6: winget install JRSoftware.InnoSetup)
#   Boxes-v<version>-win-x64.exe portable single-file exe
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content DesktopBoxes\DesktopBoxes.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in DesktopBoxes.csproj' }

if (Test-Path release) { Remove-Item release -Recurse -Force }
dotnet publish DesktopBoxes -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o release\win-x64
if ($LASTEXITCODE) { throw 'Publish failed' }
Copy-Item release\win-x64\DesktopBoxes.exe "release\Boxes-v$version-win-x64.exe"

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" installer\Boxes.iss
if ($LASTEXITCODE) { throw 'Installer build failed' }

Get-ChildItem release -File | ForEach-Object { '{0}  {1:N1} MB  {2}' -f $_.Name, ($_.Length / 1MB), (Get-FileHash $_.FullName).Hash.ToLower() }
