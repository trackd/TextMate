BeforeAll {
    $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
}

Describe 'Shared output composition' {
    It 'Composes and pages rich content in a clean process without optional frameworks' {
        $check = {
            param($moduleFile, $resultFile)
            $ErrorActionPreference = 'Stop'
            Import-Module $moduleFile
            $style = [PSTextMate.Core.TextStyle]::Plain.WithForeground([PSTextMate.Core.TextColor]::Parse('#66D9EF'))
            $content = [PSTextMate.Core.TextContent]::Rows(
                [PSTextMate.Core.TextContent]::Text('Build complete', $style),
                [PSTextMate.Core.TextContent]::Progress(3, 4)
            ).Panel('Build', '3/4')
            $formatted = $content | Out-String
            $content | Out-Page
            [Console]::WriteLine()
            # Out-Page writes redirected text directly to Console.Out; the JSON follows it.
            [pscustomobject]@{
                Text = $content.ToDocument(30).Text
                FormatsAsContent = $formatted.Contains('╭─Build')
                Html = $content.ToDocument(30).ToHtml().Contains('Build complete')
                Frameworks = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -match '^(Spectre\.Console|XenoAtom\.)' }).Count
            } | ConvertTo-Json -Compress | Set-Content -LiteralPath $resultFile
        }
        $resultFile = Join-Path $TestDrive 'composition.json'
        $output = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $resultFile
        $LASTEXITCODE | Should -Be 0
        ($output -join "`n") | Should -Match 'Build complete'
        $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
        $result.FormatsAsContent | Should -BeTrue
        $result.Html | Should -BeTrue
        $result.Frameworks | Should -Be 0
        ($result.Text -split "`n")[0].Length | Should -Be 30
    }

    It 'Formats the content object at the current host width without changing console geometry' {
        if (-not ('TextMateWidthHost' -as [type])) {
            Add-Type -Path (Join-Path $PSScriptRoot 'ConsoleWidthHost.cs')
        }
        $widthHost = [TextMateWidthHost]::new()
        $runspace = [Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($widthHost)
        $pipeline = [PowerShell]::Create()
        try {
            $runspace.Open()
            $pipeline.Runspace = $runspace
            $null = $pipeline.AddCommand('Import-Module').AddParameter('Name', $moduleFile)
            $null = $pipeline.Invoke()
            $pipeline.Commands.Clear()
            foreach ($width in @(40, 90)) {
                $widthHost.SetWidth($width)
                $null = $pipeline.AddScript(@'
$PSStyle.OutputRendering = 'PlainText'
[PSTextMate.Core.TextContent]::Text('words inside a panel').Panel('Shared') | Out-String -Width 200
'@)
                $rendered = $pipeline.Invoke()[0].ToString()
                $pipeline.Commands.Clear()
                $pipeline.HadErrors | Should -BeFalse
                $top = @($rendered -split "`n" | Where-Object { $_.StartsWith('╭') })
                $top.Count | Should -Be 1
                $top[0].TrimEnd("`r").Length | Should -Be $width
            }
        }
        finally {
            $pipeline.Dispose()
            $runspace.Dispose()
        }
    }
}
