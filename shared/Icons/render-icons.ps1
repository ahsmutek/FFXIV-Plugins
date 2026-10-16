# Renders Tabler Icons (outline) to white PNGs that the plugins tint when drawing.
# Run with Windows PowerShell; pass icon names to add more, e.g. .\render-icons.ps1 moneybag
param([string[]]$Names = @('arrow-back-up', 'hanger', 'moneybag-minus', 'moneybag-plus', 'settings', 'trash', 'x'))

Add-Type -AssemblyName PresentationCore, WindowsBase

$version = '3.49.0'
$size = 64
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svgDir = Join-Path $dir 'svg'
New-Item -ItemType Directory -Force $svgDir | Out-Null

foreach ($name in $Names) {
    $svgPath = Join-Path $svgDir "$name.svg"
    if (-not (Test-Path $svgPath)) {
        Invoke-WebRequest "https://cdn.jsdelivr.net/npm/@tabler/icons@$version/icons/outline/$name.svg" -OutFile $svgPath -UseBasicParsing
    }
    [xml]$svg = Get-Content $svgPath -Raw

    $pen = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::White), 2
    $pen.StartLineCap = 'Round'
    $pen.EndLineCap = 'Round'
    $pen.LineJoin = 'Round'

    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 24), ($size / 24)))
    foreach ($path in $svg.svg.path) {
        # Each icon starts with an invisible 24x24 bounding box.
        if ($path.stroke -eq 'none') { continue }
        $context.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse($path.d))
    }
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create((Join-Path $dir "$name.png"))
    $encoder.Save($stream)
    $stream.Close()
    Write-Output "$name.png"
}
