@{
    RootModule           = 'PSTextMate.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = 'a6490f8a-1f53-44f2-899c-bf66b9c6e608'
    Author               = 'trackd'
    CompanyName          = 'trackd'
    Copyright            = '(c) trackd. All rights reserved.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = 'Core'
    CmdletsToExport      = @(
        'Show-TextMate'
        'Test-SupportedTextMate'
        'Get-SupportedTextMate'
        'Format-CSharp'
        'Format-Markdown'
        'Format-PowerShell'
        'Measure-String'
    )
    AliasesToExport      = @(
        'fcs'
        'fmd'
        'fps'
        'st'
        'Show-Code'
    )
    RequiredAssemblies   = @(
        'lib/Onigwrap.dll'
        'lib/TextMateSharp.dll'
        'lib/TextMateSharp.Grammars.dll'
        'Markdig.Signed.dll'
    )
    FormatsToProcess     = 'PSTextMate.format.ps1xml'
    RequiredModules      = @(
        @{
            ModuleName     = 'PwshSpectreConsole'
            ModuleVersion  = '2.3.0'
            MaximumVersion = '2.99.99'
        }
    )
}
