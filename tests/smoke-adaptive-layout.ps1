$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $testRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist') -Filter '*.exe' -File | Select-Object -First 1
if (-not $exe) { throw 'Build the application before running the adaptive-layout test.' }

$assembly = [Reflection.Assembly]::LoadFile($exe.FullName)
$instanceFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$adaptiveType = $assembly.GetType('LocalImageToPdf.AdaptiveForm', $true)
$dpiMethod = $adaptiveType.GetMethod('SetLayoutDpiForTesting', $instanceFlags)
$refreshMethod = $adaptiveType.GetMethod('RefreshAdaptiveLayout', $instanceFlags)
$currentDpiProperty = $adaptiveType.GetProperty('CurrentDpi', $instanceFlags)
$layoutGuard = $adaptiveType.GetField('_applyingAdaptiveLayout', $instanceFlags)

function Get-PrivateControl([System.Windows.Forms.Form]$form, [string]$name) {
    $field = $form.GetType().GetField($name, $script:instanceFlags)
    if ($null -eq $field) { throw "Layout field was not found: $($form.GetType().Name).$name" }
    return [System.Windows.Forms.Control]$field.GetValue($form)
}

function Assert-ChildrenInside([System.Windows.Forms.Control]$parent, [string]$context) {
    foreach ($child in @($parent.Controls | Where-Object { $_.Visible })) {
        if ($child.Left -lt -1 -or $child.Top -lt -1 -or
            $child.Right -gt $parent.ClientSize.Width + 1 -or
            $child.Bottom -gt $parent.ClientSize.Height + 1) {
            throw "$context contains an out-of-bounds control '$($child.Text)': child=$($child.Bounds), parent=$($parent.ClientRectangle)"
        }
    }
}

function Assert-NoSiblingOverlap([System.Windows.Forms.Control]$parent, [string]$context) {
    $children = @($parent.Controls | Where-Object { $_.Visible })
    for ($leftIndex = 0; $leftIndex -lt $children.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $children.Count; $rightIndex++) {
            if ($children[$leftIndex].Bounds.IntersectsWith($children[$rightIndex].Bounds)) {
                throw "$context overlap: '$($children[$leftIndex].Text)' $($children[$leftIndex].Bounds) and '$($children[$rightIndex].Text)' $($children[$rightIndex].Bounds)"
            }
        }
    }
}

function Assert-ScrollableChildrenInside([System.Windows.Forms.ScrollableControl]$parent, [string]$context) {
    $availableWidth = [Math]::Max($parent.ClientSize.Width, $parent.AutoScrollMinSize.Width)
    $availableHeight = [Math]::Max($parent.ClientSize.Height, $parent.AutoScrollMinSize.Height)
    $scroll = $parent.AutoScrollPosition
    foreach ($child in @($parent.Controls | Where-Object { $_.Visible })) {
        $logicalLeft = $child.Left - $scroll.X
        $logicalTop = $child.Top - $scroll.Y
        $logicalRight = $logicalLeft + $child.Width
        $logicalBottom = $logicalTop + $child.Height
        if ($logicalLeft -lt -1 -or $logicalTop -lt -1 -or
            $logicalRight -gt $availableWidth + 1 -or $logicalBottom -gt $availableHeight + 1) {
            throw "$context contains an inaccessible control '$($child.Text)': child=$($child.Bounds), scrollable=$availableWidth x $availableHeight"
        }
    }
}

function Set-SimulatedDpi([System.Windows.Forms.Form]$form, [int]$dpi) {
    $actualDpi = [int]$script:currentDpiProperty.GetValue($form, $null)
    $factor = $dpi / [float][Math]::Max(96, $actualDpi)
    $script:layoutGuard.SetValue($form, $true)
    try {
        if ([Math]::Abs($factor - 1.0) -gt 0.001) {
            $form.Scale((New-Object Drawing.SizeF($factor, $factor)))
        }
    }
    finally {
        $script:layoutGuard.SetValue($form, $false)
    }
    $script:dpiMethod.Invoke($form, @($dpi))
}

function Set-LogicalClientSize([System.Windows.Forms.Form]$form, [int]$dpi, [int]$width, [int]$height) {
    $form.ClientSize = New-Object Drawing.Size(
        [int][Math]::Round($width * $dpi / 96.0),
        [int][Math]::Round($height * $dpi / 96.0))
    $form.PerformLayout()
    $script:refreshMethod.Invoke($form, $null)
    $form.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()
}

function New-MainForm {
    $type = $script:assembly.GetType('LocalImageToPdf.MainForm', $true)
    $constructor = $type.GetConstructor($script:instanceFlags, $null, [Type[]]@([string[]]), $null)
    $arguments = New-Object object[] 1
    $arguments[0] = [string[]]@()
    return [System.Windows.Forms.Form]$constructor.Invoke($arguments)
}

function New-PdfForm {
    $type = $script:assembly.GetType('LocalImageToPdf.PdfToImageForm', $true)
    $constructor = $type.GetConstructor(
        $script:instanceFlags,
        $null,
        [Type[]]@([System.Collections.Generic.IEnumerable[string]], [Drawing.Icon], [bool]),
        $null)
    $arguments = New-Object object[] 3
    $arguments[0] = [string[]]@()
    $arguments[1] = $null
    $arguments[2] = $true
    return [System.Windows.Forms.Form]$constructor.Invoke($arguments)
}

function New-SettingsForm {
    $type = $script:assembly.GetType('LocalImageToPdf.SettingsForm', $true)
    $constructor = $type.GetConstructor($script:instanceFlags, $null, [Type[]]@([Drawing.Icon]), $null)
    return [System.Windows.Forms.Form]$constructor.Invoke(@($null))
}

function New-OnboardingForm {
    $type = $script:assembly.GetType('LocalImageToPdf.SendToOnboardingForm', $true)
    $constructor = $type.GetConstructor($script:instanceFlags, $null, [Type[]]@([Drawing.Icon]), $null)
    return [System.Windows.Forms.Form]$constructor.Invoke(@($null))
}

function New-WatermarkForm {
    $optionsType = $script:assembly.GetType('LocalImageToPdf.WatermarkOptions', $true)
    $none = $optionsType.GetMethod('None', [Reflection.BindingFlags]'Static,Public,NonPublic').Invoke($null, $null)
    $type = $script:assembly.GetType('LocalImageToPdf.WatermarkDialog', $true)
    $constructor = $type.GetConstructor($script:instanceFlags, $null, [Type[]]@($optionsType, [Drawing.Icon]), $null)
    return [System.Windows.Forms.Form]$constructor.Invoke(@($none, $null))
}

function New-PreviewForm {
    $type = $script:assembly.GetType('LocalImageToPdf.LargePreviewForm', $true)
    $constructor = $type.GetConstructor($script:instanceFlags, $null, [Type[]]@([string], [Drawing.Bitmap], [Drawing.Icon]), $null)
    $bitmap = New-Object Drawing.Bitmap(100, 140)
    $arguments = New-Object object[] 3
    $arguments[0] = 'preview.png'
    $arguments[1] = $bitmap.PSObject.BaseObject
    $arguments[2] = $null
    return [System.Windows.Forms.Form]$constructor.Invoke($arguments)
}

function Show-Hidden([System.Windows.Forms.Form]$form) {
    $form.ShowInTaskbar = $false
    $form.Opacity = 0
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    if ($form.AutoScaleMode -ne [System.Windows.Forms.AutoScaleMode]::Dpi) {
        throw "$($form.GetType().Name) is not using DPI auto scaling."
    }
}

function Test-ConverterLayout([System.Windows.Forms.Form]$form, [int]$dpi, [object[]]$sizes, [bool]$isMain) {
    Show-Hidden $form
    Set-SimulatedDpi $form $dpi
    foreach ($size in $sizes) {
        Set-LogicalClientSize $form $dpi $size[0] $size[1]
        $root = Get-PrivateControl $form '_rootLayout'
        $header = Get-PrivateControl $form '_headerPanel'
        $actions = Get-PrivateControl $form '_headerActions'
        $content = Get-PrivateControl $form '_contentLayout'
        $footer = Get-PrivateControl $form '_footerPanel'
        $cancel = Get-PrivateControl $form '_cancelButton'

        Assert-ChildrenInside $root "$($form.GetType().Name) root at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $root "$($form.GetType().Name) root at $dpi DPI, $($size[0])x$($size[1])"
        Assert-ChildrenInside $header "$($form.GetType().Name) header at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $header "$($form.GetType().Name) header at $dpi DPI, $($size[0])x$($size[1])"
        Assert-ChildrenInside $actions "$($form.GetType().Name) actions at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $actions "$($form.GetType().Name) actions at $dpi DPI, $($size[0])x$($size[1])"
        Assert-ChildrenInside $content "$($form.GetType().Name) content at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $content "$($form.GetType().Name) content at $dpi DPI, $($size[0])x$($size[1])"
        Assert-ChildrenInside $footer "$($form.GetType().Name) footer at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $footer "$($form.GetType().Name) footer at $dpi DPI, $($size[0])x$($size[1])"

        $cancel.Visible = $true
        $script:refreshMethod.Invoke($form, $null)
        [System.Windows.Forms.Application]::DoEvents()
        Assert-ChildrenInside $footer "$($form.GetType().Name) exporting footer at $dpi DPI, $($size[0])x$($size[1])"
        Assert-NoSiblingOverlap $footer "$($form.GetType().Name) exporting footer at $dpi DPI, $($size[0])x$($size[1])"
        $cancel.Visible = $false
    }
}

function Test-DialogLayout([System.Windows.Forms.Form]$form, [int]$dpi, [int]$width, [int]$height, [bool]$requiresScroll) {
    Show-Hidden $form
    Set-SimulatedDpi $form $dpi
    Set-LogicalClientSize $form $dpi $width $height
    if ($requiresScroll -and (-not $form.AutoScroll -or $form.AutoScrollMinSize.IsEmpty)) {
        throw "$($form.GetType().Name) does not provide a scrollable fallback at $dpi DPI."
    }
    Assert-ScrollableChildrenInside $form "$($form.GetType().Name) at $dpi DPI"
    Assert-NoSiblingOverlap $form "$($form.GetType().Name) at $dpi DPI"
}

$dpis = @(96, 120, 144, 168, 192, 216, 240)
$mainSizes = @(@(720, 520), @(839, 600), @(959, 650), @(1119, 700), @(1440, 900))
$pdfSizes = @(@(700, 500), @(759, 560), @(1099, 680), @(1119, 700), @(1180, 760))
$checks = 0

foreach ($dpi in $dpis) {
    $main = New-MainForm
    try { Test-ConverterLayout $main $dpi $mainSizes $true }
    finally { $main.Close(); $main.Dispose() }

    $pdf = New-PdfForm
    try { Test-ConverterLayout $pdf $dpi $pdfSizes $false }
    finally { $pdf.Close(); $pdf.Dispose() }

    $settings = New-SettingsForm
    try { Test-DialogLayout $settings $dpi 480 380 $true }
    finally { $settings.Close(); $settings.Dispose() }

    $onboarding = New-OnboardingForm
    try { Test-DialogLayout $onboarding $dpi 520 360 $true }
    finally { $onboarding.Close(); $onboarding.Dispose() }

    $watermark = New-WatermarkForm
    try { Test-DialogLayout $watermark $dpi 420 300 $true }
    finally { $watermark.Close(); $watermark.Dispose() }

    $preview = New-PreviewForm
    try { Test-DialogLayout $preview $dpi 600 420 $false }
    finally { $preview.Close(); $preview.Dispose() }

    $checks += $mainSizes.Count + $pdfSizes.Count + 4
}

$activeForms = @('MainForm', 'PdfToImageForm', 'WatermarkDialog', 'SendToOnboardingForm', 'SettingsForm', 'LargePreviewForm')
foreach ($name in $activeForms) {
    $type = $assembly.GetType(('LocalImageToPdf.' + $name), $true)
    if (-not $type.IsSubclassOf($adaptiveType)) { throw "$name does not inherit AdaptiveForm." }
}

[pscustomobject]@{
    Result = 'PASS'
    DpiPercentages = ($dpis | ForEach-Object { [int]($_ * 100 / 96) }) -join ', '
    LayoutChecks = $checks
    ActiveAdaptiveForms = $activeForms.Count
}
