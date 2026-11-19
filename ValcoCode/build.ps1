# Builds Valco Code and its MSI installer.  Usage:  .\build.ps1   (or double-click build.cmd)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$log = Join-Path $PSScriptRoot 'build.log'
"=== Build started $(Get-Date)" | Out-File $log -Encoding utf8

function Step($name, [scriptblock]$cmd) {
    Write-Host "`n== $name" -ForegroundColor Cyan
    "`n== $name" | Out-File $log -Append -Encoding utf8
    & $cmd 2>&1 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { "BUILD FAILED at: $name" | Out-File $log -Append -Encoding utf8; throw "Failed: $name" }
}

$sdkOk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $sdkOk = (dotnet --list-sdks) -match '^(8|9|10)\.' }
if (-not $sdkOk) {
    Write-Host "Installing the .NET 8 SDK for this user..." -ForegroundColor Yellow
    $dir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$env:TEMP\dotnet-install.ps1"
    & "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir $dir
    $env:PATH = "$dir;$env:PATH"
    $env:DOTNET_ROOT = $dir
}

# Formatters bundled with Valco Code (downloaded once from their GitHub releases)
New-Item -ItemType Directory -Force tools | Out-Null
$ProgressPreference = 'SilentlyContinue'
$ruffOk = $false
try { if (Test-Path tools\ruff.exe) { & tools\ruff.exe --version | Out-Null; $ruffOk = ($LASTEXITCODE -eq 0) } } catch { }
if (-not $ruffOk) {
    Write-Host "Downloading Ruff (Python formatter)..." -ForegroundColor Yellow
    Invoke-WebRequest 'https://github.com/astral-sh/ruff/releases/download/0.17.5/ruff-x86_64-pc-windows-msvc.zip' -OutFile "$env:TEMP\ruff.zip"
    Expand-Archive "$env:TEMP\ruff.zip" -DestinationPath "$env:TEMP\ruff" -Force
    Copy-Item "$env:TEMP\ruff\ruff.exe" tools\ruff.exe -Force
}
if (-not (Test-Path tools\google-java-format.exe) -or (Get-Item tools\google-java-format.exe).Length -lt 20MB) {
    Write-Host "Downloading google-java-format (Java formatter)..." -ForegroundColor Yellow
    Invoke-WebRequest 'https://github.com/google/google-java-format/releases/download/v1.33.0/google-java-format_windows-x86-64.exe' -OutFile tools\google-java-format.exe
}

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
Step 'Publishing app' { dotnet publish ValcoCode.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o publish -nologo -clp:NoSummary }

New-Item -ItemType Directory -Force publish\tools | Out-Null
Copy-Item tools\ruff.exe, tools\google-java-format.exe publish\tools\ -Force

if (Test-Path dist) { Remove-Item dist -Recurse -Force }
Step 'Building MSI' { dotnet build installer\ValcoCode.Installer.wixproj -c Release -o dist -nologo -clp:NoSummary }

$msi = Get-ChildItem dist\*.msi | Select-Object -First 1
"BUILD SUCCEEDED: $($msi.FullName)" | Out-File $log -Append -Encoding utf8
Write-Host "`nDone! Installer: $($msi.FullName)" -ForegroundColor Green
