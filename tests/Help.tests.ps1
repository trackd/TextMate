BeforeDiscovery {
    Import-Module (Join-Path $PSScriptRoot '..' 'output' 'TextMate.psd1') -ErrorAction Stop
    $commands = Get-Command -Module TextMate -CommandType Cmdlet | ForEach-Object { @{ Name = $_.Name } }
}

Describe 'Help for <Name>' -ForEach $commands {
    BeforeAll {
        $command = Get-Command -Name $Name -Module TextMate
        $help = Get-Help -Name $Name -Full
        $common = [Management.Automation.PSCmdlet]::CommonParameters + [Management.Automation.PSCmdlet]::OptionalCommonParameters
        $parameters = $command.Parameters.Keys | Where-Object {
            $_ -notin $common -and -not ($command.Parameters[$_].Attributes | Where-Object {
                $_ -is [Management.Automation.ParameterAttribute] -and $_.DontShow
            })
        }
    }

    It 'Has authored synopsis and description' {
        $help.Synopsis | Should -Not -BeNullOrEmpty
        $help.Synopsis.Trim() | Should -Not -Match "^$([regex]::Escape($Name))(\s|$)"
        ($help.Description.Text -join '') | Should -Not -BeNullOrEmpty
    }

    It 'Has at least two explained examples' {
        $examples = @($help.Examples.Example)
        $examples.Count | Should -BeGreaterOrEqual 2
        foreach ($example in $examples) {
            $example.Code | Should -Not -BeNullOrEmpty
            $explained = ($example.Remarks.Text -join '').Trim() -or ($example.Code -split '\r?\n').Count -gt 1
            $explained | Should -BeTrue
        }
    }

    It 'Documents every visible public parameter' {
        foreach ($parameterName in $parameters) {
            $entry = $help.Parameters.Parameter | Where-Object Name -EQ $parameterName
            $entry | Should -Not -BeNullOrEmpty -Because "$Name exposes $parameterName"
            ($entry.Description.Text -join '').Trim() | Should -Not -BeNullOrEmpty
        }
    }
}
