# Disk Health Lite
# Lightweight Windows disk health monitor using built-in Storage cmdlets.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    Start-Process -FilePath "powershell.exe" -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$scriptPath`""
    ) -Verb RunAs
    exit
}

[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

$script:Disks = @()
$script:SelectedDiskId = $null

function Get-PropValue {
    param(
        [Parameter(Mandatory = $false)] $Object,
        [Parameter(Mandatory = $true)] [string[]] $Names
    )

    if ($null -eq $Object) { return $null }

    foreach ($name in $Names) {
        $prop = $Object.PSObject.Properties[$name]
        if ($null -ne $prop -and $null -ne $prop.Value -and "$($prop.Value)" -ne "") {
            return $prop.Value
        }
    }

    return $null
}

function Format-Bytes {
    param([Nullable[double]] $Bytes)

    if ($null -eq $Bytes) { return "Not reported" }

    $units = @("B", "KB", "MB", "GB", "TB", "PB")
    $value = [double]$Bytes
    $index = 0
    while ($value -ge 1024 -and $index -lt ($units.Length - 1)) {
        $value = $value / 1024
        $index++
    }

    if ($index -eq 0) { return "{0:N0} {1}" -f $value, $units[$index] }
    return "{0:N2} {1}" -f $value, $units[$index]
}

function Format-Hours {
    param([Nullable[double]] $Hours)

    if ($null -eq $Hours) { return "Not reported" }

    $wholeHours = [int64][math]::Floor([double]$Hours)
    $days = [math]::Floor($wholeHours / 24)
    $hoursLeft = $wholeHours % 24

    if ($days -gt 0) { return "{0:N0} days, {1} hours" -f $days, $hoursLeft }
    return "{0:N0} hours" -f $wholeHours
}

function Format-Size {
    param([Nullable[double]] $Bytes)
    if ($null -eq $Bytes) { return "Unknown size" }
    return Format-Bytes $Bytes
}

function Get-DriveLettersForDisk {
    param([int] $DiskNumber)

    try {
        $letters = Get-Partition -DiskNumber $DiskNumber -ErrorAction Stop |
            Where-Object { $_.DriveLetter } |
            ForEach-Object { "$($_.DriveLetter):" }
        if ($letters) { return ($letters -join ", ") }
    } catch {
        return ""
    }

    return ""
}

function Get-DiskSnapshot {
    $items = New-Object System.Collections.Generic.List[object]

    try {
        $physicalDisks = @(Get-PhysicalDisk -ErrorAction Stop | Sort-Object DeviceId)
    } catch {
        throw "Could not read physical disks. $($_.Exception.Message)"
    }

    foreach ($disk in $physicalDisks) {
        $counter = $null
        $counterError = $null

        try {
            $counter = $disk | Get-StorageReliabilityCounter -ErrorAction Stop
        } catch {
            $counterError = $_.Exception.Message
        }

        $wear = Get-PropValue $counter @("Wear")
        $temperature = Get-PropValue $counter @("Temperature")
        $powerOnHours = Get-PropValue $counter @("PowerOnHours")
        $bytesWritten = Get-PropValue $counter @("BytesWritten", "TotalBytesWritten", "HostWrites")

        $healthPercent = $null
        if ($null -ne $wear) {
            $healthPercent = [math]::Max(0, [math]::Min(100, 100 - [int]$wear))
        } elseif ("$($disk.HealthStatus)" -eq "Healthy") {
            $healthPercent = 100
        }

        $deviceId = [int](Get-PropValue $disk @("DeviceId"))
        $letters = Get-DriveLettersForDisk $deviceId

        $items.Add([pscustomobject]@{
            Id              = $deviceId
            Name            = "$($disk.FriendlyName)"
            Serial          = "$($disk.SerialNumber)".Trim()
            MediaType       = "$($disk.MediaType)"
            BusType         = "$($disk.BusType)"
            Size            = [double]$disk.Size
            HealthStatus    = "$($disk.HealthStatus)"
            Operational     = "$($disk.OperationalStatus)"
            HealthPercent   = $healthPercent
            Wear            = $wear
            Temperature     = $temperature
            PowerOnHours    = $powerOnHours
            LifetimeWrites  = $bytesWritten
            DriveLetters    = $letters
            CounterError    = $counterError
        })
    }

    return @($items)
}

function New-Label {
    param(
        [string] $Text,
        [int] $X,
        [int] $Y,
        [int] $Width,
        [int] $Height = 24,
        [int] $Size = 10,
        [System.Drawing.FontStyle] $Style = [System.Drawing.FontStyle]::Regular,
        [System.Drawing.Color] $Color = [System.Drawing.Color]::FromArgb(31, 41, 55)
    )

    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($Width, $Height)
    $label.Font = New-Object System.Drawing.Font("Segoe UI", $Size, $Style)
    $label.ForeColor = $Color
    $label.AutoEllipsis = $true
    return $label
}

function New-MetricCard {
    param(
        [string] $Title,
        [int] $X,
        [int] $Y
    )

    $card = New-Object System.Windows.Forms.Panel
    $card.Location = New-Object System.Drawing.Point($X, $Y)
    $card.Size = New-Object System.Drawing.Size(210, 94)
    $card.BackColor = [System.Drawing.Color]::White
    $card.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle

    $titleLabel = New-Label $Title 14 10 180 20 9 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(107, 114, 128))
    $valueLabel = New-Label "..." 14 36 180 34 17 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::FromArgb(17, 24, 39))
    $hintLabel = New-Label "" 14 70 180 18 8 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(107, 114, 128))

    $card.Controls.AddRange(@($titleLabel, $valueLabel, $hintLabel))
    return [pscustomobject]@{
        Panel = $card
        Title = $titleLabel
        Value = $valueLabel
        Hint = $hintLabel
    }
}

function Set-BarValue {
    param(
        [System.Windows.Forms.Panel] $Fill,
        [Nullable[int]] $Percent
    )

    if ($null -eq $Percent) {
        $Fill.Width = 0
        $Fill.BackColor = [System.Drawing.Color]::FromArgb(156, 163, 175)
        return
    }

    $Fill.Width = [math]::Round(520 * ([math]::Max(0, [math]::Min(100, $Percent)) / 100))

    if ($Percent -ge 80) {
        $Fill.BackColor = [System.Drawing.Color]::FromArgb(34, 197, 94)
    } elseif ($Percent -ge 50) {
        $Fill.BackColor = [System.Drawing.Color]::FromArgb(245, 158, 11)
    } else {
        $Fill.BackColor = [System.Drawing.Color]::FromArgb(239, 68, 68)
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Disk Health Lite"
$form.StartPosition = "CenterScreen"
$form.MinimumSize = New-Object System.Drawing.Size(980, 610)
$form.Size = New-Object System.Drawing.Size(1060, 650)
$form.BackColor = [System.Drawing.Color]::FromArgb(245, 247, 250)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$header = New-Object System.Windows.Forms.Panel
$header.Dock = [System.Windows.Forms.DockStyle]::Top
$header.Height = 68
$header.BackColor = [System.Drawing.Color]::FromArgb(15, 23, 42)

$title = New-Label "Disk Health Lite" 22 13 360 30 16 ([System.Drawing.FontStyle]::Bold) ([System.Drawing.Color]::White)
$subtitle = New-Label "Health, temperature, power-on time, and lifetime writes" 24 41 560 18 9 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(203, 213, 225))
$header.Controls.AddRange(@($title, $subtitle))

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh"
$refreshButton.Size = New-Object System.Drawing.Size(96, 32)
$refreshButton.Location = New-Object System.Drawing.Point(835, 18)
$refreshButton.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Right
$refreshButton.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$refreshButton.BackColor = [System.Drawing.Color]::FromArgb(37, 99, 235)
$refreshButton.ForeColor = [System.Drawing.Color]::White
$refreshButton.FlatAppearance.BorderSize = 0
$header.Controls.Add($refreshButton)

$statusLabel = New-Label "Starting..." 650 24 170 20 9 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(203, 213, 225))
$statusLabel.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Right
$statusLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
$header.Controls.Add($statusLabel)

$leftPanel = New-Object System.Windows.Forms.Panel
$leftPanel.Location = New-Object System.Drawing.Point(18, 88)
$leftPanel.Size = New-Object System.Drawing.Size(310, 505)
$leftPanel.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left
$leftPanel.BackColor = [System.Drawing.Color]::White
$leftPanel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle

$diskListTitle = New-Label "Detected disks" 16 14 260 24 11 ([System.Drawing.FontStyle]::Bold)
$diskList = New-Object System.Windows.Forms.ListView
$diskList.Location = New-Object System.Drawing.Point(14, 48)
$diskList.Size = New-Object System.Drawing.Size(280, 438)
$diskList.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$diskList.View = [System.Windows.Forms.View]::Details
$diskList.FullRowSelect = $true
$diskList.HideSelection = $false
$diskList.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$null = $diskList.Columns.Add("Disk", 180)
$null = $diskList.Columns.Add("Health", 78)
$leftPanel.Controls.AddRange(@($diskListTitle, $diskList))

$mainPanel = New-Object System.Windows.Forms.Panel
$mainPanel.Location = New-Object System.Drawing.Point(348, 88)
$mainPanel.Size = New-Object System.Drawing.Size(676, 505)
$mainPanel.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$mainPanel.BackColor = [System.Drawing.Color]::White
$mainPanel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle

$diskNameLabel = New-Label "Select a disk" 24 22 600 32 15 ([System.Drawing.FontStyle]::Bold)
$diskMetaLabel = New-Label "" 26 54 600 22 9 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(107, 114, 128))

$healthText = New-Label "Health" 26 92 120 22 10 ([System.Drawing.FontStyle]::Bold)
$healthPercentLabel = New-Label "..." 575 92 70 22 10 ([System.Drawing.FontStyle]::Bold)
$barBack = New-Object System.Windows.Forms.Panel
$barBack.Location = New-Object System.Drawing.Point(26, 120)
$barBack.Size = New-Object System.Drawing.Size(520, 16)
$barBack.BackColor = [System.Drawing.Color]::FromArgb(229, 231, 235)
$barFill = New-Object System.Windows.Forms.Panel
$barFill.Location = New-Object System.Drawing.Point(0, 0)
$barFill.Size = New-Object System.Drawing.Size(0, 16)
$barBack.Controls.Add($barFill)

$healthNote = New-Label "" 26 146 610 24 9 ([System.Drawing.FontStyle]::Regular) ([System.Drawing.Color]::FromArgb(55, 65, 81))

$cardHealth = New-MetricCard "Disk health" 26 190
$cardTemp = New-MetricCard "Temperature" 246 190
$cardHours = New-MetricCard "Power-on time" 466 190
$cardWrites = New-MetricCard "Lifetime writes" 26 296
$cardWear = New-MetricCard "Wear value" 246 296
$cardStatus = New-MetricCard "Windows status" 466 296

$detailsBox = New-Object System.Windows.Forms.TextBox
$detailsBox.Location = New-Object System.Drawing.Point(26, 418)
$detailsBox.Size = New-Object System.Drawing.Size(620, 58)
$detailsBox.Anchor = [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right -bor [System.Windows.Forms.AnchorStyles]::Bottom
$detailsBox.Multiline = $true
$detailsBox.ReadOnly = $true
$detailsBox.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$detailsBox.BackColor = [System.Drawing.Color]::FromArgb(249, 250, 251)
$detailsBox.ForeColor = [System.Drawing.Color]::FromArgb(55, 65, 81)

$mainPanel.Controls.AddRange(@(
    $diskNameLabel, $diskMetaLabel, $healthText, $healthPercentLabel, $barBack, $healthNote,
    $cardHealth.Panel, $cardTemp.Panel, $cardHours.Panel, $cardWrites.Panel, $cardWear.Panel, $cardStatus.Panel,
    $detailsBox
))

$form.Controls.AddRange(@($header, $leftPanel, $mainPanel))

function Set-Card {
    param(
        $Card,
        [string] $Value,
        [string] $Hint = "",
        [System.Drawing.Color] $Color = [System.Drawing.Color]::FromArgb(17, 24, 39)
    )

    $Card.Value.Text = $Value
    $Card.Value.ForeColor = $Color
    $Card.Hint.Text = $Hint
}

function Show-DiskDetails {
    param($Disk)

    if ($null -eq $Disk) {
        $diskNameLabel.Text = "No disk selected"
        $diskMetaLabel.Text = ""
        $healthPercentLabel.Text = "--"
        Set-BarValue $barFill $null
        $healthNote.Text = ""
        foreach ($card in @($cardHealth, $cardTemp, $cardHours, $cardWrites, $cardWear, $cardStatus)) {
            Set-Card $card "Not reported" ""
        }
        $detailsBox.Text = ""
        return
    }

    $diskNameLabel.Text = $Disk.Name
    $diskMetaLabel.Text = "Disk $($Disk.Id)  |  $($Disk.MediaType)  |  $($Disk.BusType)  |  $(Format-Size $Disk.Size)  |  $($Disk.DriveLetters)"

    if ($null -ne $Disk.HealthPercent) {
        $healthPercentLabel.Text = "$($Disk.HealthPercent)%"
        Set-BarValue $barFill ([int]$Disk.HealthPercent)
        Set-Card $cardHealth "$($Disk.HealthPercent)%" "100 - wear value"
    } else {
        $healthPercentLabel.Text = "N/A"
        Set-BarValue $barFill $null
        Set-Card $cardHealth "Not reported" "Wear value unavailable"
    }

    if ($Disk.HealthPercent -ge 80) {
        $healthNote.Text = "Disk looks healthy based on the counters Windows reports."
    } elseif ($null -eq $Disk.HealthPercent) {
        $healthNote.Text = "Windows did not report a wear value for this disk."
    } elseif ($Disk.HealthPercent -ge 50) {
        $healthNote.Text = "Health is reduced. Keep backups current and watch this drive."
    } else {
        $healthNote.Text = "Health is low. Back up important data and consider replacement."
    }

    $tempColor = [System.Drawing.Color]::FromArgb(17, 24, 39)
    if ($null -ne $Disk.Temperature -and [int]$Disk.Temperature -ge 55) {
        $tempColor = [System.Drawing.Color]::FromArgb(220, 38, 38)
    }

    if ($null -ne $Disk.Temperature) {
        Set-Card $cardTemp "$($Disk.Temperature) °C" "Current sensor reading" $tempColor
    } else {
        Set-Card $cardTemp "Not reported" "Sensor unavailable"
    }

    Set-Card $cardHours (Format-Hours $Disk.PowerOnHours) "Total powered-on time"
    Set-Card $cardWrites (Format-Bytes $Disk.LifetimeWrites) "Host bytes written"

    if ($null -ne $Disk.Wear) {
        Set-Card $cardWear "$($Disk.Wear)%" "Health = 100 - wear"
    } else {
        Set-Card $cardWear "Not reported" "Often missing on HDDs"
    }

    $statusColor = [System.Drawing.Color]::FromArgb(22, 163, 74)
    if ($Disk.HealthStatus -ne "Healthy" -or $Disk.Operational -notmatch "OK|Online") {
        $statusColor = [System.Drawing.Color]::FromArgb(220, 38, 38)
    }
    Set-Card $cardStatus $Disk.HealthStatus $Disk.Operational $statusColor

    $lines = @(
        "Serial: $($Disk.Serial)",
        "Refreshes automatically every 10 seconds. External drives appear after Windows detects them."
    )
    if ($Disk.CounterError) {
        $lines += "Counter note: $($Disk.CounterError)"
    }
    $detailsBox.Text = ($lines -join [Environment]::NewLine)
}

function Refresh-Disks {
    $refreshButton.Enabled = $false
    $statusLabel.Text = "Refreshing..."

    try {
        $previousId = $script:SelectedDiskId
        $script:Disks = @(Get-DiskSnapshot)

        $diskList.BeginUpdate()
        $diskList.Items.Clear()

        foreach ($disk in $script:Disks) {
            $healthTextValue = if ($null -ne $disk.HealthPercent) { "$($disk.HealthPercent)%" } else { "N/A" }
            $item = New-Object System.Windows.Forms.ListViewItem("Disk $($disk.Id): $($disk.Name)")
            $null = $item.SubItems.Add($healthTextValue)
            $item.Tag = $disk.Id

            if ($null -ne $disk.HealthPercent -and $disk.HealthPercent -lt 50) {
                $item.ForeColor = [System.Drawing.Color]::FromArgb(185, 28, 28)
            }

            $null = $diskList.Items.Add($item)
        }

        $diskList.EndUpdate()

        $selected = $script:Disks | Where-Object { $_.Id -eq $previousId } | Select-Object -First 1
        if ($null -eq $selected) {
            $selected = $script:Disks | Select-Object -First 1
        }

        if ($null -ne $selected) {
            $script:SelectedDiskId = $selected.Id
            foreach ($item in $diskList.Items) {
                if ($item.Tag -eq $selected.Id) {
                    $item.Selected = $true
                    $item.Focused = $true
                    break
                }
            }
            Show-DiskDetails $selected
        } else {
            Show-DiskDetails $null
        }

        $statusLabel.Text = "Updated $(Get-Date -Format 'HH:mm:ss')"
    } catch {
        $statusLabel.Text = "Refresh failed"
        [System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message,
            "Disk Health Lite",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    } finally {
        $refreshButton.Enabled = $true
    }
}

$diskList.Add_SelectedIndexChanged({
    if ($diskList.SelectedItems.Count -eq 0) { return }
    $id = [int]$diskList.SelectedItems[0].Tag
    $script:SelectedDiskId = $id
    $selected = $script:Disks | Where-Object { $_.Id -eq $id } | Select-Object -First 1
    Show-DiskDetails $selected
})

$refreshButton.Add_Click({ Refresh-Disks })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 10000
$timer.Add_Tick({ Refresh-Disks })
$timer.Start()

$form.Add_Shown({ Refresh-Disks })
$form.Add_FormClosed({ $timer.Stop(); $timer.Dispose() })

[System.Windows.Forms.Application]::Run($form)
