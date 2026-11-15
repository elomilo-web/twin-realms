# Builds the foliage textures for the gothic trees from CC0 ambientCG atlases (Windows PowerShell, System.Drawing).
#   powershell -File tools/blender_gothic/foliage_tex.ps1 -Src <folder with LeafSet024 and LeafSet019 unzipped (1K-PNG)>
# Writes assets/textures/gothic/leaves_cluster.png (beech leaves on twigs, RGBA) and needles.png (fir sprays, RGBA).
param([string]$Src)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$out = Join-Path $root "assets\textures\gothic"

function Merge-Alpha([string]$colorPath, [string]$opacityPath) {
  # colour + opacity -> one 32-bit ARGB bitmap (fast, via LockBits)
  $c = New-Object Drawing.Bitmap $colorPath; $o = New-Object Drawing.Bitmap $opacityPath
  $w = $c.Width; $h = $c.Height
  $res = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $rect = New-Object Drawing.Rectangle 0, 0, $w, $h
  $c2 = $c.Clone($rect, [Drawing.Imaging.PixelFormat]::Format32bppArgb); $o2 = $o.Clone($rect, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $dc = $c2.LockBits($rect, 'ReadOnly', $c2.PixelFormat); $do = $o2.LockBits($rect, 'ReadOnly', $o2.PixelFormat); $dr = $res.LockBits($rect, 'WriteOnly', $res.PixelFormat)
  $n = $w * $h * 4
  $bc = New-Object byte[] $n; $bo = New-Object byte[] $n
  [Runtime.InteropServices.Marshal]::Copy($dc.Scan0, $bc, 0, $n); [Runtime.InteropServices.Marshal]::Copy($do.Scan0, $bo, 0, $n)
  for ($i = 0; $i -lt $n; $i += 4) { $bc[$i + 3] = $bo[$i + 2] }
  [Runtime.InteropServices.Marshal]::Copy($bc, 0, $dr.Scan0, $n)
  $c2.UnlockBits($dc); $o2.UnlockBits($do); $res.UnlockBits($dr)
  $c.Dispose(); $o.Dispose(); $c2.Dispose(); $o2.Dispose()
  return $res
}

# ---- broadleaf cluster: leaves from LeafSet024 (3x3 cells) along a few twigs
$atlas = Merge-Alpha (Join-Path $Src "LeafSet024\LeafSet024_1K-PNG_Color.png") (Join-Path $Src "LeafSet024\LeafSet024_1K-PNG_Opacity.png")
$cell = [int]($atlas.Width / 3)
$sprites = @(); for ($ci = 0; $ci -lt 9; $ci++) { $sprites += $atlas.Clone((New-Object Drawing.Rectangle (($ci % 3) * $cell), ([int][Math]::Floor($ci / 3) * $cell), $cell, $cell), [Drawing.Imaging.PixelFormat]::Format32bppArgb) }
$S = 1024
$bmp = New-Object Drawing.Bitmap $S, $S, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::Transparent)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
$rnd = New-Object Random 7
$twig = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 70, 52, 38)), 6
$twig.StartCap = 'Round'; $twig.EndCap = 'Round'
for ($t = 0; $t -lt 5; $t++) {
  # each twig grows from the bottom centre outwards and up
  $a = -150 + $t * 30 + $rnd.Next(-8, 8); $rad = $a * [Math]::PI / 180
  $x0 = $S / 2 + $rnd.Next(-40, 40); $y0 = $S - 30
  $len = 700 + $rnd.Next(0, 200)
  $pts = New-Object 'Drawing.PointF[]' 11
  for ($k = 0; $k -le 10; $k++) {
    $u = $k / 10; $bend = [Math]::Sin($u * 2.4) * 60
    $pts[$k] = [Drawing.PointF]::new([float]($x0 + [Math]::Cos($rad) * $len * $u * 0.55 + $bend), [float]($y0 + [Math]::Sin($rad) * $len * $u))
  }
  $twig.Width = 7; $g.DrawCurve($twig, $pts)
  for ($k = 2; $k -le 10; $k++) {
    $p = $pts[$k]
    for ($side = -1; $side -le 1; $side += 2) {
      $sprite = $sprites[$rnd.Next(0, 9)]
      $sz = [float](150 + $rnd.Next(0, 70) - $k * 4)
      $ang = $a + 90 + $side * (35 + $rnd.Next(0, 30))
      $g.TranslateTransform($p.X, $p.Y); $g.RotateTransform($ang)
      $g.DrawImage($sprite, [float](-$sz / 2), [float](-$sz * 0.05), $sz, $sz)
      $g.ResetTransform()
    }
  }
}
$g.Dispose()
$bmp.Save((Join-Path $out "leaves_cluster.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $atlas.Dispose()

# ---- fir sprays: LeafSet019 as is (three sprays across), with alpha
$needles = Merge-Alpha (Join-Path $Src "LeafSet019\LeafSet019_1K-PNG_Color.png") (Join-Path $Src "LeafSet019\LeafSet019_1K-PNG_Opacity.png")
$needles.Save((Join-Path $out "needles.png"), [Drawing.Imaging.ImageFormat]::Png)
# rows of the three sprays (for the card UVs): print the alpha extent per band
$rect = New-Object Drawing.Rectangle 0, 0, $needles.Width, $needles.Height
$d = $needles.LockBits($rect, 'ReadOnly', $needles.PixelFormat); $n = $needles.Width * $needles.Height * 4
$b = New-Object byte[] $n; [Runtime.InteropServices.Marshal]::Copy($d.Scan0, $b, 0, $n); $needles.UnlockBits($d)
$W = $needles.Width; $rows = @()
for ($y = 0; $y -lt $needles.Height; $y += 4) { $c = 0; for ($x = 0; $x -lt $W; $x += 8) { if ($b[($y * $W + $x) * 4 + 3] -gt 60) { $c++ } }; $rows += $c }
$bands = @(); $in = $false; for ($i = 0; $i -lt $rows.Count; $i++) { if (-not $in -and $rows[$i] -gt 2) { $in = $true; $s0 = $i } elseif ($in -and $rows[$i] -le 2) { $in = $false; $bands += "{0:N3}-{1:N3}" -f ($s0 * 4 / $needles.Height), ($i * 4 / $needles.Height) } }
"needle bands (v from top): " + ($bands -join ", ")
$needles.Dispose()
