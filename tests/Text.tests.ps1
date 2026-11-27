BeforeAll {
    if (-not (Get-Module TextMate)) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }
}

Describe 'TextMate output' {
    It 'Fits image requests to remaining viewport rows before decoding pixels' {
        $imageFile = Join-Path $TestDrive 'header.png'
        [IO.File]::WriteAllBytes($imageFile, [Convert]::FromHexString('89504e470d0a1a0a0000000d494844520000078000000438080600000000000000'))
        $check = {
            param($moduleFile, $imageFile)
            $ErrorActionPreference = 'Stop'
            Add-Type @'
namespace Sixel.Terminal {
    public sealed class CellSize {
        public int PixelWidth { get; } = 11;
        public int PixelHeight { get; } = 23;
    }
    public static class Compatibility {
        public static bool TerminalSupportsSixel() => true;
        public static CellSize GetCellSize() => new CellSize();
    }
}
'@
            $global:ImageRequests = [Collections.Generic.List[object]]::new()
            New-Module -Name Sixel -ScriptBlock {
                function ConvertTo-Sixel {
                    param($Stream, $Width, $Height, $Protocol, [switch]$Force)
                    $global:ImageRequests.Add([pscustomobject]@{
                        Width = $Width
                        Height = $Height
                        Position = $Stream.Position
                    })
                    if ($Protocol -eq 'Blocks') {
                        return ((1..$Height | ForEach-Object { '▀' * $Width }) -join "`n")
                    }
                    $rendered = [psobject]::AsPSObject("`eP0;1q" + '"1;1;11;6#1;2;100;0;0~' + "`e\")
                    $rendered.PSObject.Properties.Add([System.Management.Automation.PSNoteProperty]::new('Width', $Width))
                    $rendered.PSObject.Properties.Add([System.Management.Automation.PSNoteProperty]::new('Height', $Height))
                    return $rendered
                }
                Export-ModuleMember -Function ConvertTo-Sixel
            } | Import-Module -Global
            Import-Module $moduleFile
            $source = "# Heading`n`nA paragraph.`n`n![image]($imageFile)"
            $lexical = [PSTextMate.Core.SyntaxHighlighter]::Highlight($source, 'markdown')
            foreach ($rows in @(12, 20)) {
                $options = [PSTextMate.Core.RendererOptions]::new($false, $null, 100, $false, $rows)
                $document = [PSTextMate.Core.DocumentPresentation]::Prepare($lexical, $options)
                $bounds = $document.GetImageBounds(0)
                if ($bounds.Row + $bounds.Height -gt $rows -or $bounds.Column + $bounds.Width -gt 100) {
                    throw "Image exceeds viewport $rows."
                }
            }
            [pscustomobject]@{
                Requests = $global:ImageRequests.ToArray()
                Codecs = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -match 'ImageSharp|SkiaSharp|Magick' }).Count
            } | ConvertTo-Json -Depth 4 -Compress
        }
        $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $imageFile) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.Codecs | Should -Be 0
        $result.Requests.Count | Should -Be 4
        $result.Requests.Position | Should -Be @(0, 0, 0, 0)
        $result.Requests[0].Height | Should -BeLessThan $result.Requests[2].Height
        $result.Requests[0].Width | Should -BeLessThan 100
    }

    It 'Retains module Sixel pixels without placement commands or a second source read' {
        $imageFile = Join-Path $TestDrive 'image.bin'
        [IO.File]::WriteAllBytes($imageFile, [byte[]]@(42))
        $check = {
            param($moduleFile, $imageFile)
            $ErrorActionPreference = 'Stop'
            Add-Type @'
namespace Sixel.Terminal {
    public sealed class CellSize {
        public int PixelWidth { get; } = 2;
        public int PixelHeight { get; } = 4;
    }
    public static class Compatibility {
        public static bool TerminalSupportsSixel() => true;
        public static CellSize GetCellSize() => new CellSize();
    }
}
'@
            $global:TextMateImageReads = [Collections.Generic.List[int]]::new()
            New-Module -Name Sixel -ScriptBlock {
                function ConvertTo-Sixel {
                    param($Stream, $Width, $Height, $Protocol, [switch]$Force)
                    $global:TextMateImageReads.Add($Stream.ReadByte())
                    if ($Protocol -eq 'Blocks') { return "▀▀▀`n▀▀▀" }
                    $payload = "`eP0;1q" + '"1;1;6;8#1;2;100;0;0!6~' + "`e\"
                    $rendered = [psobject]::AsPSObject("`e7$payload`e8")
                    $rendered.PSObject.Properties.Add([System.Management.Automation.PSNoteProperty]::new('Width', 3))
                    $rendered.PSObject.Properties.Add([System.Management.Automation.PSNoteProperty]::new('Height', 2))
                    return $rendered
                }
                Export-ModuleMember -Function ConvertTo-Sixel
            } | Import-Module -Global
            Import-Module $moduleFile
            $document = "![image]($imageFile)" | Format-Markdown -Width 40
            [pscustomobject]@{
                Count = $document.Images.Length
                Payload = $document.Images.ToArray()[0].Image.Sixel.ToString()
                Bounds = $document.GetImageBounds(0)
                Reads = $global:TextMateImageReads.ToArray()
            } | ConvertTo-Json -Depth 4 -Compress
        }
        $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $imageFile) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.Count | Should -Be 1
        $result.Reads | Should -Be @(42, 42)
        $result.Payload.StartsWith("`eP") | Should -BeTrue
        $result.Payload.Contains("`e7") | Should -BeFalse
        $result.Payload.Contains("`e8") | Should -BeFalse
        $result.Bounds.Row | Should -Be 1
        $result.Bounds.Width | Should -Be 3
        $result.Bounds.Height | Should -Be 2
    }

    It 'Streams lexical output directly to console spans without returning a formatted string' {
        $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
        $check = {
            param($moduleFile)
            Import-Module $moduleFile -ErrorAction Stop
            [PSTextMate.Core.SyntaxHighlighter]::WriteTo("Write-Output 42`n# literal `e[2J", [Console]::Out, 'powershell', 'DarkPlus', $true, $false)
        }
        $result = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile
        $LASTEXITCODE | Should -Be 0
        $text = $result -join "`n"
        $text | Should -Match '1 │ Write-Output 42'
        $text | Should -Match '2 │ # literal ␛\[2J'
        $text | Should -Not -Match '\u001b'
    }







    It 'Loads Sixel only for images and maps its cells into built-in output' {
        $moduleRoot = Join-Path $TestDrive 'optional-modules'
        $sixelDirectory = Join-Path $moduleRoot 'Sixel'
        $null = New-Item -Path $sixelDirectory -ItemType Directory -Force
        @'
function ConvertTo-Sixel {
    param($Path, $Width, $Height, $Protocol, [switch]$Force)
    $global:TextMateImageCall = @{
        Path = $Path
        Width = $Width
        Height = $Height
        Protocol = $Protocol
        Force = $Force.IsPresent
    }
    "`e[38;2;255;0;0;48;2;0;0;255m▀`e[0m"
}
Export-ModuleMember -Function ConvertTo-Sixel
'@ | Set-Content -LiteralPath (Join-Path $sixelDirectory 'Sixel.psm1')
        $markdownFile = Join-Path $TestDrive 'document.md'
        '![red](images/red.bmp)' | Set-Content -LiteralPath $markdownFile -NoNewline
        $check = {
            param($moduleFile, $moduleRoot, $markdownFile)
            $ErrorActionPreference = 'Stop'
            $env:PSModulePath = $moduleRoot
            Import-Module $moduleFile
            $null = 'Write-Output 1' | Format-PowerShell
            $before = [bool](Get-Module Sixel)
            $document = Format-Markdown -InputObject $markdownFile -Width 40
            [pscustomobject]@{
                Before = $before
                After = [bool](Get-Module Sixel)
                Text = $document.Text
                Colors = @($document.Ranges.ToArray() | ForEach-Object { $_.Style.Foreground })
                Backgrounds = @($document.Ranges.ToArray() | ForEach-Object { $_.Style.Background })
                Call = $global:TextMateImageCall
            } | ConvertTo-Json -Depth 4 -Compress
        }
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $moduleRoot, $markdownFile) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.Before | Should -BeFalse
        $result.After | Should -BeTrue
        $result.Text | Should -Match 'Image: red\n▀'
        $result.Colors | Should -Contain 0xff0000
        $result.Backgrounds | Should -Contain 0x0000ff
        [System.IO.Path]::GetFullPath($result.Call.Path) | Should -BeExactly (Join-Path $TestDrive 'images' 'red.bmp')
        $result.Call.Width | Should -Be 40
        # The image label occupies the first row of the redirected 30-row viewport.
        $result.Call.Height | Should -Be 29
        $result.Call.Protocol | Should -BeExactly 'Blocks'
        $result.Call.Force | Should -BeTrue
    }

    It 'Retains image links when Sixel is <Mode>' -TestCases @(
        @{ Mode = 'missing' }
        @{ Mode = 'failing' }
        @{ Mode = 'returning control sequences' }
    ) {
        param($Mode)
        $moduleRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $null = New-Item -Path $moduleRoot -ItemType Directory
        $check = {
            param($moduleFile, $moduleRoot, $mode)
            $ErrorActionPreference = 'Stop'
            $env:PSModulePath = $moduleRoot
            if ($mode -ne 'missing') {
                $global:TextMateImageMode = $mode
                New-Module -Name Sixel -ScriptBlock {
                    function ConvertTo-Sixel {
                        param($Path, $Width, $Height, $Protocol, [switch]$Force)
                        if ($global:TextMateImageMode -eq 'failing') { throw 'Decoder unavailable.' }
                        "`e[2J"
                    }
                    Export-ModuleMember -Function ConvertTo-Sixel
                } | Import-Module -Global
            }
            Import-Module $moduleFile
            $document = '![missing](missing.bmp)' | Format-Markdown -Width 40
            [pscustomobject]@{
                Text = $document.Text
                Links = @($document.Ranges.ToArray() | ForEach-Object { $_.Style.Hyperlink })
            } | ConvertTo-Json -Compress
        }
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $moduleRoot, $Mode) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.Text.Trim() | Should -BeExactly '🖼️ Image: missing'
        $result.Links | Should -Contain 'missing.bmp'
    }

    It 'Renders rich Markdown without loading either rendering framework' {
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $check = {
            param($moduleFile)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            $source = "# Heading`n`n**bold** and [link](https://example.com)."
            $result = $source | Format-Markdown -Width 40
            [pscustomobject]@{
                Source = $result.Source
                Text = $result.Text
                Frameworks = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetName().Name } |
                    Where-Object { $_ -match '^(Spectre|XenoAtom|SixLabors)' })
            } | ConvertTo-Json -Compress
        }
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.Source | Should -Match '^# Heading'
        $result.Text | Should -Match '^Heading'
        $result.Text | Should -Not -Match '\*\*|\]\('
        $result.Frameworks.Count | Should -Be 0
    }

    It 'Uses the same pager presentation for all output formats' {
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $check = {
            param($moduleFile, $format)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            "# Heading`n`n**bold**" | Format-Markdown -Width 40 -OutputFormat $format -Page -ErrorAction Stop
        }
        $expected = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, 'Text'
        $LASTEXITCODE | Should -Be 0
        foreach ($format in @('Spectre', 'Fullscreen')) {
            $actual = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $format
            $LASTEXITCODE | Should -Be 0
            ($actual -join "`n") | Should -BeExactly ($expected -join "`n")
        }
    }

    It 'Returns source-preserving documents by default for pipeline input' {
        $source = "Write-Output '[red]🦊[/]'  `r`n`r`n"
        $result = $source | Format-PowerShell -ErrorAction Stop
        $result.GetType().FullName | Should -Be 'PSTextMate.Core.HighlightedDocument'
        $result.Source | Should -BeExactly $source
        $result.LineCount | Should -Be 3
        $result.Ranges.Length | Should -BeGreaterThan 0
    }

    It 'Preserves empty input as one blank line' {
        $result = '' | Format-PowerShell -ErrorAction Stop
        $result.Source | Should -BeExactly ''
        $result.LineCount | Should -Be 1
    }

    It 'Infers a file language and preserves CRLF and trailing spaces' {
        $testFile = Join-Path $TestDrive 'source.cs'
        $source = "public class Demo { }  `r`n"
        [System.IO.File]::WriteAllText($testFile, $source)
        $result = Format-TextMate -InputObject $testFile -ErrorAction Stop
        $result.Language | Should -Be 'csharp'
        $result.Source | Should -BeExactly $source
        $result.ShowLineNumbers | Should -BeFalse
    }

    It 'Honors property binding and source extension hints' {
        $source = [psobject]::new('public class Demo {}')
        $source.psobject.Properties.Add([psnoteproperty]::new('PSChildName', 'demo.cs'))
        ($source | Format-TextMate -ErrorAction Stop).Language | Should -Be 'csharp'
    }

    It 'Reports missing files and unsupported languages through ErrorAction Stop' {
        { Format-TextMate -InputObject (Join-Path $TestDrive 'missing.ps1') -ErrorAction Stop } | Should -Throw '*File not found*'
        { '$a = 1' | Format-TextMate -Language 'missing-language' -ErrorAction Stop } | Should -Throw '*No TextMate grammar*'
    }

    It 'Keeps optional engines and renderers unloaded on import and renderers unloaded on highlighting' {
        $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
        $check = {
            param($moduleFile)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            $onImport = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetName().Name })
            $result = 'Write-Output 1' | Format-PowerShell
            $html = $result.ToHtml()
            $afterHighlight = @([AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetName().Name })
            [pscustomobject]@{
                OnImport = @($onImport -match '^(Tint|GregsEx|Spectre|XenoAtom|Markdig|SixLabors|Onig|TextMateSharp)')
                AfterHighlight = @($afterHighlight -match '^(Spectre|XenoAtom|Markdig|SixLabors|Onig|TextMateSharp)')
                Source = $result.Source
                Html = $html
            } | ConvertTo-Json -Compress
        }
        $json = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile
        $LASTEXITCODE | Should -Be 0
        $result = $json | ConvertFrom-Json
        $result.OnImport.Count | Should -Be 0
        $result.AfterHighlight.Count | Should -Be 0
        $result.Source | Should -BeExactly 'Write-Output 1'
        $result.Html | Should -Match '^<pre class="textmate"'
    }

    It 'Keeps built-in output usable with renderer directories physically absent' {
        $moduleCopy = Join-Path $TestDrive 'core-module'
        $null = New-Item -Path $moduleCopy -ItemType Directory
        Copy-Item (Join-Path $PSScriptRoot '..' 'output' '*') -Destination $moduleCopy -Recurse
        $rendererPath = Join-Path $moduleCopy 'lib' 'renderers'
        if (Test-Path -LiteralPath $rendererPath) {
            $resolved = [System.IO.Path]::GetFullPath($rendererPath)
            if (-not $resolved.StartsWith([System.IO.Path]::GetFullPath($TestDrive), [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Renderer removal path is outside TestDrive.'
            }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
        $check = {
            param($moduleFile)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            ('Write-Output 1' | Format-PowerShell).Source
            try {
                'Write-Output 1' | Format-PowerShell -OutputFormat Spectre -ErrorAction Stop
                throw 'Missing renderer was accepted.'
            }
            catch {
                if ($_.Exception.Message -notlike '*optional Spectre renderer is not installed*') { throw }
            }
            exit 0
        }
        $result = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args (Join-Path $moduleCopy 'TextMate.psd1')
        $LASTEXITCODE | Should -Be 0
        $result | Should -BeExactly 'Write-Output 1'
    }
    It 'Displays deferred Mermaid fences as ordinary code' {
        $source = "``````mermaid`nflowchart LR`nA[Start] --> B[Finish]`n``````"
        foreach ($width in @(40, 100)) {
            $document = $source | Format-Markdown -Width $width
            $document.Source | Should -BeExactly $source
            $document.Text | Should -Match 'flowchart LR'
            $document.Text | Should -Match 'A\[Start\] --> B\[Finish\]'
            $document.Text | Should -Match 'mermaid'
        }
    }

    It 'Excludes parked diagram types and APIs from the built module' {
        $assembly = [PSTextMate.Core.HighlightedDocument].Assembly
        foreach ($name in @('DiagramScene', 'DiagramRaster', 'MermaidParser', 'FlowchartLayout')) {
            $assembly.GetType("PSTextMate.Core.$name", $false) | Should -BeNullOrEmpty
        }
        [PSTextMate.Core.HighlightedDocument].GetProperty('Diagrams') | Should -BeNullOrEmpty
        [PSTextMate.Core.DocumentPresentation].GetMethod('ParseMermaid') | Should -BeNullOrEmpty
        [PSTextMate.Core.OutputRenderer].GetMethod('WriteSixel') | Should -BeNullOrEmpty
    }
}
