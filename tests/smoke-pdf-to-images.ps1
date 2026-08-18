$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exeName = ([char]0x56fe).ToString() + ([char]0x7247).ToString() + ([char]0x8f6c).ToString() + 'PDF.exe'
$guideName = ([char]0x5feb).ToString() + ([char]0x901f).ToString() + ([char]0x4f7f).ToString() + ([char]0x7528).ToString() + ([char]0x6307).ToString() + ([char]0x5357).ToString() + '.pdf'
$exe = Join-Path $projectRoot ('dist\' + $exeName)
$inputPdf = Join-Path $projectRoot ('dist\' + $guideName)
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $projectRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath $inputPdf)) { throw 'The quick-start PDF in dist is required for this smoke test.' }

$outputRoot = Join-Path $projectRoot ('tests\out\pdf-to-images-' + [Guid]::NewGuid().ToString('N'))
$pngOutput = Join-Path $outputRoot 'png'
$jpgOutput = Join-Path $outputRoot 'jpg'
$invalidOutput = Join-Path $outputRoot 'invalid'
New-Item -ItemType Directory -Force -Path $pngOutput, $jpgOutput, $invalidOutput | Out-Null

function Quote-Argument([string]$value) {
    return '"' + $value.Replace('"', '\"') + '"'
}

function Run-Converter([string]$output, [string]$format, [int]$dpi, [string]$pages) {
    $arguments = @(
        '--pdf-to-images',
        (Quote-Argument $inputPdf),
        (Quote-Argument $output),
        $format,
        $dpi.ToString(),
        (Quote-Argument $pages)
    )
    return Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
}

$pngProcess = Run-Converter $pngOutput 'png' 150 'all'
if ($pngProcess.ExitCode -ne 0) { throw "PNG conversion failed with exit code $($pngProcess.ExitCode)." }
$pngFiles = @(Get-ChildItem -LiteralPath $pngOutput -Filter '*.png' -File | Sort-Object Name)
if ($pngFiles.Count -ne 4) { throw "Expected 4 PNG files, found $($pngFiles.Count)." }

$jpgProcess = Run-Converter $jpgOutput 'jpg' 220 '2'
if ($jpgProcess.ExitCode -ne 0) { throw "JPEG conversion failed with exit code $($jpgProcess.ExitCode)." }
$jpgFiles = @(Get-ChildItem -LiteralPath $jpgOutput -Filter '*.jpg' -File)
if ($jpgFiles.Count -ne 1) { throw "Expected 1 JPEG file, found $($jpgFiles.Count)." }
if ($jpgFiles[0].Name -notlike '*002*.jpg') { throw "Unexpected JPEG page name: $($jpgFiles[0].Name)" }

Add-Type -AssemblyName System.Drawing
$pngImage = [System.Drawing.Image]::FromFile($pngFiles[0].FullName)
$jpgImage = [System.Drawing.Image]::FromFile($jpgFiles[0].FullName)
try {
    if ($pngImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'PNG signature check failed.' }
    if ($jpgImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Jpeg.Guid) { throw 'JPEG signature check failed.' }
    if ($pngImage.Width -lt 1000 -or $pngImage.Height -lt 1400) { throw '150 DPI output dimensions are unexpectedly small.' }
    if ($jpgImage.Width -le $pngImage.Width -or $jpgImage.Height -le $pngImage.Height) { throw '220 DPI output should be larger than 150 DPI output.' }
}
finally {
    $pngImage.Dispose()
    $jpgImage.Dispose()
}

$repeatProcess = Run-Converter $jpgOutput 'jpg' 220 '2'
if ($repeatProcess.ExitCode -ne 0) { throw "Repeated conversion failed with exit code $($repeatProcess.ExitCode)." }
$repeatedFiles = @(Get-ChildItem -LiteralPath $jpgOutput -Filter '*.jpg' -File)
if ($repeatedFiles.Count -ne 2 -or -not ($repeatedFiles.Name -match '\(2\)\.jpg$')) { throw 'Same-name collision handling did not append (2).' }

$invalidProcess = Run-Converter $invalidOutput 'png' 150 '99'
if ($invalidProcess.ExitCode -eq 0) { throw 'Out-of-range page selection should fail.' }
if (@(Get-ChildItem -LiteralPath $invalidOutput -File).Count -ne 0) { throw 'Invalid page selection left unexpected output files.' }

$temporaryFiles = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Where-Object { $_.Name -like '.pdf-render-*' })
if ($temporaryFiles.Count -ne 0) { throw 'Temporary render files were not cleaned up.' }

[pscustomobject]@{
    Result = 'PASS'
    PngPages = $pngFiles.Count
    JpegPages = $repeatedFiles.Count
    OutputRoot = $outputRoot
} | Format-List
