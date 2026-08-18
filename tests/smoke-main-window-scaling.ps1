$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $testRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
if (-not $exe) { throw 'Build the application before running the main-window scaling test.' }

$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$baseType = $assembly.GetType('LocalImageToPdf.DisplayAwareMainForm', $true)
$dpiProperty = $baseType.GetProperty('CurrentDpi', $flags)
$setDpi = $baseType.GetMethod('SetDpiForLayoutTesting', $flags)

function New-MainForm {
    $type = $script:assembly.GetType('LocalImageToPdf.MainForm', $true)
    $constructor = $type.GetConstructor($script:flags, $null, [Type[]]@([string[]]), $null)
    $arguments = New-Object object[] 1
    $arguments[0] = [string[]]@()
    return [Windows.Forms.Form]$constructor.Invoke($arguments)
}

function New-PdfForm {
    $type = $script:assembly.GetType('LocalImageToPdf.PdfToImageForm', $true)
    $constructor = $type.GetConstructor($script:flags, $null, [Type[]]@([Collections.Generic.IEnumerable[string]], [Drawing.Icon], [bool]), $null)
    $arguments = New-Object object[] 3
    $arguments[0] = [string[]]@()
    $arguments[1] = $null
    $arguments[2] = $true
    return [Windows.Forms.Form]$constructor.Invoke($arguments)
}

function Get-RootLayout([Windows.Forms.Form]$form) {
    if ($form.GetType().Name -eq 'MainForm') {
        $field = $form.GetType().GetField('_imageToPdfView', $script:flags)
        return [Windows.Forms.TableLayoutPanel]$field.GetValue($form)
    }
    return [Windows.Forms.TableLayoutPanel]($form.Controls | Where-Object { $_ -is [Windows.Forms.TableLayoutPanel] } | Select-Object -First 1)
}

function Assert-OriginalStructure([Windows.Forms.Form]$form, [int]$sidebarWidth) {
    $root = Get-RootLayout $form
    if ($root.ColumnCount -ne 1 -or $root.RowCount -ne 3) { throw "$($form.GetType().Name) root structure changed." }
    $content = [Windows.Forms.TableLayoutPanel]$root.GetControlFromPosition(0, 1)
    if ($content.ColumnCount -ne 2 -or $content.RowCount -ne 1) { throw "$($form.GetType().Name) no longer uses the v1.2.0 left/right layout." }
    if ($content.ColumnStyles[1].SizeType -ne [Windows.Forms.SizeType]::Absolute -or [Math]::Abs($content.ColumnStyles[1].Width - $sidebarWidth) -gt 0.1) {
        throw "$($form.GetType().Name) sidebar width changed from the v1.2.0 design."
    }
    $header = $root.GetControlFromPosition(0, 0)
    $actions = $header.Controls | Where-Object { $_ -is [Windows.Forms.FlowLayoutPanel] } | Select-Object -First 1
    if (-not $actions -or $actions.WrapContents) { throw "$($form.GetType().Name) header actions no longer use the original single row." }
}

function Assert-ChildrenInside([Windows.Forms.Control]$parent, [string]$context) {
    foreach ($child in @($parent.Controls | Where-Object { $_.Visible })) {
        if ($child.Left -lt -1 -or $child.Top -lt -1 -or $child.Right -gt $parent.ClientSize.Width + 1 -or $child.Bottom -gt $parent.ClientSize.Height + 1) {
            throw "$context contains an out-of-bounds control '$($child.Text)': child=$($child.Bounds), parent=$($parent.ClientRectangle)"
        }
    }
}

function Assert-NoOverlap([Windows.Forms.Control]$parent, [string]$context) {
    $children = @($parent.Controls | Where-Object { $_.Visible })
    for ($left = 0; $left -lt $children.Count; $left++) {
        for ($right = $left + 1; $right -lt $children.Count; $right++) {
            if ($children[$left].Bounds.IntersectsWith($children[$right].Bounds)) {
                throw "$context overlap: '$($children[$left].Text)' and '$($children[$right].Text)'"
            }
        }
    }
}

function Test-ScaledLayout([Windows.Forms.Form]$form, [int]$dpi, [int]$logicalWidth, [int]$logicalHeight) {
    $form.ShowInTaskbar = $false
    $form.Opacity = 0
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $actualDpi = [int]$script:dpiProperty.GetValue($form, $null)
    $factor = $dpi / [float][Math]::Max(96, $actualDpi)
    $script:setDpi.Invoke($form, @($dpi))
    if ([Math]::Abs($factor - 1.0) -gt 0.001) { $form.Scale((New-Object Drawing.SizeF($factor, $factor))) }
    $form.ClientSize = New-Object Drawing.Size([int][Math]::Round($logicalWidth * $dpi / 96.0), [int][Math]::Round($logicalHeight * $dpi / 96.0))
    $form.PerformLayout()
    [Windows.Forms.Application]::DoEvents()

    $root = Get-RootLayout $form
    $content = [Windows.Forms.TableLayoutPanel]$root.GetControlFromPosition(0, 1)
    if ($content.ColumnCount -ne 2 -or $content.RowCount -ne 1) { throw "$($form.GetType().Name) rearranged its content at $dpi DPI." }
    $header = $root.GetControlFromPosition(0, 0)
    $footer = $root.GetControlFromPosition(0, 2)
    Assert-ChildrenInside $header "$($form.GetType().Name) header at $dpi DPI"
    Assert-NoOverlap $header "$($form.GetType().Name) header at $dpi DPI"
    Assert-ChildrenInside $footer "$($form.GetType().Name) footer at $dpi DPI"
    Assert-NoOverlap $footer "$($form.GetType().Name) footer at $dpi DPI"
}

$mainDesign = New-MainForm
try { Assert-OriginalStructure $mainDesign 460 }
finally { $mainDesign.Dispose() }
$pdfDesign = New-PdfForm
try { Assert-OriginalStructure $pdfDesign 430 }
finally { $pdfDesign.Dispose() }

$dialogNames = @('WatermarkDialog', 'SendToOnboardingForm', 'SettingsForm', 'LargePreviewForm')
foreach ($name in $dialogNames) {
    $type = $assembly.GetType(('LocalImageToPdf.' + $name), $true)
    if ($type.IsSubclassOf($baseType)) { throw "$name was changed even though only main windows should be adapted." }
}

$dpis = @(96, 120, 144, 168, 192, 216, 240)
foreach ($dpi in $dpis) {
    $main = New-MainForm
    try { Test-ScaledLayout $main $dpi 1080 700 }
    finally { $main.Close(); $main.Dispose() }
    $pdf = New-PdfForm
    try { Test-ScaledLayout $pdf $dpi 940 640 }
    finally { $pdf.Close(); $pdf.Dispose() }
}

[pscustomobject]@{
    Result = 'PASS'
    DpiPercentages = ($dpis | ForEach-Object { [int]($_ * 100 / 96) }) -join ', '
    MainWindowChecks = $dpis.Count * 2
    Layout = 'v1.2.0 left/right structure preserved'
    DialogsUnchanged = $dialogNames.Count
}
