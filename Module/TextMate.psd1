@{
    RootModule           = 'lib/PSTextMate.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = 'a6490f8a-1f53-44f2-899c-bf66b9c6e608'
    Author               = 'trackd'
    CompanyName          = 'trackd'
    Copyright            = '(c) trackd. All rights reserved.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = 'Core'
    CmdletsToExport      = @(
        'Format-TextMate'
        'Format-CSharp'
        'Format-Markdown'
        'Format-PowerShell'
        'Test-SupportedTextMate'
        'Get-SupportedTextMate'
    )
    AliasesToExport      = @(
        'fcs'
        'fmd'
        'fps'
        'ftm'
        'Show-TextMate'
    )
    RequiredAssemblies   = @()
    FormatsToProcess     = 'TextMate.format.ps1xml'
    RequiredModules      = @(
        @{
            ModuleName     = 'PwshSpectreConsole'
            ModuleVersion  = '2.3.0'
            MaximumVersion = '2.99.99'
        }
    )
}
