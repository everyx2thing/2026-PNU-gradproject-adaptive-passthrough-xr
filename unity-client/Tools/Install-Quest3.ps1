[CmdletBinding()]
param(
    [string]$AdbPath,
    [string]$Serial,
    [string]$ApkPath,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApkPath)) {
    $ApkPath = Join-Path $PSScriptRoot "..\Builds\Android\AdaptivePassthrough.apk"
}

function Find-Adb {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $pathCommand = Get-Command "adb.exe" -ErrorAction SilentlyContinue
    if ($pathCommand) {
        return $pathCommand.Source
    }

    $candidates = @()
    if ($env:ANDROID_SDK_ROOT) {
        $candidates += Join-Path $env:ANDROID_SDK_ROOT "platform-tools\adb.exe"
    }
    if ($env:ANDROID_HOME) {
        $candidates += Join-Path $env:ANDROID_HOME "platform-tools\adb.exe"
    }

    $projectVersionPath = Join-Path $PSScriptRoot "..\ProjectSettings\ProjectVersion.txt"
    if (Test-Path -LiteralPath $projectVersionPath) {
        $versionLine = Get-Content -LiteralPath $projectVersionPath |
            Where-Object { $_ -match "^m_EditorVersion:\s+(.+)$" } |
            Select-Object -First 1
        if ($versionLine -match "^m_EditorVersion:\s+(.+)$") {
            $unityVersion = $Matches[1]
            $candidates += "C:\Unity Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
        }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "adb.exe was not found. Pass the Unity Android SDK adb.exe path with -AdbPath."
}

$resolvedAdb = Find-Adb -ExplicitPath $AdbPath
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path

$adbOutput = @(& $resolvedAdb devices -l)
$deviceLines = @(
    $adbOutput |
        Select-Object -Skip 1 |
        Where-Object { $_ -match "^([^\s]+)\s+device(?:\s|$)" }
)
$unauthorizedLines = @(
    $adbOutput |
        Select-Object -Skip 1 |
        Where-Object { $_ -match "^([^\s]+)\s+unauthorized(?:\s|$)" }
)
$offlineLines = @(
    $adbOutput |
        Select-Object -Skip 1 |
        Where-Object { $_ -match "^([^\s]+)\s+offline(?:\s|$)" }
)

if (-not $deviceLines) {
    if ($unauthorizedLines) {
        throw "Quest was detected but USB debugging is unauthorized. Put on the headset, accept the USB debugging prompt, then retry."
    }

    if ($offlineLines) {
        throw "Quest was detected but ADB reports it as offline. Reconnect the USB cable, then run 'adb reconnect' and retry."
    }

    throw "No Quest was detected by ADB. Enable Developer Mode and USB debugging, reconnect a data-capable USB cable, then retry."
}

$connectedSerials = @(
    $deviceLines | ForEach-Object {
        if ($_ -match "^([^\s]+)\s+device(?:\s|$)") {
            $Matches[1]
        }
    }
)

function Get-DeviceIdentity {
    param(
        [string]$Adb,
        [string]$DeviceSerial
    )

    $manufacturer = (
        & $Adb -s $DeviceSerial shell getprop ro.product.manufacturer
    ).Trim()
    $model = (
        & $Adb -s $DeviceSerial shell getprop ro.product.model
    ).Trim()
    $device = (
        & $Adb -s $DeviceSerial shell getprop ro.product.device
    ).Trim()

    [PSCustomObject]@{
        Serial = $DeviceSerial
        Manufacturer = $manufacturer
        Model = $model
        Device = $device
        IsQuest = (
            $manufacturer -match "Meta|Oculus" -or
            $model -match "Quest" -or
            $device -match "eureka|panther|hollywood"
        )
    }
}

if ($Serial) {
    if ($connectedSerials -notcontains $Serial) {
        throw "The requested device '$Serial' was not found in the authorized ADB device list."
    }
    $selectedSerial = $Serial
}
elseif ($connectedSerials.Count -eq 1) {
    $selectedSerial = $connectedSerials[0]
}
else {
    $identities = @(
        $connectedSerials | ForEach-Object {
            Get-DeviceIdentity -Adb $resolvedAdb -DeviceSerial $_
        }
    )
    $questDevices = @($identities | Where-Object { $_.IsQuest })
    if ($questDevices.Count -eq 1) {
        $selectedSerial = $questDevices[0].Serial
        Write-Host (
            "Automatically selected Quest: {0} ({1}, {2})" -f
            $selectedSerial,
            $questDevices[0].Manufacturer,
            $questDevices[0].Model
        )
    }
    else {
        $deviceSummary = $identities | ForEach-Object {
            "{0} [{1} {2}]" -f $_.Serial, $_.Manufacturer, $_.Model
        }
        throw "Multiple devices are connected and one Quest could not be selected automatically. Use -Serial. Devices: $($deviceSummary -join ', ')"
    }
}

$deviceArgs = @("-s", $selectedSerial)
& $resolvedAdb @deviceArgs install -r $resolvedApk
if ($LASTEXITCODE -ne 0) {
    throw "APK installation failed (adb exit code $LASTEXITCODE)."
}

Write-Host "Installation completed: $resolvedApk"
Write-Host "When the app opens on Quest, allow the headset camera permission."

if ($Launch) {
    & $resolvedAdb @deviceArgs shell monkey `
        -p "com.pnu.teamvr.adaptivepassthrough" `
        -c "android.intent.category.LAUNCHER" 1
    if ($LASTEXITCODE -ne 0) {
        throw "Automatic app launch failed (adb exit code $LASTEXITCODE)."
    }
}
