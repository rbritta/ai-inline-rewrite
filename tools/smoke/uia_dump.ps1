# Prints, as JSON, every element of a process's top-level windows (name, control type, automation id, screen
# rectangle), for tools/smoke/smoke_test.py to find the bubble's rows, buttons and status.
param([Parameter(Mandatory)][int]$ProcessId)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
$out = @()
foreach ($w in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
    $all = $w.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        $c = $e.Current
        $r = $c.BoundingRectangle
        if ($r.IsEmpty) { continue }
        $toggle = $null
        $p = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $toggle = "$($p.Current.ToggleState)" }
        $out += [pscustomobject]@{
            win = $w.Current.Name; name = $c.Name; type = $c.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            status = $c.ItemStatus; toggle = $toggle; x = [int]$r.X; y = [int]$r.Y; w = [int]$r.Width; h = [int]$r.Height
        }
    }
}
ConvertTo-Json -InputObject @($out) -Compress -Depth 3
