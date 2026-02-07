BeforeAll {
    if (-not (Get-Module 'PSTextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'PSTextMate.psd1') -ErrorAction Stop
    }
}

Describe 'Test-SupportedTextMate' {
    It 'Recognizes powershell language' {
        Test-SupportedTextMate -Language 'powershell' | Should -BeTrue
    }

    It 'Recognizes .ps1 extension' {
        Test-SupportedTextMate -Extension '.ps1' | Should -BeTrue
    }

    It 'Recognizes an existing file as supported' {
        $testFile = Join-Path $PSScriptRoot 'Show-TextMate.tests.ps1'
        Test-SupportedTextMate -File $testFile | Should -BeTrue
    }
}
