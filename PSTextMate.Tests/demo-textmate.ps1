#Requires -Modules 'PwshSpectreConsole'

Set-SpectreColors -AccentColor BlueViolet

# Build root layout scaffolding for:
# .--------------------------------.
# |            Header              |
# |--------------------------------|
# | File List | Preview            |
# |           |                    |
# |           |                    |
# |___________|____________________|
$layout = New-SpectreLayout -Name "root" -Rows @(
    # Row 1
    (
        New-SpectreLayout -Name "header" -MinimumSize 5 -Ratio 1 -Data ("empty")
    ),
    # Row 2
    (
        New-SpectreLayout -Name "content" -Ratio 10 -Columns @(
            (
                New-SpectreLayout -Name "filelist" -Ratio 2 -Data "empty"
            ),
            (
                New-SpectreLayout -Name "preview" -Ratio 4 -Data "empty"
            )
        )
    )
)

# Functions for rendering the content of each panel
function Get-TitlePanel {
    return "📁 File Browser - Spectre Live Demo [gray]$(Get-Date)[/]" | Format-SpectreAligned -HorizontalAlignment Center -VerticalAlignment Middle | Format-SpectrePanel -Expand
}

function Get-FileListPanel {
    param (
        $Files,
        $SelectedFile
    )
    $fileList = $Files | ForEach-Object {
        $name = $_.Name
        if ($_.Name -eq $SelectedFile.Name) {
            $name = "[darkviolet]$($name)[/]"
        }
        if ($_ -is [System.IO.DirectoryInfo]) {
            "📁 $name"
        } elseif ($_.Name -eq "..") {
            "🔼 $name"
        } else {
            "📄 $name"
        }
    } | Out-String
    return Format-SpectrePanel -Header "[white]File List[/]" -Data $fileList.Trim() -Expand
}

function Get-PreviewPanel {
    param (
        $SelectedFile
    )
    $item = Get-Item -Path $SelectedFile.FullName
    $result = ""
    if ($item -is [System.IO.DirectoryInfo]) {
        $result = "[grey]$($SelectedFile.Name) is a directory.[/]"
    } else {
        try {
            # $content = Get-Content -Path $item.FullName -Raw -ErrorAction Stop
            # $result = "[grey]$($content | Get-SpectreEscapedText)[/]"
            $result = Show-TextMate -Path $item.FullName
        } catch {
            $result = "[red]Error reading file content: $($_.Exception.Message | Get-SpectreEscapedText)[/]"
        }
    }
    return $result | Format-SpectrePanel -Header "[white]Preview[/]" -Expand
}

# Start live rendering the layout
Invoke-SpectreLive -Data $layout -ScriptBlock {
    param (
        $Context
    )

    # State
    $fileList = @(@{Name = ".."; Fullname = ".."}) + (Get-ChildItem)
    $selectedFile = $fileList[0]

    while ($true) {
        # Handle input
        $lastKeyPressed = $null
        while ([Console]::KeyAvailable) {
            $lastKeyPressed = [Console]::ReadKey($true)
        }
        if ($lastKeyPressed -ne $null) {
            if ($lastKeyPressed.Key -eq "DownArrow") {
                $selectedFile = $fileList[($fileList.IndexOf($selectedFile) + 1) % $fileList.Count]
            } elseif ($lastKeyPressed.Key -eq "UpArrow") {
                $selectedFile = $fileList[($fileList.IndexOf($selectedFile) - 1 + $fileList.Count) % $fileList.Count]
            } elseif ($lastKeyPressed.Key -eq "Enter") {
                if ($selectedFile -is [System.IO.DirectoryInfo] -or $selectedFile.Name -eq "..") {
                    $fileList = @(@{Name = ".."; Fullname = ".."}) + (Get-ChildItem -Path $selectedFile.FullName)
                    $selectedFile = $fileList[0]
                } else {
                    notepad $selectedFile.FullName
                    return
                }
            }
        }

        # Generate new data
        $titlePanel = Get-TitlePanel
        $fileListPanel = Get-FileListPanel -Files $fileList -SelectedFile $selectedFile
        $previewPanel = Get-PreviewPanel -SelectedFile $selectedFile

        # Update layout
        $layout["header"].Update($titlePanel) | Out-Null
        $layout["filelist"].Update($fileListPanel) | Out-Null
        $layout["preview"].Update($previewPanel) | Out-Null

        # Draw changes
        $Context.Refresh()
        Start-Sleep -Milliseconds 200
    }
}
