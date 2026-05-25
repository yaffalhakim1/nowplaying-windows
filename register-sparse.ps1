# register-sparse.ps1
# One-time setup: grants package identity so NowOnTaskbar can read notifications.
# Run from the project root (folder containing Package.appxmanifest).

$ErrorActionPreference = "Stop"

# Detect app directory (folder containing this script)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "App directory: $scriptDir" -ForegroundColor Cyan

# ─── Step 0: add MakeAppx + SignTool to PATH ───
$kitsPath = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit"
if (-not (Test-Path "$kitsPath\makeappx.exe")) {
    Write-Host "ERROR: Windows 10 SDK not found at $kitsPath" -ForegroundColor Red
    Write-Host "  Install: winget install Microsoft.WindowsSDK" -ForegroundColor Yellow
    exit 1
}
$env:Path += ";$kitsPath"

# ─── Step 1: self-signed certificate ───
$certName = "CN=NowOnTaskbar"
$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object Subject -eq $certName | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigning -Subject $certName -CertStoreLocation Cert:\CurrentUser\My
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "Certificate exists: $($cert.Thumbprint)" -ForegroundColor Cyan
}

# ─── Step 2: placeholder assets ───
$assetsDir = Join-Path $scriptDir "Assets"
if (-not (Test-Path "$assetsDir\Square150x150Logo.png")) {
    Write-Host "Generating placeholder assets..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.Drawing
    foreach ($sz in @(150, 44, 155)) {
        $name = @{ 150="Square150x150Logo"; 44="Square44x44Logo"; 155="storelogo" }[$sz]
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
        $bmp.Save("$assetsDir\$name.png")
        $bmp.Dispose()
    }
}

# ─── Step 3: build MSIX (MakeAppx requires a stub exe) ───
$pkgDir = Join-Path $env:TEMP "nowontaskbar-pkg"
Remove-Item -LiteralPath $pkgDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "$pkgDir\Assets" -Force | Out-Null

Copy-Item "$scriptDir\Package.appxmanifest" "$pkgDir\AppxManifest.xml" -Force
Copy-Item "$scriptDir\Assets\*.png" "$pkgDir\Assets\" -Force

# MakeAppx validates exe exists; use build output exe as stub
$exeSource = "$scriptDir\bin\Release\net9.0-windows10.0.19041.0\NowOnTaskbar.exe"
$exeSource2 = "$scriptDir\bin\Release\net9.0-windows10.0.19041.0\win-x64\NowOnTaskbar.exe"
if (Test-Path $exeSource) {
    Copy-Item $exeSource "$pkgDir\NowOnTaskbar.exe" -Force
} elseif (Test-Path $exeSource2) {
    Copy-Item $exeSource2 "$pkgDir\NowOnTaskbar.exe" -Force
} else {
    Write-Host "ERROR: Build output exe not found. Run 'dotnet build -c Release' first." -ForegroundColor Red
    exit 1
}

$msixPath = Join-Path $env:TEMP "NowOnTaskbar.msix"
$mapFile = Join-Path $env:TEMP "nowontaskbar-map.txt"

$files = Get-ChildItem -Path $pkgDir -Recurse -File
$mapContent = "[Files]`n"
foreach ($f in $files) {
    $rel = $f.FullName.Substring($pkgDir.Length + 1) -replace '\\', '/'
    $mapContent += "`"$($f.FullName)`" `"$rel`"`n"
}
$mapContent | Out-File -FilePath $mapFile -Encoding utf8 -NoNewline

Write-Host "Building MSIX..." -ForegroundColor Cyan
& makeappx pack /p $msixPath /f $mapFile 2>&1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ─── Step 4: sign MSIX ───
Write-Host "Signing MSIX..." -ForegroundColor Cyan
& signtool sign /fd SHA256 /a /s MY /sha1 $cert.Thumbprint $msixPath 2>&1

# ─── Step 5: trust certificate (requires admin once) ───
$thumb = $cert.Thumbprint
$trusted = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $thumb | Select-Object -First 1
if (-not $trusted) {
    Write-Host "Installing certificate to machine TrustedPeople store (admin required)..." -ForegroundColor Cyan
    Export-Certificate -Cert $cert -FilePath "$env:TEMP\NowOnTaskbar.cer" -Force | Out-Null
    Start-Process -FilePath "certutil.exe" -ArgumentList "-addstore TrustedPeople $env:TEMP\NowOnTaskbar.cer" -Verb RunAs -Wait
}

# ─── Step 6: register sparse package ───
Write-Host "Registering package..." -ForegroundColor Cyan
Add-AppxPackage -Path $msixPath -ExternalLocation $scriptDir

# ─── Done ───
Write-Host ""
Write-Host "✓ Package identity registered." -ForegroundColor Green
Write-Host "  Start NowOnTaskbar, then send a test notification." -ForegroundColor Cyan
Write-Host "  On first run, Windows will ask: 'Let NowOnTaskbar read your notifications?'" -ForegroundColor Cyan
Write-Host "  Uninstall: Get-AppxPackage -Name NowOnTaskbar | Remove-AppxPackage" -ForegroundColor DarkGray
