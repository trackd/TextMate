#! /usr/bin/pwsh
#Requires -Version 7.4 -Module InvokeBuild
param(
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests
)
Write-Host "$($PSBoundParameters.GetEnumerator())" -ForegroundColor Cyan

$modulename = [System.IO.Path]::GetFileName($PSCommandPath) -replace '\.build\.ps1$'

$script:folders = @{
    ModuleName       = $modulename
    ProjectRoot      = $PSScriptRoot
    TempLib          = Join-Path $PSScriptRoot 'templib'
    SourcePath       = Join-Path $PSScriptRoot 'src'
    OutputPath       = Join-Path $PSScriptRoot 'output'
    DestinationPath  = Join-Path $PSScriptRoot 'output' 'lib'
    ModuleSourcePath = Join-Path $PSScriptRoot 'module'
    DocsPath         = Join-Path $PSScriptRoot 'docs' 'en-US'
    TestPath         = Join-Path $PSScriptRoot 'tests'
    CsprojPath       = Join-Path $PSScriptRoot 'src' "$modulename.csproj"
}

task Clean {
    if (Test-Path $folders.OutputPath) {
        Remove-Item -Path $folders.OutputPath -Recurse -Force -ErrorAction 'Ignore'
    }
    New-Item -Path $folders.OutputPath -ItemType Directory -Force | Out-Null
}

task Build {
    if (-not (Test-Path $folders.CsprojPath)) {
        Write-Warning 'C# project not found, skipping Build'
        return
    }
    try {
        Push-Location $folders.SourcePath

        # exec { dotnet publish $folders.CsprojPath --configuration $Configuration --nologo --verbosity minimal --output $folders.DestinationPath }
        exec { dotnet publish $folders.CsprojPath --configuration $Configuration --nologo --verbosity minimal --output $folders.TempLib }
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
        New-Item -Path $folders.outputPath -ItemType Directory -Force | Out-Null
        New-Item -Path $folders.DestinationPath -ItemType Directory -Force | Out-Null
        Get-ChildItem -Path (Join-Path $folders.TempLib 'runtimes' 'win-x64' 'native') -Filter *.dll | Move-Item -Destination $folders.DestinationPath -Force
        Get-ChildItem -Path (Join-Path $folders.TempLib 'runtimes' 'osx-arm64' 'native') -Filter *.dylib | Move-Item -Destination $folders.DestinationPath -Force
        Get-ChildItem -Path (Join-Path $folders.TempLib 'runtimes' 'linux-x64' 'native') -Filter *.so | Copy-Item -Destination $folders.DestinationPath -Force
        Move-Item (Join-Path $folders.TempLib 'PSTextMate.dll') -Destination $folders.OutputPath -Force
        Get-ChildItem "$($folders.TempLib)/*.dll" | Move-Item -Destination $folders.DestinationPath -Force
        if (Test-Path -Path $folders.TempLib -PathType Container) {
            Remove-Item -Path $folders.TempLib -Recurse -Force -ErrorAction 'Ignore'
        }
    }
    finally {
        Pop-Location
    }
}

task ModuleFiles {
    if (Test-Path $folders.ModuleSourcePath) {
        Get-ChildItem -Path $folders.ModuleSourcePath -File | Copy-Item -Destination $folders.OutputPath -Force
    }
    else {
        Write-Warning "Module directory not found at: $($folders.ModuleSourcePath)"
    }
}

task GenerateHelp -if (-not $SkipHelp) {
    if (-not (Test-Path $folders.DocsPath)) {
        Write-Warning "Documentation path not found at: $($folders.DocsPath)"
        return
    }
    if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS)) {
        Write-Host '    Installing Microsoft.PowerShell.PlatyPS...' -ForegroundColor Yellow
        Install-Module -Name Microsoft.PowerShell.PlatyPS -Scope CurrentUser -Force -AllowClobber
    }

    Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop

    $modulePath = Join-Path $folders.OutputPath ($folders.ModuleName + '.psd1')
    if (-not (Test-Path $modulePath)) {
        Write-Warning "Module manifest not found at: $modulePath. Skipping help generation."
        return
    }

    Import-Module $modulePath -Force

    $helpOutputPath = Join-Path $folders.OutputPath 'en-US'
    New-Item -Path $helpOutputPath -ItemType Directory -Force | Out-Null

    $allCommandHelp = Get-ChildItem -Path $folders.DocsPath -Filter '*.md' -Recurse -File |
        Where-Object { $_.Name -ne "$($folders.ModuleName).md" } |
        Import-MarkdownCommandHelp

    if ($allCommandHelp.Count -gt 0) {
        $tempOutputPath = Join-Path $helpOutputPath 'temp'
        Export-MamlCommandHelp -CommandHelp $allCommandHelp -OutputFolder $tempOutputPath -Force | Out-Null

        $generatedFile = Get-ChildItem -Path $tempOutputPath -Filter '*.xml' -Recurse -File | Select-Object -First 1
        if ($generatedFile) {
            Move-Item -Path $generatedFile.FullName -Destination $helpOutputPath -Force
        }
        Remove-Item -Path $tempOutputPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

task Test -if (-not $SkipTests) {
    if (-not (Test-Path $folders.TestPath)) {
        Write-Warning "Test directory not found at: $($folders.TestPath)"
        return
    }
    $ParentPath = Split-Path $folders.ProjectRoot -Parent
    Import-Module (Join-Path $ParentPath 'PwshSpectreConsole' 'output' 'PwshSpectreConsole.psd1')

    Import-Module (Join-Path $folders.OutputPath 'PSTextMate.psd1') -ErrorAction Stop
    Import-Module (Join-Path $folders.TestPath 'testhelper.psm1') -ErrorAction Stop

    $pesterConfig = New-PesterConfiguration
    # $pesterConfig.Output.Verbosity = 'Detailed'
    $pesterConfig.Run.Path = $folders.TestPath
    $pesterConfig.Run.Throw = $true
    $pesterConfig.Debug.WriteDebugMessages = $false
    Invoke-Pester -Configuration $pesterConfig
}
task CleanAfter {
    if ($script:config.DestinationPath -and (Test-Path $script:config.DestinationPath)) {
        Get-ChildItem $script:config.DestinationPath -File | Where-Object { $_.Extension -in '.pdb', '.json' } | Remove-Item -Force -ErrorAction Ignore
    }
}


task All -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter , Test
task BuildAndTest -Jobs Clean, Build, ModuleFiles, CleanAfter #, Test
