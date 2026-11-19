# Builds Valco Word and its MSI installer.  Usage:  .\build.ps1
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

# .NET 8 SDK (installed per-user if missing, no admin needed)
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

# Clear the old build. A copy of the app that is still running can't be deleted, but it can be moved aside.
if (Test-Path publish) {
    foreach ($f in [IO.Directory]::GetFiles((Resolve-Path publish).Path, '*', 'AllDirectories')) {
        try { [IO.File]::Delete($f) } catch { try { [IO.File]::Move($f, (Join-Path $env:TEMP ("valcoword-old-" + [guid]::NewGuid().ToString('N')))) } catch { } }
    }
}
Step 'Publishing app' { dotnet publish src\ValcoWord\ValcoWord.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o publish }

Step 'Building MSI' { dotnet build installer\ValcoWord.Installer.wixproj -c Release -o dist }

$msi = Get-ChildItem dist\*.msi | Select-Object -First 1
"BUILD SUCCEEDED: $($msi.FullName)" | Out-File $log -Append -Encoding utf8
Write-Host "`nDone! Installer: $($msi.FullName)" -ForegroundColor Green
