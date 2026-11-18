param([string] $Directory = (Join-Path $PSScriptRoot '../assets/branding'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
$Directory = [IO.Path]::GetFullPath($Directory)
New-Item -ItemType Directory -Path $Directory -Force | Out-Null
$colors = @('#00E5BF', '#0085FF', '#8E6BFA', '#FF2D9A')
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$curve = 'M12 37 C22 40 25 42 33 36 C40 31 43 25 52 27'

function Write-Logo([string] $Name, [string] $Ink, [string[]] $Palette, [bool] $Wordmark) {
    $width = if ($Wordmark) { 243 } else { 64 }
    $svg = [Text.StringBuilder]::new()
    [void]$svg.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 $width 64' role='img' aria-label='KeyFlow Pads'>")
    [void]$svg.AppendLine("  <rect width='64' height='64' rx='16' fill='#10141D'/>")
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 4; $col++) {
            [void]$svg.AppendLine("  <rect x='$(9 + 12 * $col)' y='$(14 + 13 * $row)' width='10' height='10' rx='2.5' fill='$($Palette[$col])'/>")
        }
    }
    [void]$svg.AppendLine("  <path d='$curve' fill='none' stroke='#10141D' stroke-width='6.5' stroke-linecap='round'/>")
    [void]$svg.AppendLine("  <path d='$curve' fill='none' stroke='#F8FAFC' stroke-width='2.7' stroke-linecap='round'/>")
    if ($Wordmark) {
        [void]$svg.AppendLine("  <text x='77' y='42' font-family='Inter, sans-serif' font-size='25' font-weight='700' fill='$Ink'>KeyFlow Pads</text>")
    }
    [void]$svg.AppendLine('</svg>')
    [IO.File]::WriteAllText((Join-Path $Directory "$Name.svg"), $svg.ToString())
}

function New-RoundedRect([float] $X, [float] $Y, [float] $Size, [float] $Radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $Radius
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc(($X + $Size - $diameter), $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(($X + $Size - $diameter), ($Y + $Size - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($X, ($Y + $Size - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Paint-Icon([Drawing.Graphics] $Canvas, [float] $Scale) {
    $Canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $Canvas.ScaleTransform($Scale, $Scale)
    $paths = [Collections.Generic.List[IDisposable]]::new()
    try {
        $dark = [Drawing.ColorTranslator]::FromHtml('#10141D')
        $background = [Drawing.SolidBrush]::new($dark)
        $base = New-RoundedRect 0 0 64 16
        $paths.Add($background)
        $paths.Add($base)
        $Canvas.FillPath($background, $base)
        for ($col = 0; $col -lt 4; $col++) {
            $paint = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($colors[$col]))
            $paths.Add($paint)
            for ($row = 0; $row -lt 3; $row++) {
                $pad = New-RoundedRect (9 + 12 * $col) (14 + 13 * $row) 10 2.5
                $paths.Add($pad)
                $Canvas.FillPath($paint, $pad)
            }
        }
        $curvePath = [Drawing.Drawing2D.GraphicsPath]::new()
        $paths.Add($curvePath)
        $curvePath.AddBezier(12, 37, 22, 40, 25, 42, 33, 36)
        $curvePath.AddBezier(33, 36, 40, 31, 43, 25, 52, 27)
        foreach ($item in @(@($dark, 6.5), @([Drawing.ColorTranslator]::FromHtml('#F8FAFC'), 2.7))) {
            $line = [Drawing.Pen]::new($item[0], $item[1])
            $paths.Add($line)
            $line.StartCap = $line.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $line.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
            $Canvas.DrawPath($line, $curvePath)
        }
    } finally {
        foreach ($item in $paths) { $item.Dispose() }
    }
}

Write-Logo 'KeyFlowPads-Logo' '#F8FAFC' $colors $true
Write-Logo 'KeyFlowPads-Logo-Light' '#10141D' $colors $true
Write-Logo 'KeyFlowPads-Logo-Monochrome' '#10141D' @('#F8FAFC', '#F8FAFC', '#F8FAFC', '#F8FAFC') $true
Write-Logo 'KeyFlowPads-Icon' '#F8FAFC' $colors $false

$images = foreach ($size in $sizes) {
    $bmp = [Drawing.Bitmap]::new($size, $size)
    $canvas = [Drawing.Graphics]::FromImage($bmp)
    $stream = [IO.MemoryStream]::new()
    try {
        Paint-Icon $canvas ($size / 64.0)
        $bmp.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $data = $stream.ToArray()
        [IO.File]::WriteAllBytes((Join-Path $Directory "KeyFlowPads-Icon-$size.png"), $data)
        if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $Directory 'KeyFlowPads-Icon.png'), $data) }
        @{ Size = $size; Data = $data }
    } finally {
        $canvas.Dispose()
        $bmp.Dispose()
        $stream.Dispose()
    }
}

$ico = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $Directory 'KeyFlowPads-Icon.ico')))
try {
    $ico.Write([uint16]0)
    $ico.Write([uint16]1)
    $ico.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $ico.Write([byte]($image.Size % 256))
        $ico.Write([byte]($image.Size % 256))
        $ico.Write([uint16]0)
        $ico.Write([uint16]1)
        $ico.Write([uint16]32)
        $ico.Write([uint32]$image.Data.Length)
        $ico.Write([uint32]$offset)
        $offset += $image.Data.Length
    }
    foreach ($image in $images) { $ico.Write([byte[]]$image.Data) }
} finally { $ico.Dispose() }

$logo = [Drawing.Bitmap]::new(972, 256)
$canvas = [Drawing.Graphics]::FromImage($logo)
$fontFiles = [Drawing.Text.PrivateFontCollection]::new()
$fontFiles.AddFontFile([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets/fonts/Inter-Bold.ttf')))
$font = [Drawing.Font]::new($fontFiles.Families[0], 25, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
try {
    Paint-Icon $canvas 4
    $canvas.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $canvas.DrawString('KeyFlow Pads', $font, [Drawing.Brushes]::WhiteSmoke, 76, 17)
    $logo.Save((Join-Path $Directory 'KeyFlowPads-Logo.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $font.Dispose()
    $fontFiles.Dispose()
    $canvas.Dispose()
    $logo.Dispose()
}
