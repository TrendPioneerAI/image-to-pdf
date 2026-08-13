$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$fixtureRoot = Join-Path $testRoot 'fixtures'
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
Add-Type -AssemblyName System.Drawing

function Save-TestImage([string]$path, [int]$width, [int]$height, [System.Drawing.Color]$background, [string]$label, [string]$format) {
    $bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear($background)
            $graphics.DrawRectangle([System.Drawing.Pens]::DarkBlue, 10, 10, $width - 20, $height - 20)
            $graphics.DrawString($label, (New-Object System.Drawing.Font('Arial', 24)), [System.Drawing.Brushes]::Black, 25, 25)
        } finally { $graphics.Dispose() }
        $imageFormat = if ($format -eq 'png') { [System.Drawing.Imaging.ImageFormat]::Png } else { [System.Drawing.Imaging.ImageFormat]::Jpeg }
        $bitmap.Save($path, $imageFormat)
    } finally { $bitmap.Dispose() }
}

function Save-TransparentPng([string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap(1000, 1200, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.FillEllipse([System.Drawing.Brushes]::LightSkyBlue, 100, 100, 800, 800)
            $graphics.DrawString('PNG 03', (New-Object System.Drawing.Font('Arial', 24)), [System.Drawing.Brushes]::Black, 250, 500)
        } finally { $graphics.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

$cn = ([char]0x4e2d).ToString() + ([char]0x6587).ToString()
Save-TestImage (Join-Path $fixtureRoot ('01_' + $cn + '.jpg')) 900 1300 ([System.Drawing.Color]::White) 'Portrait 01' 'jpg'
Save-TestImage (Join-Path $fixtureRoot ('02_' + $cn + '.jpg')) 1400 900 ([System.Drawing.Color]::LightYellow) 'Landscape 02' 'jpg'
Save-TransparentPng (Join-Path $fixtureRoot ('03_' + $cn + '.png'))
Write-Host "Fixtures: $fixtureRoot"
