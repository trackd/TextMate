#! /usr/bin/pwsh
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests,
    [Switch]$BuildAndTestOnly,
    [string]$Task,
    [ValidateSet('Spectre', 'Fullscreen', 'XenoAtom')]
    [string[]]$Renderer = @()
)

$ErrorActionPreference = 'Stop'


# Helper function to get paths
$buildparams = @{
    Configuration = $Configuration
    SkipHelp      = $SkipHelp.IsPresent
    SkipTests     = $SkipTests.IsPresent
    File          = (Get-Item (Join-Path $PSScriptRoot '*.build.ps1')).FullName
    Task          = 'All'
    Result        = 'Result'
    Renderer      = $Renderer
}
if (-not (Get-Module -ListAvailable -Name InvokeBuild | Where-Object Version -EQ ([version]'5.14.23'))) {
    Install-Module -Name InvokeBuild -RequiredVersion 5.14.23 -Scope CurrentUser -Force -AllowClobber
}
Import-Module InvokeBuild -RequiredVersion 5.14.23 -ErrorAction Stop

if ($Task) {
    $buildparams.Task = $Task
}
elseif ($BuildAndTestOnly) {
    $buildparams.Task = 'BuildAndTest'
}

if (-not $env:CI) {
    # this is just so the dll doesn't get locked on and i can rebuild without restarting terminal
    $sb = {
        param($bp)
        Invoke-Build @bp
    }
    & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -Command $sb -args $buildparams
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
else {
    Invoke-Build @buildparams
}
