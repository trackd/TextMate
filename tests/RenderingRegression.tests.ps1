BeforeAll {
    $moduleFile = Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1'
    if (-not (Get-Module TextMate)) { Import-Module $moduleFile -ErrorAction Stop }

    function Copy-TestConsumerRuntime {
        param([string]$Project, [string]$Destination)
        $assetsFile = Join-Path $PSScriptRoot '..' 'src' "PSTextMate.$Project" 'obj' 'project.assets.json'
        $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json -AsHashtable
        $target = $assets.targets['net10.0']
        foreach ($library in $target.Keys) {
            if ($library -notmatch '^(Spectre\.Console|XenoAtom\.[^/]+)/') { continue }
            foreach ($asset in $target[$library].runtime.Keys) {
                if ($asset -notlike '*.dll') { continue }
                foreach ($root in $assets.packageFolders.Keys) {
                    $path = Join-Path $root $assets.libraries[$library].path $asset
                    if (Test-Path -LiteralPath $path) {
                        Copy-Item -LiteralPath $path -Destination $Destination
                        break
                    }
                }
            }
        }
    }
}

Describe 'Rendering regressions' {
    It 'Displays terminal controls in Markdown code without executing them or changing Source' {
        $source = 'before ' + [char]96 + [char]27 + '[?25l' + [char]96 + ' after'
        $document = $source | Format-Markdown
        $document.Source | Should -BeExactly $source
        $document.Text | Should -BeExactly 'before ␛[?25l after'
        $document.ToAnsi().Contains("`e[?25l") | Should -BeFalse
    }

    It 'Uses the current console window width and honors an explicit override' {
        if (-not ('TextMateWidthHost' -as [type])) {
            Add-Type -Path (Join-Path $PSScriptRoot 'ConsoleWidthHost.cs')
        }
        $widthHost = [TextMateWidthHost]::new()
        $runspace = [Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($widthHost)
        try {
            $runspace.Open()
            $pipeline = [PowerShell]::Create()
            try {
                $pipeline.Runspace = $runspace
                $null = $pipeline.AddCommand('Import-Module').AddParameter('Name', $moduleFile)
                $null = $pipeline.Invoke()
                $pipeline.Commands.Clear()
                $results = foreach ($width in @(120, 100, 160)) {
                    $widthHost.SetWidth($width)
                    $null = $pipeline.AddScript("('word ' * 50) | Format-Markdown")
                    $document = $pipeline.Invoke()[0]
                    $pipeline.Commands.Clear()
                    ($document.Text -split "`n" | ForEach-Object Length | Measure-Object -Maximum).Maximum
                }
                $null = $pipeline.AddScript("('word ' * 50) | Format-Markdown -Width 40")
                $override = $pipeline.Invoke()[0]
                $results | Should -Be @(119, 99, 159)
                ($override.Text -split "`n" | ForEach-Object Length | Measure-Object -Maximum).Maximum | Should -Be 39
                $pipeline.HadErrors | Should -BeFalse
            }
            finally { $pipeline.Dispose() }
        }
        finally { $runspace.Dispose() }
    }

    It 'Preserves blank lines between headings and paragraphs as the installed renderer does' {
        $source = "# Heading`n`nFirst paragraph.`n`nSecond paragraph."
        $document = $source | Format-Markdown -Width 80
        $document.Text | Should -BeExactly "Heading`n`nFirst paragraph.`n`nSecond paragraph."
        $document.Source | Should -BeExactly $source
    }

    It 'Matches rounded table borders and separates the header without ruling every body row' {
        $source = @'
| A | B |
|:--|--:|
| one | 1 |
| two | 2 |
'@
        $document = $source | Format-Markdown -Width 80
        $expected = @'
╭─────┬───╮
│ A   │ B │
├─────┼───┤
│ one │ 1 │
│ two │ 2 │
╰─────┴───╯
'@
        $document.Text | Should -BeExactly ($expected -replace "`r`n", "`n")
    }

    It 'Keeps hard line breaks inside table cells aligned with their borders' {
        $source = "| Name | Value |`n|---|---|`n| first<br>second | 12 |"
        $document = $source | Format-Markdown -Width 60
        $document.Text | Should -Not -Match '<br>'
        $rows = $document.Text -split "`n"
        $first = @($rows | Where-Object { $_ -match 'first' })
        $second = @($rows | Where-Object { $_ -match 'second' })
        $first.Count | Should -Be 1
        $second.Count | Should -Be 1
        $first[0].Length | Should -Be $second[0].Length
        $second[0] | Should -Match '^│.*second.*│.*│$'
    }

    It 'Ships optional adapters in the default package' {
        foreach ($format in @('Spectre', 'XenoAtom')) {
            Join-Path $PSScriptRoot '..' 'output' 'lib' 'renderers' $format "PSTextMate.$format.dll" |
                Should -Exist
        }
    }

    It 'Auto-imports the <Format> consumer and recovers after a missing adapter' -TestCases @(
        @{ Format = 'Spectre'; Module = 'PwshSpectreConsole'; Runtime = 'Spectre.Console'; Type = 'PSTextMate.Spectre.TextMateRenderable' }
        @{ Format = 'Fullscreen'; Module = 'fullscreen'; Runtime = 'XenoAtom.Terminal.UI'; Type = 'PSTextMate.XenoAtom.TextMateVisual' }
    ) {
        param($Format, $Module, $Runtime, $Type)
        $moduleRoot = Join-Path $TestDrive $Format 'modules'
        $consumer = Join-Path $moduleRoot $Module
        $library = Join-Path $consumer 'lib'
        $null = New-Item -Path $library -ItemType Directory -Force
        Copy-TestConsumerRuntime -Project $(if ($Format -eq 'Fullscreen') { 'XenoAtom' } else { $Format }) -Destination $library
        $unrelated = Join-Path $TestDrive $Format 'unrelated'
        $null = New-Item -Path $unrelated -ItemType Directory
        Get-ChildItem -LiteralPath $library -Filter '*.dll' | Copy-Item -Destination $unrelated
        @'
$context = [System.Runtime.Loader.AssemblyLoadContext]::new('TextMate.TestConsumer', $false)
foreach ($file in [IO.Directory]::GetFiles((Join-Path $PSScriptRoot 'lib'), '*.dll')) {
    $null = $context.LoadFromAssemblyPath($file)
}
'@ | Set-Content -LiteralPath (Join-Path $consumer "$Module.psm1")
        $copy = Join-Path $TestDrive $Format 'TextMate'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..' 'output') -Destination $copy -Recurse
        $check = {
            param($moduleFile, $moduleRoot, $format, $module, $runtime, $unrelated)
            $ErrorActionPreference = 'Stop'
            $env:PSModulePath = $moduleRoot
            $foreign = [System.Runtime.Loader.AssemblyLoadContext]::new('Unrelated.Framework', $false)
            foreach ($file in [IO.Directory]::GetFiles($unrelated, '*.dll')) {
                $null = $foreign.LoadFromAssemblyPath($file)
            }
            Import-Module $moduleFile
            $backend = if ($format -eq 'Fullscreen') { 'XenoAtom' } else { $format }
            $adapter = Join-Path (Split-Path $moduleFile) 'lib' 'renderers' $backend "PSTextMate.$backend.dll"
            $bytes = [IO.File]::ReadAllBytes($adapter)
            Remove-Item -LiteralPath $adapter
            $failed = $false
            try { $null = 'Write-Output 1' | Format-PowerShell -OutputFormat $format -ErrorAction Stop }
            catch {
                if ($_.Exception.Message -notlike '*optional*renderer is not installed*') { throw }
                $failed = $true
            }
            [IO.File]::WriteAllBytes($adapter, $bytes)
            $value = 'Write-Output 1' | Format-PowerShell -OutputFormat $format -ErrorAction Stop
            $frameworkAssembly = if ($format -eq 'Spectre') {
                $value.GetType().GetInterface('Spectre.Console.Rendering.IRenderable').Assembly
            }
            else { $value.GetType().BaseType.Assembly }
            [pscustomobject]@{
                FirstFailed = $failed
                Type = $value.GetType().FullName
                ConvertedType = [PSTextMate.Core.OutputRenderer]::GetDocument($value).ConvertTo($format).GetType().FullName
                Imported = [bool](Get-Module $module)
                Context = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($frameworkAssembly).Name
                Runtime = $frameworkAssembly.GetName().Name
                Text = [PSTextMate.Core.OutputRenderer]::GetDocument($value).Text
            } | ConvertTo-Json -Compress
        }
        $result = (& (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args (Join-Path $copy 'TextMate.psd1'), $moduleRoot, $Format, $Module, $Runtime, $unrelated) | ConvertFrom-Json
        $LASTEXITCODE | Should -Be 0
        $result.FirstFailed | Should -BeTrue
        $result.Imported | Should -BeTrue
        $result.Type | Should -Be $Type
        $result.ConvertedType | Should -Be $Type
        $result.Context | Should -BeExactly 'TextMate.TestConsumer'
        $result.Runtime | Should -BeExactly $Runtime
        $result.Text | Should -BeExactly 'Write-Output 1'
    }
}
