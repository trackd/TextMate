BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }

    Import-Module (Join-Path $PSScriptRoot 'testhelper.psm1') -Force

}

Describe 'Out-Page' {
    It 'Has command metadata and help' {
        $cmd = Get-Command Out-Page -ErrorAction Stop
        $cmd | Should -Not -BeNullOrEmpty

        $help = Get-Help Out-Page -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
    }
}
