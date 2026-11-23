# Builds the Windows app into .\dist\DynamicIsland and zips it.
#   pwsh ./build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$out = Join-Path $PSScriptRoot 'dist\DynamicIsland'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained false -o $out
dotnet publish FaceHelper\FaceHelper.csproj -c Release -r win-x64 --self-contained false -o $out
dotnet build FaceSignIn\FaceSignIn.csproj -c Release
Copy-Item FaceSignIn\bin\Release\net48\DynamicIsland.FaceSignIn.dll $out
Copy-Item 'Turn off Cat Face sign-in.cmd', 'Restore taskbar.cmd' $out
Copy-Item ..\docs\SETUP.md (Join-Path $out "README.md")
Copy-Item ..\docs\FEATURES.md, ..\docs\TROUBLESHOOTING.md $out
Get-ChildItem $out -Filter *.pdb | Remove-Item
Get-ChildItem $out -Filter 'opencv_videoio_ffmpeg*.dll' | Remove-Item

# installer (Inno Setup 6)
$version = ([xml](Get-Content DynamicIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
  & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$out" Installer\CatBridge.iss
  if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
  Write-Host "Built dist\DynamicIsland-Setup.exe ($version)"
} else { Write-Warning "Inno Setup 6 not found: skipping the installer (winget install JRSoftware.InnoSetup)" }

$zip = Join-Path $PSScriptRoot 'dist\DynamicIsland-windows.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Built $zip"
