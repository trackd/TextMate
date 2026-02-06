Describe 'Get-SupportedTextMate' {
    It 'Returns at least one available language' {
        $result = Get-SupportedTextMate
        $result | Should -Not -BeNullOrEmpty
    }
}
