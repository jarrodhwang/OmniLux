$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$asset = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\OmniLux\Assets'
New-Item -ItemType Directory -Force -Path $asset | Out-Null
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $scale = $size / 64.0
    $graphics.ScaleTransform($scale,$scale)
    $ink = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#149F90'))
    $light = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#A3EEE0'))
    $pen = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#149F90')),3
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.FillEllipse($ink,10,10,44,44)
    $graphics.FillEllipse($light,21,21,22,22)
    foreach ($angle in @(0,45,90,135,180,225,270,315)) {
        $r = $angle * [Math]::PI / 180
        $graphics.DrawLine($pen,[single](32+26*[Math]::Cos($r)),[single](32+26*[Math]::Sin($r)),[single](32+29*[Math]::Cos($r)),[single](32+29*[Math]::Sin($r)))
    }
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{Size=$size;Data=$memory.ToArray()}
    if ($size -eq 256) { $bitmap.Save((Join-Path $asset 'OmniLux.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $pen.Dispose(); $ink.Dispose(); $light.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $asset 'OmniLux.ico'))
$writer = New-Object IO.BinaryWriter $output
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6+16*$images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) {0} else {$entry.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Data.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Data.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Data) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output (Join-Path $asset 'OmniLux.ico')
