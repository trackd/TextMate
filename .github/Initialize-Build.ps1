#Requires -Version 7.6
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$tools = @{
    InvokeBuild = '5.14.23'
    'Microsoft.PowerShell.PlatyPS' = '1.0.1'
    Pester = '5.7.1'
}
foreach ($name in $tools.Keys) {
    $version = $tools[$name]
    if (-not (Get-Module -ListAvailable -Name $name | Where-Object Version -EQ ([version]$version))) {
        Install-Module -Name $name -RequiredVersion $version -Repository PSGallery -Scope CurrentUser -Force -AllowClobber
    }
}
