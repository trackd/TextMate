BeforeAll {
    if (-not (Get-Module 'TextMate')) {
        Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    }

    Import-Module (Join-Path $PSScriptRoot 'testhelper.psm1') -Force

}

Describe 'Out-Page' {
    It 'Writes redirected pipeline input before the producer completes' {
        $releaseFile = Join-Path $TestDrive 'release.txt'
        $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
        $commandText = @'
param($moduleFile, $releaseFile)
Import-Module $moduleFile -ErrorAction Stop
& {
    'early-output'
    while (-not [IO.File]::Exists($releaseFile)) { Start-Sleep -Milliseconds 20 }
    'late-output'
} | Out-Page
'@
        $scriptFile = Join-Path $TestDrive 'producer.ps1'
        Set-Content -LiteralPath $scriptFile -Value $commandText
        $startInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')))
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $scriptFile, $moduleFile, $releaseFile)) {
            $startInfo.ArgumentList.Add($argument)
        }
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $childProcess = [Diagnostics.Process]::Start($startInfo)
        try {
            $firstLine = $childProcess.StandardOutput.ReadLineAsync()
            $firstLine.Wait(10000) | Should -BeTrue
            $firstLine.Result | Should -BeExactly 'early-output'
            $childProcess.HasExited | Should -BeFalse
            Set-Content -LiteralPath $releaseFile -Value 'continue'
            $remaining = $childProcess.StandardOutput.ReadToEndAsync()
            $childProcess.WaitForExit(10000) | Should -BeTrue
            $childProcess.ExitCode | Should -Be 0
            $remaining.Result.Trim() | Should -BeExactly 'late-output'
            $childProcess.StandardError.ReadToEnd() | Should -BeNullOrEmpty
        }
        finally {
            if (-not $childProcess.HasExited) { $childProcess.Kill($true) }
            $childProcess.Dispose()
        }
    }
    It 'Has command metadata and help' {
        $cmd = Get-Command Out-Page -ErrorAction Stop
        $cmd | Should -Not -BeNullOrEmpty

        $help = Get-Help Out-Page -Full
        $help.Synopsis | Should -Not -BeNullOrEmpty
    }

    It 'Decodes ANSI from <Kind> input before redirecting it as plain text' -TestCases @(
        @{ Kind = 'strings' }
        @{ Kind = 'formatted objects' }
    ) {
        param($Kind)
        $check = {
            param($moduleFile, $kind)
            Import-Module $moduleFile -ErrorAction Stop
            $PSStyle.OutputRendering = 'Ansi'
            $colored = "`e[38;2;12;34;56mColoredValue`e[0m"
            if ($kind -eq 'strings') { $colored | Out-Page }
            else { [pscustomobject]@{ Label = $colored; Count = 7 } | Out-Page }
        }
        $moduleFile = Join-Path $PSScriptRoot '../output/TextMate.psd1'
        $output = & (Join-Path $PSHOME ($IsWindows ? 'pwsh.exe' : 'pwsh')) -NoProfile -NonInteractive -Command $check -args $moduleFile, $Kind
        $LASTEXITCODE | Should -Be 0
        $text = $output -join "`n"
        $text | Should -Match 'ColoredValue'
        $text | Should -Not -Match '[\u001b\u241b]|38;2;12;34;56'
        if ($Kind -eq 'formatted objects') { $text | Should -Match 'Label\s+Count' }
    }
}
