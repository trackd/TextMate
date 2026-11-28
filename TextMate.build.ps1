#! /usr/bin/pwsh
#Requires -Version 7.6 -Module InvokeBuild
param(
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests,
    [ValidateSet('Spectre', 'Fullscreen', 'XenoAtom')]
    [string[]]$Renderer = @()
)

Write-Host "$($PSBoundParameters.GetEnumerator())" -ForegroundColor Cyan

$modulename = [System.IO.Path]::GetFileName($PSCommandPath) -replace '\.build\.ps1$'
# $modulename = 'PSTextMate'

$script:folders = @{
    ModuleName       = $modulename
    ProjectRoot      = $PSScriptRoot
    TempLib          = Join-Path $PSScriptRoot 'templib'
    OutputPath       = Join-Path $PSScriptRoot 'output'
    DestinationPath  = Join-Path $PSScriptRoot 'output' 'lib'
    ModuleSourcePath = Join-Path $PSScriptRoot 'Module'
    DocsPath         = Join-Path $PSScriptRoot 'docs' 'en-us'
    TestPath         = Join-Path $PSScriptRoot 'tests'
    CsprojPath       = Join-Path $PSScriptRoot 'src' 'PSTextMate' 'PSTextMate.csproj'
}

task Clean {
    if (Test-Path $folders.OutputPath) {
        $resolvedOutput = [System.IO.Path]::GetFullPath($folders.OutputPath)
        $expectedOutput = [System.IO.Path]::Combine($PSScriptRoot, 'output')
        if ($resolvedOutput -ne $expectedOutput) {
            throw "Refusing to clean unexpected output path '$resolvedOutput'."
        }
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force -ErrorAction Stop
    }
    New-Item -Path $folders.OutputPath -ItemType Directory -Force | Out-Null
}


task Build {
    if (-not (Test-Path $folders.CsprojPath)) {
        Write-Warning 'C# project not found, skipping Build'
        return
    }
    exec {
        dotnet publish $folders.CsprojPath --configuration $Configuration --nologo --verbosity minimal --output $folders.DestinationPath
    }
    $null = New-Item -Path $folders.outputPath -ItemType Directory -Force
    $markdownProject = Join-Path $PSScriptRoot 'src' 'PSTextMate.Markdown' 'PSTextMate.Markdown.csproj'
    $markdownOutput = Join-Path $folders.DestinationPath 'presentation' 'Markdown'
    exec {
        dotnet publish $markdownProject --configuration $Configuration --nologo --verbosity minimal --output $markdownOutput
    }
    $extensionOutputs = @($markdownOutput)
    # Lightweight optional-module bridge; no image codecs are shipped.
    $imageProject = Join-Path $PSScriptRoot 'src' 'PSTextMate.Images' 'PSTextMate.Images.csproj'
    $imageOutput = Join-Path $folders.DestinationPath 'presentation' 'Images'
    exec {
        dotnet publish $imageProject --configuration $Configuration --nologo --verbosity minimal --output $imageOutput
    }
    $extensionOutputs += $imageOutput
    foreach ($rendererName in @('Spectre', 'XenoAtom')) {
        $rendererProject = Join-Path $PSScriptRoot 'src' "PSTextMate.$rendererName" "PSTextMate.$rendererName.csproj"
        $rendererOutput = Join-Path $folders.DestinationPath 'renderers' $rendererName
        exec {
            dotnet publish $rendererProject --configuration $Configuration --nologo --verbosity minimal --output $rendererOutput
        }
        if ($Renderer -notcontains $rendererName -and -not ($rendererName -eq 'XenoAtom' -and $Renderer -contains 'Fullscreen')) {
            # Always ship TextMate's small adapters; consumer modules provide optional UI runtimes.
            Get-ChildItem -LiteralPath $rendererOutput -Filter '*.dll' -File |
                Where-Object Name -ne "PSTextMate.$rendererName.dll" |
                Remove-Item -Force -ErrorAction Stop
        }
        $extensionOutputs += $rendererOutput
    }
    foreach ($extensionOutput in $extensionOutputs) {
        $sharedNames = @('PSTextMate.dll', 'PSTextMate.ALC.dll', 'Tint.dll', 'Tint.Grammars.dll', 'Tint.Themes.dll', 'GregsEx.dll')
        foreach ($sharedName in $sharedNames) {
            $sharedPath = Join-Path $extensionOutput $sharedName
            if (Test-Path -LiteralPath $sharedPath) {
                Remove-Item -LiteralPath $sharedPath -Force -ErrorAction Stop
            }
        }
        foreach ($sharedDirectory in @('themes', 'grammars')) {
            $sharedPath = [System.IO.Path]::GetFullPath((Join-Path $extensionOutput $sharedDirectory))
            $rendererRoot = [System.IO.Path]::GetFullPath($extensionOutput) + [System.IO.Path]::DirectorySeparatorChar
            if (-not $sharedPath.StartsWith($rendererRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove unexpected shared-assets path '$sharedPath'."
            }
            if (Test-Path -LiteralPath $sharedPath -PathType Container) {
                Remove-Item -LiteralPath $sharedPath -Recurse -Force -ErrorAction Stop
            }
        }
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
    if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS | Where-Object Version -EQ ([version]'1.0.1'))) {
        Write-Host '    Installing Microsoft.PowerShell.PlatyPS...' -ForegroundColor Yellow
        Install-Module -Name Microsoft.PowerShell.PlatyPS -RequiredVersion 1.0.1 -Scope CurrentUser -Force -AllowClobber
    }

    Import-Module Microsoft.PowerShell.PlatyPS -RequiredVersion 1.0.1 -ErrorAction Stop

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
            # PlatyPS 1.0 emits fenced example code in introduction paragraphs, leaving dev:code empty.
            # Populate standard MAML code/remarks so Get-Help exposes executable examples correctly.
            $maml = [xml][IO.File]::ReadAllText($generatedFile.FullName)
            foreach ($example in $maml.SelectNodes('//*[local-name()="example"]')) {
                $codeNode = $example.SelectSingleNode('*[local-name()="code"]')
                $introduction = $example.SelectSingleNode('*[local-name()="introduction"]')
                $remarks = $example.SelectSingleNode('*[local-name()="remarks"]')
                if (-not $codeNode -or -not $introduction -or $codeNode.InnerText.Trim()) { continue }
                $text = ($introduction.ChildNodes | ForEach-Object InnerText) -join "`n"
                $fences = [regex]::Matches($text, '(?ms)^```[^\r\n]*\r?\n(.*?)^```[ \t]*\r?$')
                if ($fences.Count -eq 0) { continue }
                $codeNode.InnerText = (($fences | ForEach-Object { $_.Groups[1].Value.TrimEnd() }) -join "`n`n")
                $explanation = [regex]::Replace($text, '(?ms)^```[^\r\n]*\r?\n(.*?)^```[ \t]*\r?$', '')
                $explanation = $explanation.Replace([char]0x80, ' ').Trim()
                $remarks.RemoveAll()
                if ($explanation) {
                    $paragraph = $maml.CreateElement('maml', 'para', $introduction.NamespaceURI)
                    $paragraph.InnerText = $explanation
                    $null = $remarks.AppendChild($paragraph)
                }
                $introduction.RemoveAll()
            }
            $maml.Save($generatedFile.FullName)
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

    Import-Module (Join-Path $folders.OutputPath ($folders.ModuleName + '.psd1')) -ErrorAction Stop
    Import-Module (Join-Path $folders.TestPath 'testhelper.psm1') -ErrorAction Stop

    Import-Module Pester -RequiredVersion 5.7.1 -ErrorAction Stop
    $pesterConfig = New-PesterConfiguration
    # $pesterConfig.Output.Verbosity = 'Detailed'
    $pesterConfig.Run.Path = $folders.TestPath
    if ($Renderer -notcontains 'Spectre') {
        # Suites tagged 'Adapter' need the bundled Spectre runtime; every other suite always runs.
        $pesterConfig.Filter.ExcludeTag = 'Adapter'
    }
    $pesterConfig.Run.Throw = $true
    $pesterConfig.Run.Exit = $false
    $pesterConfig.Run.PassThru = $true
    if ($env:CI) {
        $pesterConfig.TestResult.Enabled = $true
        $pesterConfig.TestResult.OutputPath = Join-Path $PSScriptRoot 'TestResults' 'pester.xml'
    }
    $pesterConfig.Debug.WriteDebugMessages = $false
    $result = Invoke-Pester -Configuration $pesterConfig
    if ($result.TotalCount -eq 0) {
        throw 'No PowerShell tests were discovered.'
    }
}

task DotNetTest -if (-not $SkipTests) {
    exec {
        dotnet test (Join-Path $PSScriptRoot 'tests' 'PSTextMate.CoreTests' 'PSTextMate.CoreTests.csproj') --configuration $Configuration --nologo --logger trx --results-directory (Join-Path $PSScriptRoot 'TestResults')
    }
    if ($Renderer -contains 'Spectre') {
        exec {
            dotnet test (Join-Path $PSScriptRoot 'tests' 'PSTextMate.InteractiveTests' 'PSTextMate.InteractiveTests.csproj') --configuration $Configuration --nologo --logger trx --results-directory (Join-Path $PSScriptRoot 'TestResults')
        }
    }
}

task CleanAfter {
    if ($script:folders.DestinationPath -and (Test-Path $script:folders.DestinationPath)) {
        Get-ChildItem $script:folders.DestinationPath -File -Recurse |
            Where-Object { $_.Extension -eq '.pdb' } |
            Remove-Item -Force -ErrorAction Stop
    }
}


task All -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter, Test, DotNetTest
task BuildAndTest -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter, Test, DotNetTest
task BuildModule -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter
