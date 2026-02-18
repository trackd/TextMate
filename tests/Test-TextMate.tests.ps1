BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}


Describe 'Test-TextMate' {
    It 'Recognizes powershell language' {
        Test-TextMate -Language 'powershell' | Should -BeTrue
    }

    It 'Recognizes .ps1 extension' {
        Test-TextMate -Extension '.ps1' | Should -BeTrue
    }

    It 'Recognizes an existing file as supported' {
        $testFile = Join-Path $PSScriptRoot 'Format-TextMate.tests.ps1'
        Test-TextMate -File $testFile | Should -BeTrue
    }
    It 'Should have Help and examples' {
        $help = Get-Help Test-TextMate -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
