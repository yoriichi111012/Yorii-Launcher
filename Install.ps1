# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------
$ErrorActionPreference = "Stop"

$Owner      = "yoriichi111012"
$Repository = "Yorii-Launcher"

$CertificatePath = Join-Path $env:TEMP "Yorii Launcher.cer"

$Headers = @{
    "User-Agent" = "Yorii Launcher Installer"
}

# -----------------------------------------------------------------------------
# Helper Functions
# -----------------------------------------------------------------------------
function Write-Info($Message) {
    Write-Host "[INFO]    $Message" -ForegroundColor Cyan
}

function Write-Success($Message) {
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green
}

function Write-ErrorAndExit($Message) {
    Write-Host ""
    Write-Host "[ERROR]   $Message" -ForegroundColor Red
    Write-Host ""
    Pause
    exit 1
}

# -----------------------------------------------------------------------------
# Banner
# -----------------------------------------------------------------------------
Clear-Host

Write-Host ""
Write-Host "==================================================" -ForegroundColor Magenta
Write-Host "              Yorii Launcher Installer            " -ForegroundColor White
Write-Host "==================================================" -ForegroundColor Magenta
Write-Host ""

# -----------------------------------------------------------------------------
# Enable TLS
# -----------------------------------------------------------------------------
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ([Enum]::GetNames([Net.SecurityProtocolType]) -contains "Tls13") {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.SecurityProtocolType]::Tls12 -bor `
        [Net.SecurityProtocolType]::Tls13
}

# -----------------------------------------------------------------------------
# Internet Check
# -----------------------------------------------------------------------------
Write-Info "Checking internet connection..."

try {
    Invoke-WebRequest `
        -Uri "https://api.github.com" `
        -Method Head `
        -Headers $Headers `
        -UseBasicParsing `
        -TimeoutSec 10 | Out-Null
}
catch {
    Write-ErrorAndExit "No internet connection or GitHub is unreachable."
}

Write-Success "Internet connection verified."

# -----------------------------------------------------------------------------
# Download Certificate
# -----------------------------------------------------------------------------
Write-Info "Downloading signing certificate..."

$CertUrl = "https://raw.githubusercontent.com/yoriichi111012/Yorii-Launcher/main/Yorii-Launcher-Certificate.cer"

try {
    Invoke-WebRequest `
        -Uri $CertUrl `
        -Headers $Headers `
        -UseBasicParsing `
        -OutFile $CertificatePath `
        -MaximumRedirection 5

    if (!(Test-Path $CertificatePath) -or (Get-Item $CertificatePath).Length -eq 0) {
        Write-ErrorAndExit "Downloaded certificate file is empty or missing."
    }
}
catch {
    Write-ErrorAndExit "Failed to download signing certificate: $_"
}

# -----------------------------------------------------------------------------
# Certificate Verification & Installation
# -----------------------------------------------------------------------------
Write-Info "Checking signing certificate..."

try {
    $Certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath)

    if ($Certificate.NotAfter -lt (Get-Date)) {
        Write-ErrorAndExit "The signing certificate expired on $($Certificate.NotAfter)."
    }

    $Thumbprint = $Certificate.Thumbprint

    $InstalledCertificate = Get-ChildItem Cert:\LocalMachine\Root -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $Thumbprint

    if ($null -eq $InstalledCertificate) {
        Write-Info "Installing signing certificate (Admin Prompt required)..."

        # Targeted Elevation: Spawns a hidden UAC prompt just for importing the certificate
        $CertCommand = "Import-Certificate -FilePath '$CertificatePath' -CertStoreLocation Cert:\LocalMachine\Root"

        try {
            Start-Process powershell.exe -ArgumentList "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command `"$CertCommand`"" -Verb RunAs -Wait -ErrorAction Stop
            Write-Success "Certificate installed."
        }
        catch {
            Write-ErrorAndExit "Failed to install certificate (UAC Prompt denied or error occurred)."
        }
    }
    else {
        Write-Success "Certificate already installed."
    }
}
catch {
    Write-ErrorAndExit "Certificate processing failed: $_"
}
finally {
    if ($Certificate -is [System.IDisposable]) {
        $Certificate.Dispose()
    }
    if (Test-Path $CertificatePath) {
        Remove-Item $CertificatePath -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------------------
# Architecture
# -----------------------------------------------------------------------------
Write-Info "Detecting system architecture..."

if ([Environment]::Is64BitOperatingSystem) {
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        $Architecture = "arm64"
    }
    else {
        $Architecture = "x64"
    }
}
else {
    Write-ErrorAndExit "Yorii Launcher requires a 64-bit version of Windows."
}

Write-Success "Architecture: $Architecture"

# -----------------------------------------------------------------------------
# Latest Release
# -----------------------------------------------------------------------------
Write-Info "Fetching latest release..."

try {
    $Release = Invoke-RestMethod `
        -Uri "https://api.github.com/repos/$Owner/$Repository/releases/latest" `
        -Headers $Headers
}
catch {
    Write-ErrorAndExit "Unable to retrieve the latest release from GitHub."
}

Write-Success "Latest version: $($Release.tag_name)"

# -----------------------------------------------------------------------------
# Find Package
# -----------------------------------------------------------------------------
$Package =
    $Release.assets |
    Where-Object {
        $_.name -match "^Yorii\.Launcher_.*_${Architecture}\.msix$"
    } |
    Select-Object -First 1

if ($null -eq $Package) {
    Write-ErrorAndExit @"
No compatible package found.

Release : $($Release.tag_name)
Architecture : $Architecture
"@
}

Write-Success "Package: $($Package.name)"

# -----------------------------------------------------------------------------
# Download (High Speed Stream + Real-Time Progress Bar)
# -----------------------------------------------------------------------------
$DownloadPath = Join-Path $env:TEMP $Package.name

Write-Info "Downloading package..."

$ResponseStream = $null
$FileStream = $null
$Response = $null

try {
    # Create Native WebRequest (No external assembly loading required)
    $Request = [System.Net.HttpWebRequest]::Create($Package.browser_download_url)
    $Request.UserAgent = "Yorii Launcher Installer"
    $Request.Timeout = 600000 # 10 Minutes
    $Request.AllowAutoRedirect = $true

    $Response = $Request.GetResponse()
    $TotalBytes = $Response.ContentLength

    $ResponseStream = $Response.GetResponseStream()
    $FileStream = New-Object System.IO.FileStream($DownloadPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)

    $Buffer = New-Object byte[] 65536 # 64 KB Buffer
    $TotalBytesRead = 0L
    $Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $LastUiUpdate = [System.Diagnostics.Stopwatch]::StartNew()

    while (($BytesRead = $ResponseStream.Read($Buffer, 0, $Buffer.Length)) -gt 0) {
        $FileStream.Write($Buffer, 0, $BytesRead)
        $TotalBytesRead += $BytesRead

        # Throttle progress bar to once every 200ms for max throughput
        if ($LastUiUpdate.ElapsedMilliseconds -ge 200 -or $TotalBytesRead -eq $TotalBytes) {
            $ElapsedSeconds = [Math]::Max($Stopwatch.Elapsed.TotalSeconds, 0.001)
            $SpeedBps = $TotalBytesRead / $ElapsedSeconds
            $SpeedMBps = $SpeedBps / 1MB
            $DownloadedMB = $TotalBytesRead / 1MB

            if ($TotalBytes -gt 0) {
                $TotalMB = $TotalBytes / 1MB
                $Percent = [Math]::Min(100, [int](($TotalBytesRead / $TotalBytes) * 100))
                $RemainingBytes = $TotalBytes - $TotalBytesRead
                $EtaSeconds = if ($SpeedBps -gt 0) { [int]($RemainingBytes / $SpeedBps) } else { 0 }

                $EtaString = if ($EtaSeconds -ge 60) {
                    "{0}m {1}s" -f [math]::Floor($EtaSeconds / 60), ($EtaSeconds % 60)
                } else {
                    "{0}s" -f $EtaSeconds
                }

                $StatusText = "{0:N1} MB / {1:N1} MB ({2:N2} MB/s) - ETA: {3}" -f $DownloadedMB, $TotalMB, $SpeedMBps, $EtaString
                Write-Progress -Activity "Downloading Yorii Launcher" -Status $StatusText -PercentComplete $Percent
            } else {
                $StatusText = "{0:N1} MB downloaded ({1:N2} MB/s)" -f $DownloadedMB, $SpeedMBps
                Write-Progress -Activity "Downloading Yorii Launcher" -Status $StatusText -PercentComplete -1
            }

            $LastUiUpdate.Restart()
        }
    }
}
catch {
    Write-ErrorAndExit ("Download failed: " + $_.Exception.Message)
}
finally {
    Write-Progress -Activity "Downloading Yorii Launcher" -Completed
    if ($null -ne $FileStream) { $FileStream.Dispose() }
    if ($null -ne $ResponseStream) { $ResponseStream.Dispose() }
    if ($null -ne $Response) { $Response.Close() }
}

if (!(Test-Path $DownloadPath)) {
    Write-ErrorAndExit "The package could not be downloaded."
}

$DownloadedSize = (Get-Item $DownloadPath).Length

if ($null -ne $Package.size -and $DownloadedSize -ne $Package.size) {
    Remove-Item $DownloadPath -Force -ErrorAction SilentlyContinue
    Write-ErrorAndExit "Downloaded package is incomplete."
}

Write-Success "Download completed."

# -----------------------------------------------------------------------------
# Install
# -----------------------------------------------------------------------------
Write-Info "Installing Yorii Launcher..."

try {
    $AppInstaller = Get-AppxPackage Microsoft.DesktopAppInstaller -ErrorAction SilentlyContinue

    if ($null -eq $AppInstaller) {
        Write-ErrorAndExit "Microsoft App Installer is not installed."
    }

    Add-AppxPackage -Path $DownloadPath -ForceUpdateFromAnyVersion -ForceApplicationShutdown
}
catch {
    Remove-Item $DownloadPath -Force -ErrorAction SilentlyContinue
    Write-ErrorAndExit $_.Exception.Message
}

Write-Success "Installation completed."

# -----------------------------------------------------------------------------
# Cleanup
# -----------------------------------------------------------------------------
Remove-Item $DownloadPath -Force -ErrorAction SilentlyContinue

# -----------------------------------------------------------------------------
# Finished
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "==================================================" -ForegroundColor Magenta
Write-Host "  Yorii Launcher has been installed successfully! " -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Magenta
Write-Host ""
Read-Host "Press Enter to exit"
