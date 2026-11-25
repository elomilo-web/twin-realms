# Dev loop contact sheet: tiles screenshots into one small PNG so a whole capture costs one image to look at.
#   powershell -File tools/sheet.ps1                       -> all of current/ (4 columns of 320x180) -> .devloop/sheet.png
#   powershell -File tools/sheet.ps1 -names hud,village    -> only these (2 columns of 640x360)
#   powershell -File tools/sheet.ps1 -names hud -compare   -> previous (left) vs current (right), one row per name
param([string]$names = "", [switch]$compare, [string]$out = ".devloop/sheet.png")
Set-Location (Join-Path $PSScriptRoot "..")
Add-Type -AssemblyName System.Drawing
$cur = ".devloop/screenshots/current"; $prev = ".devloop/screenshots/previous"
$list = if ($names) { $names -split ',' } else { Get-ChildItem $cur -Filter *.png | Sort-Object Name | ForEach-Object { $_.BaseName } }
$tiles = @()
foreach ($n in $list) { if ($compare) { $tiles += , @("$prev/$n.png", "prev $n"); $tiles += , @("$cur/$n.png", "now $n") } else { $tiles += , @("$cur/$n.png", $n) } }
$cols = if ($compare -or $names) { 2 } else { 4 }; $w = if ($cols -eq 2) { 640 } else { 320 }; $h = [int]($w * 9 / 16)
$rows = [Math]::Ceiling($tiles.Count / $cols)
$bmp = New-Object Drawing.Bitmap ($w * $cols), ($h * $rows); $g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::DimGray)
$font = New-Object Drawing.Font "Arial", 10; $i = 0
foreach ($t in $tiles) {
  $x = ($i % $cols) * $w; $y = [Math]::Floor($i / $cols) * $h
  if (Test-Path $t[0]) { $im = [Drawing.Image]::FromFile((Resolve-Path $t[0])); $g.DrawImage($im, $x, $y, $w, $h); $im.Dispose() }
  $g.FillRectangle([Drawing.Brushes]::Black, $x, $y, 170, 16); $g.DrawString($t[1], $font, [Drawing.Brushes]::Yellow, $x + 2, $y + 1); $i++
}
$bmp.Save((Join-Path (Get-Location) $out)); $bmp.Dispose(); "SHEET: $out ($($tiles.Count) tiles)"
