Describe 'Format-CSharp' {
    It 'Formats a simple C# string and returns renderables' {
        $code = 'public class Foo { }'
        $out = $code | Format-CSharp
        $out | Should -Not -BeNullOrEmpty
        $rendered = _GetSpectreRenderable $out -EscapeAnsi
        $rendered | Should -Match 'class|public class|namespace'
    }

    It 'Formats a C# file and returns renderables' {
        $temp = Join-Path $PSScriptRoot 'temp.cs'
        'public class Temp { }' | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = (Get-Item $temp) | Format-CSharp
            $out | Should -Not -BeNullOrEmpty
            $renderedFile = _GetSpectreRenderable $out -EscapeAnsi
            $renderedFile | Should -Match 'class|public class|namespace'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }
}
