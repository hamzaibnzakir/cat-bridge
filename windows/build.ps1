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
Copy-Item ..\docs\WINDOWS.md (Join-Path $out "README.md")
Get-ChildItem $out -Filter *.pdb | Remove-Item
Get-ChildItem $out -Filter 'opencv_videoio_ffmpeg*.dll' | Remove-Item

$zip = Join-Path $PSScriptRoot 'dist\DynamicIsland-windows.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Built $zip"
