param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\launcher-icon.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\assets\launcher-icon.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(256, 128, 64, 48, 32, 16)
$sourceImage = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($Source))
$images = [Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            }
            finally { $graphics.Dispose() }

            $memory = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
                $images.Add($memory.ToArray())
            }
            finally { $memory.Dispose() }
        }
        finally { $bitmap.Dispose() }
    }
}
finally { $sourceImage.Dispose() }

$output = [IO.File]::Open([IO.Path]::GetFullPath($Destination), [IO.FileMode]::Create)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
    $output.Dispose()
}

Get-Item -LiteralPath $Destination
