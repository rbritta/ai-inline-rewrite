# Prints "left,top,right,bottom" of the notification-area icon whose name holds -Name, on the taskbar or in the open
# hidden-icons flyout, for tools/smoke/smoke_test.py to right-click AIR's icon; "not found" with exit code 1 otherwise.
# "Holds", not "starts with": the taskbar puts an invisible mark before some icons' names.
param([Parameter(Mandatory)][string]$Name)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$All = [System.Windows.Automation.Condition]::TrueCondition
foreach ($w in $A::RootElement.FindAll($Tree::Children, $All)) {
    if ($w.Current.ClassName -notin @('Shell_TrayWnd', 'TopLevelWindowForOverflowXamlIsland', 'NotifyIconOverflowWindow')) { continue }
    foreach ($e in $w.FindAll($Tree::Descendants, $All)) {
        $n = $e.Current.Name
        if (-not $n -or -not $n.Contains($Name)) { continue }
        $r = $e.Current.BoundingRectangle
        if ($r.Width -gt 0 -and $r.Height -gt 0) {
            '{0},{1},{2},{3}' -f [int]$r.Left, [int]$r.Top, [int]$r.Right, [int]$r.Bottom
            exit 0
        }
    }
}
'not found'
exit 1
