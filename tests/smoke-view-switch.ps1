$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exeName = ([char]0x56fe).ToString() + ([char]0x7247).ToString() + ([char]0x8f6c).ToString() + 'PDF.exe'
$exe = Join-Path $projectRoot ('dist\' + $exeName)
if (-not (Test-Path -LiteralPath $exe)) { & (Join-Path $projectRoot 'build.ps1') }

$assembly = [Reflection.Assembly]::LoadFile($exe)
$mainType = $assembly.GetType('LocalImageToPdf.MainForm', $true)
$constructor = $mainType.GetConstructor(
    [Reflection.BindingFlags]'Instance, Public, NonPublic',
    $null,
    [Type[]]@([string[]]),
    $null)
if ($null -eq $constructor) { throw 'Main window constructor was not found.' }
$constructorArguments = New-Object object[] 1
$constructorArguments[0] = [string[]]@()
$form = [System.Windows.Forms.Form]$constructor.Invoke($constructorArguments)

try {
    $form.ShowInTaskbar = $false
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point(-32000, -32000)
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    $imageViewField = $mainType.GetField('_imageToPdfView', [Reflection.BindingFlags]'Instance, NonPublic')
    $pdfViewField = $mainType.GetField('_pdfConverter', [Reflection.BindingFlags]'Instance, NonPublic')
    $openMethod = $mainType.GetMethod('OpenPdfConverter', [Reflection.BindingFlags]'Instance, NonPublic')
    $returnMethod = $mainType.GetMethod('ReturnToImageConverter', [Reflection.BindingFlags]'Instance, NonPublic')
    if ($null -in @($imageViewField, $pdfViewField, $openMethod, $returnMethod)) { throw 'View-switch members were not found.' }

    $imageView = [System.Windows.Forms.Control]$imageViewField.GetValue($form)
    $pdfView = [System.Windows.Forms.Form]$pdfViewField.GetValue($form)
    if ($null -eq $imageView -or $null -eq $pdfView) { throw 'Both converter views must be pre-created.' }
    $returnLinks = @($pdfView.Controls | ForEach-Object {
        $queue = New-Object 'System.Collections.Generic.Queue[System.Windows.Forms.Control]'
        $queue.Enqueue($_)
        while ($queue.Count -gt 0) {
            $control = $queue.Dequeue()
            if ($control -is [System.Windows.Forms.LinkLabel] -and $control.Links.Count -gt 0) { $control }
            foreach ($child in $control.Controls) { $queue.Enqueue($child) }
        }
    })
    if ($returnLinks.Count -ne 1) { throw ('The embedded PDF page must expose one return link; found ' + $returnLinks.Count + '.') }

    $openArguments = New-Object object[] 1
    $openArguments[0] = [string[]]@()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $openMethod.Invoke($form, $openArguments) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
    $toPdfMilliseconds = $watch.ElapsedMilliseconds
    if ($imageView.Visible -or -not $pdfView.Visible) { throw 'Switching to PDF must exchange the two in-window views.' }
    if ($toPdfMilliseconds -gt 500) { throw ('PDF view switch exceeded 500 ms: ' + $toPdfMilliseconds) }

    $returnArguments = New-Object object[] 2
    $returnArguments[0] = $pdfView
    $returnArguments[1] = [EventArgs]::Empty
    $watch.Restart()
    $returnMethod.Invoke($form, $returnArguments) | Out-Null
    [System.Windows.Forms.Application]::DoEvents()
    $toImageMilliseconds = $watch.ElapsedMilliseconds
    if (-not $imageView.Visible -or $pdfView.Visible) { throw 'Returning must restore the image view without replacing the main window.' }

    [pscustomobject]@{
        Result = 'PASS'
        SameWindow = $true
        ReturnLink = $true
        ToPdfMilliseconds = $toPdfMilliseconds
        ToImageMilliseconds = $toImageMilliseconds
    } | Format-List
}
finally {
    $form.Close()
    $form.Dispose()
}
