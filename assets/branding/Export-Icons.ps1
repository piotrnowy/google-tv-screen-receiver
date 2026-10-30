# Export platform sizes from the CompanyLogo-based P&K artwork. No design changes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$iconSource = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'pk-applications-icon.png'))
$bannerSource = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'pk-screen-receiver-banner.png'))

function Convert-PngSize {
    param([Drawing.Image]$Source, [int]$Width, [int]$Height)
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    $attributes = [Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $graphics.DrawImage($Source, [Drawing.Rectangle]::new(0, 0, $Width, $Height), 0, 0, $Source.Width, $Source.Height,
            [Drawing.GraphicsUnit]::Pixel, $attributes)
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally { $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}

try {
    $densities = [ordered]@{ mdpi = 48; hdpi = 72; xhdpi = 96; xxhdpi = 144; xxxhdpi = 192 }
    foreach ($density in $densities.GetEnumerator()) {
        $directory = Join-Path $projectRoot "app/src/main/res/mipmap-$($density.Key)"
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $bytes = Convert-PngSize -Source $iconSource -Width $density.Value -Height $density.Value
        [IO.File]::WriteAllBytes((Join-Path $directory 'ic_launcher_pk.png'), $bytes)
    }
    $bannerDirectory = Join-Path $projectRoot 'app/src/main/res/drawable-xhdpi'
    [IO.File]::WriteAllBytes((Join-Path $bannerDirectory 'tv_banner_pk.png'),
        (Convert-PngSize -Source $bannerSource -Width 320 -Height 180))

    # PNG-backed ICO frames are supported by Windows 11 and retain transparency.
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = @($sizes | ForEach-Object { Convert-PngSize -Source $iconSource -Width $_ -Height $_ })
    $icoStream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($icoStream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
            $writer.Write([byte]$sizeByte); $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
        $writer.Flush()
        [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'pk-applications.ico'), $icoStream.ToArray())
    } finally { $writer.Dispose(); $icoStream.Dispose() }
} finally { $iconSource.Dispose(); $bannerSource.Dispose() }

Write-Output 'Exported Android launcher icons, 320x180 TV banner and 10-size Windows ICO.'
