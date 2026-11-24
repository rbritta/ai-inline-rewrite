# Presses one control of a process's window through UI Automation (Toggle when it has an on/off state, else Invoke),
# for tools/smoke/smoke_test.py to pick an option the window may have scrolled out of sight. -Window * looks in any
# window of the process (the tray menu has no title). Prints "pressed", or "not found" with exit code 1.
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$Window,
    [Parameter(Mandatory)][string]$Name,
    [string]$ControlType = ''
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
foreach ($w in $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
    if ($Window -ne '*' -and $w.Current.Name -ne $Window) { continue }
    $named = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Name)
    foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Subtree, $named)) {
        $type = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        if ($ControlType -and $type -ne $ControlType) { continue }
        $p = $null
        if ($e.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$p)) { $p.Toggle(); 'pressed'; exit 0 }
        if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); 'pressed'; exit 0 }
    }
}
'not found'
exit 1
