<#
.SYNOPSIS
  Drives the SateAIs dock pane in a running ArcGIS Pro and records what it shows.

.DESCRIPTION
  Run in the desktop session where ArcGIS Pro is open, with the add-in loaded and an
  API key set. It changes the Analysis inputs, reads whether Submit is enabled, and
  saves screenshots for the visual checks. It never presses Submit, so no job is
  created and no credits are used. Estimates go to the API configured in the add-in.

  Output: results.md and results.json, plus one PNG per screenshot, in -OutDir.
  Checks 1, 2, 4-7 are visual: read their screenshots. The others are judged here.

  Saved with a UTF-8 BOM: Windows PowerShell 5.1 reads BOM-less scripts in the ANSI
  code page, which breaks the non-ASCII text this script matches on.
#>
param([string]$OutDir = (Join-Path $PWD 'gui-smoke-out'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing, System.Windows.Forms
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Small areas in Tokyo, one over the timeseries area limit, and one over open ocean
# where Sentinel-1 has no scenes.
$Small = 'POLYGON((139.74 35.66, 139.76 35.66, 139.76 35.68, 139.74 35.68, 139.74 35.66))'
$Large = 'POLYGON((139.70 35.60, 139.81 35.60, 139.81 35.69, 139.70 35.69, 139.70 35.60))'
$Ocean = 'POLYGON((160.00 30.00, 160.05 30.00, 160.05 30.05, 160.00 30.05, 160.00 30.00))'

# --- UI Automation helpers ------------------------------------------------------------

function Cond($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }
function Find($root, $property, $value) { $root.FindFirst($Scope::Descendants, (Cond $property $value)) }
function ByName($root, [string]$name) { Find $root $A::NameProperty $name }
function Parent($el) { $Walker.GetParent($el) }
function Pattern($el, $pattern) { $el.GetCurrentPattern($pattern) }
function Invoke-El($el) { (Pattern $el ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
function Set-Value($el, [string]$text) { (Pattern $el ([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($text) }

function Wait-Until([scriptblock]$condition, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Shot([string]$name, $rect) {
    $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return "$name.png"
}

# --- the pane ---------------------------------------------------------------------------

$proc = Get-Process ArcGISPro -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw 'ArcGIS Pro is not running.' }
$main = $A::RootElement.FindFirst($Scope::Children, (Cond $A::ProcessIdProperty $proc.Id))
$pane = Find $main $A::AutomationIdProperty 'SateAIs_Dockpane'
if (-not $pane) { throw 'Open the SateAIs pane first.' }

function View([string]$class) { Find $pane $A::ClassNameProperty $class }
function Select-Tab([string]$name) {
    $tab = $pane.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition(
        (Cond $A::NameProperty $name), (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::TabItem)))))
    (Pattern $tab ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 400
}
function Submit-Enabled { (ByName (View 'AnalysisView') 'Submit').Current.IsEnabled }
function Estimating { [bool](ByName (View 'AnalysisView') 'Checking coverage and cost…') }

# Texts in the estimate card (between the steps and Submit): the note, credits, legend.
function Card-Texts {
    $view = View 'AnalysisView'
    $submitTop = (ByName $view 'Submit').Current.BoundingRectangle.Y
    $steps = ByName $view 'Area drawn'
    $top = if ($steps) { $steps.Current.BoundingRectangle.Y } else { 0 }
    $texts = $view.FindAll($Scope::Descendants, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text)))
    @($texts | Where-Object {
        $y = $_.Current.BoundingRectangle.Y
        $_.Current.Name -and $y -gt $top + 20 -and $y -lt $submitTop
    } | ForEach-Object { $_.Current.Name })
}

# A step's header is the button around its label; its body is visible only when open.
function Open-Step([string]$label, [scriptblock]$isOpen) {
    if (-not (& $isOpen)) {
        Invoke-El (Parent (ByName (View 'AnalysisView') $label))
        if (-not (Wait-Until $isOpen 5)) { throw "Step '$label' did not open." }
    }
}
function Open-Type { Open-Step 'Detection type' { [bool](Find (View 'AnalysisView') $A::ClassNameProperty 'ItemsControl') } }
function Open-Period {
    Open-Step 'Period' { [bool](Find (View 'AnalysisView') $A::ClassNameProperty 'DatePicker') }
}
function Open-Area { Open-Step 'Area drawn' { [bool](Find (View 'AnalysisView') $A::AutomationIdProperty 'PolygonEdit') } }
function Open-AreaAny {
    # Before an area is set the step is labelled differently; find it by the edit box instead.
    $view = View 'AnalysisView'
    if (Find $view $A::AutomationIdProperty 'PolygonEdit') { return }
    foreach ($label in 'Area drawn', 'Draw an area on the map', 'Area') {
        $t = ByName $view $label
        if ($t) { Invoke-El (Parent $t); if (Wait-Until { [bool](Find (View 'AnalysisView') $A::AutomationIdProperty 'PolygonEdit') } 5) { return } }
    }
    throw 'Area step did not open.'
}

function Choose-Type([string]$label) {
    Open-Type
    # The step header also shows the current type, so look only inside the list of types.
    $list = Find (View 'AnalysisView') $A::ClassNameProperty 'ItemsControl'
    Invoke-El (Parent (ByName $list $label))
    Start-Sleep -Milliseconds 300
}
function Set-Area([string]$wkt) {
    Open-AreaAny
    Set-Value (Find (View 'AnalysisView') $A::AutomationIdProperty 'PolygonEdit') $wkt
}
function Press([string]$name) { Invoke-El (ByName (View 'AnalysisView') $name) }
function Set-Dates([string]$start, [string]$end) {
    Open-Period
    $pickers = (View 'AnalysisView').FindAll($Scope::Descendants, (Cond $A::ClassNameProperty 'DatePicker'))
    Set-Value $pickers[0] $start
    Set-Value $pickers[1] $end
}
function Wait-Estimate {
    # Read the card once per poll: checking and the card text are separate UI reads, and
    # between them the card can switch to "Checking". Settled = a result that holds twice.
    $deadline = (Get-Date).AddSeconds(90)
    $last = $null
    while ((Get-Date) -lt $deadline) {
        $texts = @(Card-Texts)
        $settled = $texts.Count -gt 0 -and -not ($texts -contains 'Checking coverage and cost…')
        $now = $texts -join ' | '
        if ($settled -and $now -eq $last) { return }
        $last = if ($settled) { $now } else { $null }
        Start-Sleep -Milliseconds 400
    }
}

# --- checks -------------------------------------------------------------------------------

$results = New-Object System.Collections.Generic.List[object]
function Record([string]$id, [string]$check, [string]$expected, [bool]$pass, [string]$observed, [string[]]$shots = @()) {
    $results.Add([pscustomobject]@{ id = $id; check = $check; expected = $expected; pass = $pass; observed = $observed; screenshots = $shots })
}
function Run([string]$id, [string]$check, [scriptblock]$body) {
    try { & $body }
    catch {
        $where = "line $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())"
        $shot = try { Shot "err_$id" ([System.Windows.Forms.Screen]::PrimaryScreen.Bounds) } catch { '' }
        Record $id $check '' $false ("error: $($_.Exception.Message) at $where") @($shot)
    }
}

Select-Tab 'Analysis'

# 1-3. Date fields and calendar (New building, Period open)
Run '1' 'Date fields' {
    Choose-Type 'New building detection'
    Open-Period
    $s = Shot '01_period' $pane.Current.BoundingRectangle
    Record '1' 'Date fields' 'Dark frame like the search box, yyyy-MM-dd, small calendar icon' $true 'see screenshot' @($s)
}
Run '2' 'Calendar' {
    $picker = Find (View 'AnalysisView') $A::ClassNameProperty 'DatePicker'
    $r = $picker.Current.BoundingRectangle
    $toggle = Pattern $picker ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $toggle.Expand()
    try {
        Start-Sleep -Milliseconds 700
        $s = Shot '02_calendar' ([System.Windows.Rect]::new(($r.X - 10), ($r.Y - 5), 330, 300))
    } finally {
        $toggle.Collapse()
        Start-Sleep -Milliseconds 300
    }
    Record '2' 'Calendar' 'Dark calendar below the field; today outlined, selected day filled' $true 'see screenshot' @($s)
}
Run '3' 'Picking a date' {
    Set-Area $Small
    Open-Period; Press '12 months'
    Wait-Estimate
    $pickers = (View 'AnalysisView').FindAll($Scope::Descendants, (Cond $A::ClassNameProperty 'DatePicker'))
    Set-Value $pickers[0] '2026-05-01'
    $rechecked = Wait-Until { Estimating } 5
    Wait-Estimate
    $summary = @((View 'AnalysisView').FindAll($Scope::Descendants, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text))) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*→*' -and $_ -like '20*' })
    $ok = $rechecked -and ($summary -join ' ') -like '*2026-05-01*'
    $s = Shot '03_date_changed' $pane.Current.BoundingRectangle
    Record '3' 'Picking a date' 'Period changes and the estimate runs again' $ok ("re-estimated=$rechecked summary=" + ($summary -join ' / ')) @($s)
}
Run '4' 'Reference date (ship)' {
    Choose-Type 'Ship detection'
    Open-Period
    $s = Shot '04_reference_date' $pane.Current.BoundingRectangle
    Record '4' 'Reference date (ship)' 'Same look as the period fields' $true 'see screenshot' @($s)
}

# 8-12. Submit hold
Run '8' 'Submit waits for the estimate' {
    Choose-Type 'New building detection'
    Open-Period; Press '12 months'
    Set-Area $Large
    Wait-Estimate
    Set-Area $Small
    $during = Submit-Enabled
    $wasChecking = Wait-Until { Estimating } 5
    $duringCheck = Submit-Enabled
    Wait-Estimate
    $after = Submit-Enabled
    $ok = (-not $during) -and (-not $duringCheck) -and $after
    Record '8' 'Submit waits for the estimate' 'Disabled while checking, enabled once the estimate is back' $ok ("right after input: $during; while checking ($wasChecking): $duringCheck; after: $after; card: " + ((Card-Texts) -join ' | '))
}
Run '9' 'Area over the limit (timeseries)' {
    Choose-Type 'Time-series change'
    Set-Area $Large
    Wait-Estimate
    $held = -not (Submit-Enabled); $card = (Card-Texts) -join ' | '
    $s = Shot '09_area_limit' $pane.Current.BoundingRectangle
    Set-Area $Small
    Wait-Estimate
    $released = Submit-Enabled
    $ok = $held -and $released -and $card -like '*limit*'
    Record '9' 'Area over the limit (timeseries)' 'Limit sentence and Submit disabled; enabled after a smaller area' $ok ("held=$held released=$released card: $card") @($s)
}
Run '10' 'Period over the limit (timeseries)' {
    Choose-Type 'Time-series change'
    Set-Area $Small
    Wait-Estimate
    Open-Period; Press '5 years'
    Wait-Estimate
    $held = -not (Submit-Enabled); $card = (Card-Texts) -join ' | '
    $s = Shot '10_period_limit' $pane.Current.BoundingRectangle
    Press '2 years'
    Wait-Estimate
    $released = Submit-Enabled
    $ok = $held -and $released
    Record '10' 'Period over the limit (timeseries)' 'Submit disabled with 5 years; enabled with 2 years' $ok ("held=$held released=$released card: $card") @($s)
}
Run '11' 'No scenes (open ocean)' {
    Choose-Type 'New building detection'
    Open-Period; Press '12 months'
    Set-Area $Ocean
    Wait-Estimate
    $held = -not (Submit-Enabled); $card = (Card-Texts) -join ' | '
    $s = Shot '11_no_scenes' $pane.Current.BoundingRectangle
    Set-Area $Small
    Wait-Estimate
    $released = Submit-Enabled
    $ok = $held -and $released
    Record '11' 'No scenes (open ocean)' 'Warning and Submit disabled; enabled over land' $ok ("held=$held released=$released card: $card") @($s)
}
Run '12' 'Too few scenes (5 days)' {
    Choose-Type 'New building detection'
    Set-Area $Small
    Set-Dates '2026-10-24' '2026-10-29'
    Wait-Estimate
    $held = -not (Submit-Enabled); $card = (Card-Texts) -join ' | '
    $s = Shot '12_few_scenes' $pane.Current.BoundingRectangle
    Open-Period; Press '12 months'
    Wait-Estimate
    $released = Submit-Enabled
    $ok = $held -and $released
    Record '12' 'Too few scenes (5 days)' 'Warning and Submit disabled; enabled with 12 months' $ok ("held=$held released=$released card: $card") @($s)
}

# 5-7. Text boxes (last, so the Analysis inputs above are not disturbed)
Run '6' 'Area box' {
    Choose-Type 'New building detection'
    Set-Area ''
    Start-Sleep -Milliseconds 300
    $first = Shot '06_area_placeholder' $pane.Current.BoundingRectangle
    Set-Area $Small
    Start-Sleep -Milliseconds 300
    $second = Shot '06_area_typed' $pane.Current.BoundingRectangle
    Record '6' 'Area box' 'Placeholder and typed text start at the same left edge' $true 'see screenshots' @($first, $second)
}
Run '5' 'Jobs search box' {
    Select-Tab 'Jobs'
    $box = Find (View 'JobsView') $A::AutomationIdProperty 'SearchBox'
    Set-Value $box ''
    Start-Sleep -Milliseconds 300
    $first = Shot '05_search_placeholder' $pane.Current.BoundingRectangle
    Set-Value $box 'newbuilding'
    Start-Sleep -Milliseconds 300
    $second = Shot '05_search_typed' $pane.Current.BoundingRectangle
    Set-Value $box ''
    Select-Tab 'Analysis'
    Record '5' 'Jobs search box' 'Same height as Refresh; placeholder centred; typed text aligned with it' $true 'see screenshots' @($first, $second)
}
Run '7' 'Settings' {
    Invoke-El (ByName $pane 'Settings')
    $dialog = $null
    $null = Wait-Until {
        $script:dialog = $A::RootElement.FindFirst($Scope::Children, (New-Object System.Windows.Automation.AndCondition(
            (Cond $A::ProcessIdProperty $proc.Id), (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Window)))))
        $script:dialog -and $script:dialog.Current.Name -ne $main.Current.Name
    } 10
    if (-not $script:dialog -or $script:dialog.Current.Name -eq $main.Current.Name) {
        $script:dialog = $main.FindFirst($Scope::Children, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Window)))
    }
    $s = Shot '07_settings' $script:dialog.Current.BoundingRectangle
    (Pattern $script:dialog ([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    Record '7' 'Settings' 'API key field does not stretch vertically' $true 'see screenshot' @($s)
}

# --- report -------------------------------------------------------------------------------

$results | ConvertTo-Json -Depth 4 | Out-File (Join-Path $OutDir 'results.json') -Encoding utf8
$lines = @('| # | Check | Pass | Observed | Screenshots |', '|---|---|---|---|---|')
foreach ($r in ($results | Sort-Object { [int]$_.id })) {
    $lines += "| $($r.id) | $($r.check) | $(if ($r.pass) { 'yes' } else { '**no**' }) | $($r.observed -replace '\|', '/') | $($r.screenshots -join ', ') |"
}
$lines | Out-File (Join-Path $OutDir 'results.md') -Encoding utf8
