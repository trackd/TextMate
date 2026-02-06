#Requires -Version 7.4
if (-Not $PSScriptRoot) {
    return 'Run this script from the root of the project'
}
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

dotnet clean
dotnet restore

$ModuleFilesFolder = Join-Path -Path $PSScriptRoot -ChildPath 'Module'
if (-Not (Test-Path $ModuleFilesFolder)) {
    $null = New-Item -ItemType Directory -Path $ModuleFilesFolder -Force
}
Get-ChildItem -Path (Join-Path -Path $PSScriptRoot -ChildPath 'Output') -File -Recurse | Remove-Item -Force

$moduleLibFolder = Join-Path -Path $PSScriptRoot -ChildPath 'Output' | Join-Path -ChildPath 'lib'
if (-Not (Test-Path $moduleLibFolder)) {
    $null = New-Item -ItemType Directory -Path $moduleLibFolder -Force
}

$csproj = Get-Item (Join-Path -Path $PSScriptRoot -ChildPath 'src' | Join-Path -ChildPath 'PSTextMate.csproj')
$outputfolder = Join-Path -Path $PSScriptRoot -ChildPath 'packages'
if (-Not (Test-Path -Path $outputfolder)) {
    $null = New-Item -ItemType Directory -Path $outputfolder -Force
}

dotnet publish $csproj.FullName -c Release -o $outputfolder
Copy-Item -Path $ModuleFilesFolder/* -Destination (Join-Path -Path $PSScriptRoot -ChildPath 'Output') -Force -Recurse -Include '*.psd1', '*.psm1', '*.ps1xml'

Get-ChildItem -Path $moduleLibFolder -File | Remove-Item -Force


Get-ChildItem -Path (Join-Path -Path $outputfolder -ChildPath 'runtimes' | Join-Path -ChildPath 'win-x64' | Join-Path -ChildPath 'native') -Filter *.dll | Move-Item -Destination $moduleLibFolder -Force
Get-ChildItem -Path (Join-Path -Path $outputfolder -ChildPath 'runtimes' | Join-Path -ChildPath 'osx-arm64' | Join-Path -ChildPath 'native') -Filter *.dylib | Move-Item -Destination $moduleLibFolder -Force
Get-ChildItem -Path (Join-Path -Path $outputfolder -ChildPath 'runtimes' | Join-Path -ChildPath 'linux-x64' | Join-Path -ChildPath 'native') -Filter *.so | Copy-Item -Destination $moduleLibFolder -Force
Move-Item (Join-Path -Path $outputfolder -ChildPath 'PSTextMate.dll') -Destination (Split-Path $moduleLibFolder) -Force
Get-ChildItem -Path $outputfolder -File |
    Where-Object { -Not $_.Name.StartsWith('System.Text') -And $_.Extension -notin '.json','.pdb','.xml' } |
        Move-Item -Destination $moduleLibFolder -Force

Pop-Location
