if (-not (Get-Module PSTextMate)) {
	# Import-Module (Join-Path $PSScriptRoot 'output' 'PSTextMate.psd1')
	$Path = Resolve-Path (Join-Path $PSScriptRoot '..')
	. (Join-Path $Path 'harness.ps1') -Load -Path $Path
}

$TestStrings = @(
	'Plain ASCII',
	"CJK: `u{4E2D}`u{6587}`u{65E5}`u{672C}`u{8A9E}",
	"Hangul: `u{D55C}`u{AE00}",
	"Emoji: `u{1F600}`u{1F64F}`u{1F680}",
	"Wide + ASCII: abc`u{4E2D}def`u{1F600}ghi",
	"Combining: a`u{0301} e`u{0301} n`u{0303}",
	"ZWJ: `u{1F469}`u{200D}`u{1F4BB}",
	"Flag: `u{1F1FA}`u{1F1F8}"
)

$AnsiTestStrings = @(
	"`e[31mRed`e[0m",
	"`e[32mGreen`e[0m `e[1mBold`e[0m",
	"`e[38;5;214mIndexed`e[0m",
	"`e[38;2;255;128;0mTrueColor`e[0m",
	"VT + Wide: `e[36m`u{4E2D}`u{6587}`e[0m",
	"OSC title: `e]0;PSTextMate Test`a",
	"CSI cursor move: start`e[2Cend"
	'{0}{1}First - {2}{3}Second - {4}{5}{6}Bold' -f $PSStyle.Foreground.Red, $PSStyle.Background.Green, $PSStyle.Foreground.Green, $psstyle.Background.Red, $PSStyle.Blink, $PSStyle.Background.Yellow, $PSStyle.Foreground.BrightCyan
	'{0}Hello{1}{2}{3}{1}{4}yep!' -f $PSStyle.Foreground.Red, $PSStyle.Reset, $PSStyle.Background.Magenta, $PSStyle.FormatHyperlink('world!', 'https://www.example.com'), [Char]::ConvertFromUtf32(128110)
	'{0}Hello{1}{2}{3} https://www.example.com' -f $PSStyle.Foreground.Red, $PSStyle.Reset, $PSStyle.Background.Magenta, $PSStyle.Reset
)

$TestStrings | Measure-String
$AnsiTestStrings| Measure-String
# $AnsiTestStrings| Measure-String -IgnoreVT
# @([string[]][System.Text.Rune[]]@(0x1F600..0x1F64F)) | Measure-String
# @([string[]][char[]]@(@(0xe0b0..0xe0d4) + @(0x2588..0x259b) + @(0x256d..0x2572))) | Measure-String
