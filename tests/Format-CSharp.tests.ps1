BeforeAll {
    if (-Not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }

    Import-Module (Join-Path $PSScriptRoot 'testhelper.psm1') -Force
}

Describe 'Format-CSharp' -Tag 'Adapter' {
    It 'Formats a simple C# string and returns renderables' {
        $code = 'public class Foo { }'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $out | Should -Not -BeNullOrEmpty
        $out.LineCount | Should -BeGreaterThan 0
        $out.Language | Should -Be 'csharp'
        $out.ShowLineNumbers | Should -Be $false
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'public class Foo'
    }

    It 'Formats a simple C# string' {
        $code = 'public class Foo { }'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'public class Foo'
    }

    It 'Formats a C# file and returns renderables' {
        $temp = Join-Path $PSScriptRoot 'temp.cs'
        'public class Temp { }' | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = Get-Item $temp | Format-CSharp -OutputFormat Spectre
            $out | Should -Not -BeNullOrEmpty
            $out.Language | Should -Be 'csharp'
            $out.LineCount | Should -BeGreaterThan 0
            $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
            $rendered | Should -Match 'public class Temp'
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }

    It 'Splits multiline pipeline items into individual lines' {
        $s1 = "// a`npublic class A { }"
        $s2 = "// b`npublic class B { }"

        $out = @($s1, $s2) | Format-CSharp -OutputFormat Spectre

        $out.LineCount | Should -Be 4
    }

    It 'Renders generic type syntax without crashing' {
        $code = 'public class Repo<T> where T : class { }'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Repo'
        $rendered | Should -Match 'class'
    }

    It 'Renders using directives' {
        $code = "using System;`nusing System.Collections.Generic;"
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'System'
        $rendered | Should -Match 'Collections'
    }

    It 'Renders XML doc comments without losing content' {
        $code = "/// <summary>`n/// Does a thing.`n/// </summary>`npublic void DoThing() { }"
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'summary'
        $rendered | Should -Match 'DoThing'
    }

    It 'Renders LINQ expression syntax' {
        $code = 'var result = items.Where(x => x > 0).Select(x => x * 2).ToList();'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'result'
        $rendered | Should -Match 'Where'
        $rendered | Should -Match 'Select'
    }

    It 'Preserves interpolated string content' {
        $code = 'var msg = $"Hello {name}, you are {age} years old.";'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'Hello'
        $rendered | Should -Match 'name'
        $rendered | Should -Match 'age'
    }

    It 'Renders interface and record declarations' {
        $code = "public interface IWidget { void Draw(); }`npublic record Point(int X, int Y);"
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'IWidget'
        $rendered | Should -Match 'Point'
    }

    It 'Preserves unicode characters inside a string literal' {
        $code = 'var s = "emoji: \U0001F680";'
        $out = $code | Format-CSharp -OutputFormat Spectre
        $rendered = _GetSpectreRenderable -RenderableObject $out -EscapeAnsi
        $rendered | Should -Match 'emoji'
    }

    It 'Counts the final empty line when a source file ends with a newline' {
        $temp = Join-Path $PSScriptRoot 'temp_count.cs'
        "// line 1`npublic class X { }`n// line 3" | Out-File -FilePath $temp -Encoding utf8
        try {
            $out = Get-Item $temp | Format-CSharp -OutputFormat Spectre
            $out.LineCount | Should -Be 4
        } finally {
            Remove-Item -Force -ErrorAction SilentlyContinue $temp
        }
    }

    It 'Should have Help and examples' {
        $help = Get-Help Format-CSharp -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.examples.example.Count | Should -BeGreaterThan 1
    }
}
