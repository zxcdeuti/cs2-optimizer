# Renders the AppLogo drawing from Themes/Logo.xaml into Assets/app.ico (PNG-compressed, 16–256 px).
# Run with Windows PowerShell (STA): powershell -STA -File tools\make-icon.ps1
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot
$dict = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText("$root\Themes\Logo.xaml"))
$pngs = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $logo = if ($size -le 32) { $dict['AppLogoSmall'] } else { $dict['AppLogo'] }
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawImage($logo, (New-Object Windows.Rect 0, 0, $size, $size))
    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , @($size, $ms.ToArray())
}

New-Item -ItemType Directory -Force "$root\Assets" | Out-Null
$out = New-Object IO.BinaryWriter ([IO.File]::Create("$root\Assets\app.ico"))
$out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([uint16]1); $out.Write([uint16]32)
    $out.Write([uint32]$p[1].Length); $out.Write([uint32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $pngs) { $out.Write($p[1]) }
$out.Close()

# Large preview for the README / checking the look.
[IO.File]::WriteAllBytes("$root\Assets\app-256.png", $pngs[-1][1])
"app.ico: $($pngs.Count) sizes"
