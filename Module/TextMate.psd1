@{
    RootModule           = 'lib/PSTextMate.dll'
    ModuleVersion        = '0.1.0'
    GUID                 = 'fe78d2cb-2418-4308-9309-a0850e504cd6'
    Author               = 'trackd'
    CompanyName          = 'trackd'
    Copyright            = '(c) trackd. All rights reserved.'
    Description          = 'A PowerShell module for syntax highlighting using TextMate grammars. Using PwshSpectreConsole for rendering.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = 'Core'
    CmdletsToExport      = @(
        'Format-TextMate'
        'Format-CSharp'
        'Format-Markdown'
        'Format-PowerShell'
        'Test-TextMate'
        'Get-TextMateGrammar'
    )
    AliasesToExport      = @(
        'fcs'
        'fmd'
        'fps'
        'ftm'
        'Show-TextMate'
    )
    FormatsToProcess     = 'TextMate.format.ps1xml'
    RequiredModules      = @(
        @{
            ModuleName     = 'PwshSpectreConsole'
            ModuleVersion  = '2.3.0'
            MaximumVersion = '2.99.99'
        }
    )
    PrivateData          = @{
        PSData = @{
            Tags       = 'Windows', 'Linux', 'OSX', 'TextMate', 'Markdown', 'SyntaxHighlighting'
            LicenseUri = 'https://github.com/trackd/TextMate/blob/main/LICENSE'
            ProjectUri = 'https://github.com/trackd/TextMate'
            IconUri    = 'https://raw.githubusercontent.com/trackd/TextMate/main/assets/logo.png'
        }
    }
    HelpInfoURI            = 'https://github.com/trackd/TextMate/tree/main/docs/en-us'
    DscResourcesToExport   = @()
    RequiredAssemblies     = @()
    ModuleList             = @()
    FileList               = @()
    ScriptsToProcess       = @()
    TypesToProcess         = @()
    NestedModules          = @()
    FunctionsToExport      = @()
    VariablesToExport      = @()
}
