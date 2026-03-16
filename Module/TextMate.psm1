using namespace System.IO
using namespace System.Management.Automation
using namespace System.Reflection

$importModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
$isReload = $true
$alcAssemblyPath = [Path]::Combine($PSScriptRoot, 'lib', 'PSTextMate.ALC.dll')

if (-not (Test-Path -Path $alcAssemblyPath -PathType Leaf)) {
    throw "Could not find required ALC assembly at '$alcAssemblyPath'."
}

if (-not ('PSTextMate.ALC.LoadContext' -as [type])) {
    $isReload = $false
    Add-Type -Path $alcAssemblyPath
}
else {
    $loadedAlcAssemblyPath = [PSTextMate.ALC.LoadContext].Assembly.Location
    if ([Path]::GetFullPath($loadedAlcAssemblyPath) -ne [Path]::GetFullPath($alcAssemblyPath)) {
        throw "PSTextMate.ALC.LoadContext is already loaded from '$loadedAlcAssemblyPath'. Restart PowerShell to load this module from '$alcAssemblyPath'."
    }
}

$mainModule = [PSTextMate.ALC.LoadContext]::Initialize()
$innerMod = &$importModule -Assembly $mainModule -PassThru


if ($isReload) {
    # https://github.com/PowerShell/PowerShell/issues/20710
    $addExportedCmdlet = [PSModuleInfo].GetMethod(
        'AddExportedCmdlet',
        [BindingFlags]'Instance, NonPublic'
    )
    $addExportedAlias = [PSModuleInfo].GetMethod(
        'AddExportedAlias',
        [BindingFlags]'Instance, NonPublic'
    )
    foreach ($cmd in $innerMod.ExportedCmdlets.Values) {
        $addExportedCmdlet.Invoke($ExecutionContext.SessionState.Module, @(, $cmd))
    }
    foreach ($alias in $innerMod.ExportedAliases.Values) {
        $addExportedAlias.Invoke($ExecutionContext.SessionState.Module, @(, $alias))
    }
}
