Add-Type -AssemblyName System.Drawing

# Create a 256x256 bitmap
$bmp = New-Object System.Drawing.Bitmap(256, 256)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAlias'

# Dark background
$g.Clear([System.Drawing.Color]::FromArgb(30, 30, 46))

# Create gradient brush for circle (blue to purple)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    [System.Drawing.Point]::new(0, 0),
    [System.Drawing.Point]::new(256, 256),
    [System.Drawing.Color]::FromArgb(0, 150, 255),
    [System.Drawing.Color]::FromArgb(138, 43, 226)
)

# Draw filled circle
$g.FillEllipse($brush, 15, 15, 226, 226)

# Add subtle glow/border
$glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(100, 255, 255, 255), 2)
$g.DrawEllipse($glowPen, 15, 15, 226, 226)

# Draw the "R" letter
$font = New-Object System.Drawing.Font('Segoe UI', 130, [System.Drawing.FontStyle]::Bold)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = 'Center'
$sf.LineAlignment = 'Center'

# White text with slight shadow
$shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
$g.DrawString('R', $font, $shadowBrush, 132, 135, $sf)
$g.DrawString('R', $font, [System.Drawing.Brushes]::White, 128, 130, $sf)

# Save as PNG
$bmp.Save("$PSScriptRoot\app.png", [System.Drawing.Imaging.ImageFormat]::Png)

# Cleanup
$g.Dispose()
$bmp.Dispose()
$brush.Dispose()
$glowPen.Dispose()
$shadowBrush.Dispose()
$font.Dispose()

Write-Host "Icon PNG created at $PSScriptRoot\app.png"

# Now create ICO file with multiple sizes
$sizes = @(16, 32, 48, 256)
$iconPath = "$PSScriptRoot\app.ico"

# Load the PNG
$sourceBmp = New-Object System.Drawing.Bitmap("$PSScriptRoot\app.png")

# Create memory stream for ICO
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICO header
$bw.Write([Int16]0) # Reserved
$bw.Write([Int16]1) # Type (1 = ICO)
$bw.Write([Int16]$sizes.Count) # Number of images

# Calculate offset (header = 6 bytes, entries = 16 bytes each)
$offset = 6 + ($sizes.Count * 16)
$imageData = @()

foreach ($size in $sizes) {
    # Create resized bitmap
    $resized = New-Object System.Drawing.Bitmap($size, $size)
    $gResize = [System.Drawing.Graphics]::FromImage($resized)
    $gResize.SmoothingMode = 'HighQuality'
    $gResize.InterpolationMode = 'HighQualityBicubic'
    $gResize.DrawImage($sourceBmp, 0, 0, $size, $size)
    
    # Convert to PNG bytes
    $pngMs = New-Object System.IO.MemoryStream
    $resized.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngMs.ToArray()
    $imageData += ,@($size, $pngBytes)
    
    $gResize.Dispose()
    $resized.Dispose()
    $pngMs.Dispose()
}

# Write directory entries
foreach ($data in $imageData) {
    $size = $data[0]
    $bytes = $data[1]
    
    $bw.Write([Byte]$(if ($size -eq 256) { 0 } else { $size })) # Width
    $bw.Write([Byte]$(if ($size -eq 256) { 0 } else { $size })) # Height
    $bw.Write([Byte]0) # Color palette
    $bw.Write([Byte]0) # Reserved
    $bw.Write([Int16]1) # Color planes
    $bw.Write([Int16]32) # Bits per pixel
    $bw.Write([Int32]$bytes.Length) # Image size
    $bw.Write([Int32]$offset) # Offset
    
    $offset += $bytes.Length
}

# Write image data
foreach ($data in $imageData) {
    $bw.Write($data[1])
}

# Save ICO file
$bw.Flush()
[System.IO.File]::WriteAllBytes($iconPath, $ms.ToArray())

$bw.Dispose()
$ms.Dispose()
$sourceBmp.Dispose()

Write-Host "Icon ICO created at $iconPath"