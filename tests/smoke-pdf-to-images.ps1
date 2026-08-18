$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exeName = ([char]0x56fe).ToString() + ([char]0x7247).ToString() + ([char]0x8f6c).ToString() + 'PDF.exe'
$guideName = ([char]0x5feb).ToString() + ([char]0x901f).ToString() + ([char]0x4f7f).ToString() + ([char]0x7528).ToString() + ([char]0x6307).ToString() + ([char]0x5357).ToString() + '.pdf'
$exe = Join-Path $projectRoot ('dist\' + $exeName)
$inputPdf = Join-Path $projectRoot ('dist\' + $guideName)
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $projectRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath $inputPdf)) { throw 'The quick-start PDF in dist is required for this smoke test.' }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$assembly = [Reflection.Assembly]::LoadFile($exe)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$formType = $assembly.GetType('LocalImageToPdf.PdfToImageForm', $true)
$constructor = $formType.GetConstructor($flags, $null, [Type[]]@([Collections.Generic.IEnumerable[string]], [Drawing.Icon], [bool]), $null)
$constructorArguments = New-Object object[] 3
$constructorArguments[0] = [string[]]@()
$constructorArguments[1] = $null
$constructorArguments[2] = $true
$formatProbe = [Windows.Forms.Form]$constructor.Invoke($constructorArguments)
try {
    $formatCombo = [Windows.Forms.ComboBox]$formType.GetField('_formatCombo', $flags).GetValue($formatProbe)
    if ($formatCombo.Items.Count -ne 4) { throw "Expected 4 PDF image output formats, found $($formatCombo.Items.Count)." }
    if ($formatCombo.SelectedIndex -ne 0) { throw 'PNG is not the default PDF image output format.' }
    $namingModeCombo = [Windows.Forms.ComboBox]$formType.GetField('_namingModeCombo', $flags).GetValue($formatProbe)
    $customNameBox = [Windows.Forms.TextBox]$formType.GetField('_customNameBox', $flags).GetValue($formatProbe)
    if ($namingModeCombo.Items.Count -ne 2 -or $namingModeCombo.SelectedIndex -ne 0) { throw 'Default/custom naming selector is missing or has the wrong default.' }
    if ($customNameBox.Enabled) { throw 'Custom name box should be disabled in default naming mode.' }
    $namingModeCombo.SelectedIndex = 1
    if (-not $customNameBox.Enabled) { throw 'Custom name box was not enabled in custom naming mode.' }
}
finally {
    $formatProbe.Dispose()
}

$outputRoot = Join-Path $projectRoot ('tests\out\pdf-to-images-' + [Guid]::NewGuid().ToString('N'))
$pngOutput = Join-Path $outputRoot 'png'
$pngRepeatOutput = Join-Path $outputRoot 'png-repeat'
$jpgOutput = Join-Path $outputRoot 'jpg'
$bmpOutput = Join-Path $outputRoot 'bmp'
$tiffOutput = Join-Path $outputRoot 'tiff'
$multiOutput = Join-Path $outputRoot 'multi'
$customOutput = Join-Path $outputRoot 'custom'
$invalidOutput = Join-Path $outputRoot 'invalid'
New-Item -ItemType Directory -Force -Path $pngOutput, $pngRepeatOutput, $jpgOutput, $bmpOutput, $tiffOutput, $multiOutput, $customOutput, $invalidOutput | Out-Null
$inputBaseName = [System.IO.Path]::GetFileNameWithoutExtension($inputPdf)
$convertedSuffix = '-' + ([char]0x8f6c).ToString() + ([char]0x6362).ToString() + ([char]0x540e).ToString()
$expectedFolderName = $inputBaseName + $convertedSuffix

function Quote-Argument([string]$value) {
    return '"' + $value.Replace('"', '\"') + '"'
}

function Run-Converter([string]$source, [string]$output, [string]$format, [int]$dpi, [string]$pages, [string]$customName = '') {
    $arguments = @(
        '--pdf-to-images',
        (Quote-Argument $source),
        (Quote-Argument $output),
        $format,
        $dpi.ToString(),
        (Quote-Argument $pages)
    )
    if (-not [String]::IsNullOrWhiteSpace($customName)) { $arguments += (Quote-Argument $customName) }
    return Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
}

$pngProcess = Run-Converter $inputPdf $pngOutput 'png' 150 'all'
if ($pngProcess.ExitCode -ne 0) { throw "PNG conversion failed with exit code $($pngProcess.ExitCode)." }
$pngFolder = Join-Path $pngOutput $expectedFolderName
if (-not (Test-Path -LiteralPath $pngFolder -PathType Container)) { throw "Expected per-PDF output folder was not created: $pngFolder" }
if (@(Get-ChildItem -LiteralPath $pngOutput -File).Count -ne 0) { throw 'PNG files were written directly into the selected root folder.' }
$pngFiles = @(Get-ChildItem -LiteralPath $pngFolder -Filter '*.png' -File | Sort-Object Name)
if ($pngFiles.Count -ne 4) { throw "Expected 4 PNG files, found $($pngFiles.Count)." }

$pngRepeatProcess = Run-Converter $inputPdf $pngRepeatOutput 'png' 150 'all'
if ($pngRepeatProcess.ExitCode -ne 0) { throw "Repeated PNG conversion failed with exit code $($pngRepeatProcess.ExitCode)." }
$pngRepeatFiles = @(Get-ChildItem -LiteralPath (Join-Path $pngRepeatOutput $expectedFolderName) -Filter '*.png' -File | Sort-Object Name)
if ($pngRepeatFiles.Count -ne $pngFiles.Count) { throw 'Repeated parallel PNG conversion returned a different page count.' }
for ($pageIndex = 0; $pageIndex -lt $pngFiles.Count; $pageIndex++) {
    if ($pngRepeatFiles[$pageIndex].Name -ne $pngFiles[$pageIndex].Name) { throw 'Repeated parallel PNG conversion changed page ordering.' }
    $firstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pngFiles[$pageIndex].FullName).Hash
    $repeatHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $pngRepeatFiles[$pageIndex].FullName).Hash
    if ($firstHash -ne $repeatHash) { throw "Repeated parallel PNG conversion changed page bytes: $($pngFiles[$pageIndex].Name)" }
}

$jpgProcess = Run-Converter $inputPdf $jpgOutput 'jpg' 220 '2'
if ($jpgProcess.ExitCode -ne 0) { throw "JPEG conversion failed with exit code $($jpgProcess.ExitCode)." }
$jpgFolder = Join-Path $jpgOutput $expectedFolderName
$jpgFiles = @(Get-ChildItem -LiteralPath $jpgFolder -Filter '*.jpg' -File)
if ($jpgFiles.Count -ne 1) { throw "Expected 1 JPEG file, found $($jpgFiles.Count)." }
if ($jpgFiles[0].Name -notlike '*002*.jpg') { throw "Unexpected JPEG page name: $($jpgFiles[0].Name)" }

$bmpProcess = Run-Converter $inputPdf $bmpOutput 'bmp' 150 '1'
if ($bmpProcess.ExitCode -ne 0) { throw "BMP conversion failed with exit code $($bmpProcess.ExitCode)." }
$bmpFiles = @(Get-ChildItem -LiteralPath (Join-Path $bmpOutput $expectedFolderName) -Filter '*.bmp' -File)
if ($bmpFiles.Count -ne 1) { throw "Expected 1 BMP file, found $($bmpFiles.Count)." }

$tiffProcess = Run-Converter $inputPdf $tiffOutput 'tif' 150 '1'
if ($tiffProcess.ExitCode -ne 0) { throw "TIFF conversion failed with exit code $($tiffProcess.ExitCode)." }
$tiffFiles = @(Get-ChildItem -LiteralPath (Join-Path $tiffOutput $expectedFolderName) -Filter '*.tif' -File)
if ($tiffFiles.Count -ne 1) { throw "Expected 1 TIFF file, found $($tiffFiles.Count)." }

$pngImage = [System.Drawing.Image]::FromFile($pngFiles[0].FullName)
$jpgImage = [System.Drawing.Image]::FromFile($jpgFiles[0].FullName)
$bmpImage = [System.Drawing.Image]::FromFile($bmpFiles[0].FullName)
$tiffImage = [System.Drawing.Image]::FromFile($tiffFiles[0].FullName)
try {
    if ($pngImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'PNG signature check failed.' }
    if ($jpgImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Jpeg.Guid) { throw 'JPEG signature check failed.' }
    if ($bmpImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Bmp.Guid) { throw 'BMP signature check failed.' }
    if ($tiffImage.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Tiff.Guid) { throw 'TIFF signature check failed.' }
    if ($pngImage.Width -lt 1000 -or $pngImage.Height -lt 1400) { throw '150 DPI output dimensions are unexpectedly small.' }
    if ($jpgImage.Width -le $pngImage.Width -or $jpgImage.Height -le $pngImage.Height) { throw '220 DPI output should be larger than 150 DPI output.' }
    if ($bmpImage.Width -ne $pngImage.Width -or $bmpImage.Height -ne $pngImage.Height) { throw 'BMP dimensions differ from PNG at the same DPI.' }
    if ($tiffImage.Width -ne $pngImage.Width -or $tiffImage.Height -ne $pngImage.Height) { throw 'TIFF dimensions differ from PNG at the same DPI.' }
}
finally {
    $pngImage.Dispose()
    $jpgImage.Dispose()
    $bmpImage.Dispose()
    $tiffImage.Dispose()
}

$repeatProcess = Run-Converter $inputPdf $jpgOutput 'jpg' 220 '2'
if ($repeatProcess.ExitCode -ne 0) { throw "Repeated conversion failed with exit code $($repeatProcess.ExitCode)." }
$repeatedFolders = @(Get-ChildItem -LiteralPath $jpgOutput -Directory | Sort-Object Name)
$repeatedFiles = @(Get-ChildItem -LiteralPath $jpgOutput -Recurse -Filter '*.jpg' -File)
if ($repeatedFolders.Count -ne 2 -or -not ($repeatedFolders.Name -contains ($expectedFolderName + '(2)'))) { throw 'Same-name folder collision handling did not append (2).' }
if ($repeatedFiles.Count -ne 2) { throw "Expected 2 JPEG files across the two output folders, found $($repeatedFiles.Count)." }

$secondBaseName = ([char]0x7b2c).ToString() + ([char]0x4e8c).ToString() + ([char]0x4efd).ToString() + ([char]0x6d4b).ToString() + ([char]0x8bd5).ToString()
$secondPdf = Join-Path $outputRoot ($secondBaseName + '.pdf')
Copy-Item -LiteralPath $inputPdf -Destination $secondPdf
$multiFirst = Run-Converter $inputPdf $multiOutput 'png' 150 '1'
$multiSecond = Run-Converter $secondPdf $multiOutput 'png' 150 '1'
if ($multiFirst.ExitCode -ne 0 -or $multiSecond.ExitCode -ne 0) { throw 'Multiple PDF folder routing conversion failed.' }
$multiFolders = @(Get-ChildItem -LiteralPath $multiOutput -Directory)
if ($multiFolders.Count -ne 2) { throw "Expected 2 independent PDF output folders, found $($multiFolders.Count)." }
if (-not ($multiFolders.Name -contains $expectedFolderName) -or -not ($multiFolders.Name -contains ($secondBaseName + $convertedSuffix))) { throw 'Multiple PDFs were not routed to folders named after each source PDF.' }

$customName = ([char]0x9879).ToString() + ([char]0x76ee).ToString() + 'A:' + ([char]0x56fe).ToString() + ([char]0x7247).ToString()
$sanitizedCustomName = $customName.Replace(':', '_')
$customProcess = Run-Converter $inputPdf $customOutput 'png' 150 '1' $customName
if ($customProcess.ExitCode -ne 0) { throw "Custom-name conversion failed with exit code $($customProcess.ExitCode)." }
$customFolder = Join-Path $customOutput ($sanitizedCustomName + $convertedSuffix)
if (-not (Test-Path -LiteralPath $customFolder -PathType Container)) { throw 'Custom name was not applied to the result folder.' }
$customFiles = @(Get-ChildItem -LiteralPath $customFolder -Filter '*.png' -File)
if ($customFiles.Count -ne 1 -or $customFiles[0].Name -notlike ($sanitizedCustomName + '_*001*.png')) { throw 'Custom name was not applied to the exported image or invalid characters were not replaced.' }

$linkProbeArguments = New-Object object[] 3
$linkProbeArguments[0] = [string[]]@()
$linkProbeArguments[1] = $null
$linkProbeArguments[2] = $true
$linkProbe = [Windows.Forms.Form]$constructor.Invoke($linkProbeArguments)
try {
    $resultType = $assembly.GetType('LocalImageToPdf.PdfImageExportResult', $true)
    $result = [Activator]::CreateInstance($resultType, $true)
    $resultFiles = [Collections.Generic.List[string]]$resultType.GetProperty('OutputFiles', $flags).GetValue($result, $null)
    $resultFiles.Add($customFiles[0].FullName)
    $statusArguments = New-Object object[] 2
    $statusArguments[0] = $result
    $statusArguments[1] = [string]$customOutput
    $formType.GetMethod('SetSuccessfulStatus', $flags).Invoke($linkProbe, $statusArguments) | Out-Null
    $statusLink = [Windows.Forms.LinkLabel]$formType.GetField('_statusLabel', $flags).GetValue($linkProbe)
    if ($statusLink.Links.Count -ne 1) { throw 'Successful export does not expose the result-folder link in the footer.' }
    if ([string]$statusLink.Links[0].LinkData -ne $customFolder) { throw 'The result-folder link does not point to the actual per-PDF output folder.' }
}
finally {
    $linkProbe.Dispose()
}

$invalidProcess = Run-Converter $inputPdf $invalidOutput 'png' 150 '99'
if ($invalidProcess.ExitCode -eq 0) { throw 'Out-of-range page selection should fail.' }
if (@(Get-ChildItem -LiteralPath $invalidOutput -Force).Count -ne 0) { throw 'Invalid page selection left an unexpected output folder or file.' }

$temporaryFiles = @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Where-Object { $_.Name -like '.pdf-render-*' })
if ($temporaryFiles.Count -ne 0) { throw 'Temporary render files were not cleaned up.' }

[pscustomobject]@{
    Result = 'PASS'
    PngPages = $pngFiles.Count
    ParallelPngHashMatches = $pngRepeatFiles.Count
    JpegPages = $repeatedFiles.Count
    BmpPages = $bmpFiles.Count
    TiffPages = $tiffFiles.Count
    PdfFolders = $multiFolders.Count
    CustomNaming = $customFiles[0].Name
    ResultFolderLink = 'PASS'
    OutputRoot = $outputRoot
} | Format-List
