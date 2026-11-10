$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$paths = @()
foreach ($size in $sizes) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $rect = New-Object Drawing.Rectangle 0,0,$size,$size
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush $rect,([Drawing.Color]::FromArgb(218,126,85)),([Drawing.Color]::FromArgb(139,65,72)),35
    $g.FillRectangle($brush,$rect)
    $glow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(30,255,255,255))
    $g.FillEllipse($glow, -$size/5, -$size/3, $size, $size)
    $ink = [Drawing.Brushes]::White
    $font = [Drawing.Font]::new('Microsoft YaHei UI', [single]($size * 0.43), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $g.DrawString(([char]0x6587).ToString(),$font,$ink,(New-Object Drawing.RectangleF 0,($size*0.04),$size,($size*0.64)),$format)
    $small = [Drawing.Font]::new('Segoe UI', [single]($size * 0.12), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $g.DrawString((('A / ' + [char]0x4E2D)),$small,$ink,(New-Object Drawing.RectangleF 0,($size*0.69),$size,($size*0.25)),$format)
    $path = Join-Path $outDir "icon-$size.png"
    $bmp.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    $paths += $path
    $font.Dispose();$small.Dispose();$format.Dispose();$brush.Dispose();$glow.Dispose();$g.Dispose();$bmp.Dispose()
}
& ffmpeg -y -loglevel error -i $paths[-1] -vf "scale=256:256" (Join-Path $outDir 'icon-preview.png')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# Multi-size PNG-backed ICO for desktop and taskbar.
$bytes = [Collections.Generic.List[byte]]::new()
$bytes.AddRange([BitConverter]::GetBytes([uint16]0))
$bytes.AddRange([BitConverter]::GetBytes([uint16]1))
$bytes.AddRange([BitConverter]::GetBytes([uint16]$sizes.Count))
$offset = 6 + 16 * $sizes.Count
for ($i=0;$i -lt $sizes.Count;$i++) {
    $data = [IO.File]::ReadAllBytes($paths[$i])
    $size = $sizes[$i]
    $bytes.Add([byte]($size -band 255));$bytes.Add([byte]($size -band 255))
    $bytes.Add(0);$bytes.Add(0)
    $bytes.AddRange([BitConverter]::GetBytes([uint16]1))
    $bytes.AddRange([BitConverter]::GetBytes([uint16]32))
    $bytes.AddRange([BitConverter]::GetBytes([uint32]$data.Length))
    $bytes.AddRange([BitConverter]::GetBytes([uint32]$offset))
    $offset += $data.Length
}
foreach ($path in $paths) { $bytes.AddRange([IO.File]::ReadAllBytes($path)) }
[IO.File]::WriteAllBytes((Join-Path $outDir 'ClaudeDesktop-ZH-CN.ico'),$bytes.ToArray())
Get-Item (Join-Path $outDir 'ClaudeDesktop-ZH-CN.ico') | Select-Object FullName,Length
