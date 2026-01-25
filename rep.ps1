Push-Location $PSScriptRoot
$f = & .\build.ps1
Import-Module PwshSpectreConsole
Import-Module ./output/PSTextMate.psd1
# $c = Get-Content ./tests/test-markdown.md -Raw
# $c | Show-TextMate -Verbose
# Get-Item ./tests/test-markdown.md | Show-TextMate -Verbose
Show-TextMate -Path ./tests/test-markdown.md #-Verbose
Show-TextMate -Path ./tests/test-markdown.md -Alternate #-Verbose
Pop-Location
