# A plain window with one text box, for tools/smoke/smoke_test.py to type into. Nothing is saved anywhere.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.Text = 'AIR smoke test'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(160, 160)
$form.Size = New-Object System.Drawing.Size(900, 340)
$form.TopMost = $true
$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = 'Fill'
$box.Font = New-Object System.Drawing.Font('Segoe UI', 14)
$form.Controls.Add($box)
[void]$form.ShowDialog()
