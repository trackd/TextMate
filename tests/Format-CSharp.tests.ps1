BeforeAll {
    if (-Not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }

    Import-Module (Join-Path $PSScriptRoot 'testhelper.psm1') -Force
}

Describe 'Format-CSharp' {
    It 'Formats a simple C# string and returns renderables' {
        $code = 'public class Foo { }'
        $out = $code | Format-CSharp
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'public class Foo'
    }

        It 'Formats a simple C# string' {
            $code = 'public class Foo { }'
            $out = $code | Format-CSharp
            $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $rendered | Should -Match 'public class Foo'
        }

    It 'Formats a C# file and returns renderables' {
        $temp = Join-Path $PSScriptRoot 'temp.cs'
        'public class Temp { }' | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = Get-Item $temp | Format-CSharp
            $out | Should -Not -BeNullOrEmpty
            $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $rendered | Should -Match 'public class Temp'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }

    It 'Splits multiline pipeline items into individual lines' {
        $s1 = "// a`npublic class A { }"
        $s2 = "// b`npublic class B { }"

        $out = @($s1, $s2) | Format-CSharp

        $out.LineCount | Should -Be 4
    }

    It 'Should have Help and examples' {
        $help = Get-Help Format-CSharp -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
