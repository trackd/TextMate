BeforeAll {
    if (-not (Get-Module 'PSTextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'PSTextMate.psd1') -ErrorAction Stop
    }
}

Describe 'Get-SupportedTextMate' {
    It 'Returns at least one available language' {
        $result = Get-SupportedTextMate
        $result | Should -Not -BeNullOrEmpty
    }
}
