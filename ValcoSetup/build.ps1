# Builds Valco Office Setup and packs every Valco app into it.
#   .\build.ps1              package the apps as they are built now
#   .\build.ps1 -BuildApps   rebuild every app first (runs each app's own build.ps1)
param([switch]$BuildApps)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem
$log = Join-Path $PSScriptRoot 'build.log'
"=== Build started $(Get-Date)" | Out-File $log -Encoding utf8
function Log($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c; $t | Out-File $log -Append -Encoding utf8 }
function Step($name, [scriptblock]$cmd) {
    Log "`n== $name" Cyan
    & $cmd 2>&1 | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { "BUILD FAILED at: $name" | Out-File $log -Append -Encoding utf8; throw "Failed: $name" }
}

# .NET 8 SDK (installed per-user if missing, no admin needed)
$sdkOk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $sdkOk = (dotnet --list-sdks) -match '^(8|9|10)\.' }
if (-not $sdkOk) {
    $dir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$env:TEMP\dotnet-install.ps1"
    & "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir $dir
    $env:PATH = "$dir;$env:PATH"; $env:DOTNET_ROOT = $dir
}

$cfg = Get-Content apps.json -Raw | ConvertFrom-Json

if ($BuildApps) {
    foreach ($a in $cfg.apps) {
        $root = Resolve-Path (Join-Path $PSScriptRoot ([IO.Path]::GetDirectoryName($a.source) -replace '\\(publish|dist)$','')) -ErrorAction SilentlyContinue
        if ($root -and (Test-Path (Join-Path $root 'build.ps1'))) {
            Log "`n== Building $($a.name)" Cyan
            & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') 2>&1 | Tee-Object -FilePath $log -Append
        }
    }
}

# ---------------- setup.exe ----------------
if (Test-Path out) {
    foreach ($f in [IO.Directory]::GetFiles((Resolve-Path out).Path, '*', 'AllDirectories')) {
        try { [IO.File]::Delete($f) } catch { try { [IO.File]::Move($f, (Join-Path $env:TEMP ("valcosetup-old-" + [guid]::NewGuid().ToString('N')))) } catch { } }
    }
}
Step 'Building Valco Office Setup' { dotnet publish src\ValcoSetup\ValcoSetup.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out }

# ---------------- payload ----------------
$dist = Join-Path $PSScriptRoot 'dist\Valco Office'
$payload = Join-Path $dist 'payload'
$release = Join-Path $PSScriptRoot 'release'
foreach ($d in @($dist, $release)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $payload, $release | Out-Null

function Revision([datetime]$t) { [int](($t.ToUniversalTime() - [datetime]'2026-01-01').TotalMinutes) }
function BaseVersion($a) {
    $p = Join-Path $PSScriptRoot $a.versionFrom
    if (-not (Test-Path $p)) { return '1.0.0' }
    $text = Get-Content $p -Raw
    if ($p -like '*.json') { return ($text | ConvertFrom-Json).version }
    if ($text -match '<Version>([^<]+)</Version>') { return $Matches[1] }
    return '1.0.0'
}
function Sha($f) { (Get-FileHash $f -Algorithm SHA256).Hash }

$apps = @()
foreach ($a in $cfg.apps) {
    $src = Join-Path $PSScriptRoot $a.source
    if (-not (Test-Path $src)) { Log "Skipping $($a.name): not built yet ($src)" Yellow; continue }
    $file = ($a.exe -replace '\.exe$','') + $(if ($a.kind -eq 'msi') { '.msi' } else { '.zip' })
    $dest = Join-Path $payload $file
    if ($a.kind -eq 'msi') {
        Copy-Item $src $dest
        $stamp = (Get-Item $src).LastWriteTime
        $installSize = (Get-Item $src).Length * 3
    } else {
        $files = Get-ChildItem $src -File -Recurse | Where-Object { $_.Extension -notin '.pdb', '.xml' }
        if (-not $files) { Log "Skipping $($a.name): $src is empty" Yellow; continue }
        $tmp = Join-Path $env:TEMP ("valco-pack-" + $a.id)
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
        foreach ($f in $files) {
            $rel = $f.FullName.Substring((Resolve-Path $src).Path.Length).TrimStart('\')
            $to = Join-Path $tmp $rel
            New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
            Copy-Item $f.FullName $to
        }
        [IO.Compression.ZipFile]::CreateFromDirectory($tmp, $dest, [IO.Compression.CompressionLevel]::Optimal, $false)
        Remove-Item $tmp -Recurse -Force
        $stamp = ($files | Where-Object Name -eq $a.exe | Select-Object -First 1).LastWriteTime
        if (-not $stamp) { $stamp = ($files | Measure-Object LastWriteTime -Maximum).Maximum }
        $installSize = ($files | Measure-Object Length -Sum).Sum
    }
    # base version from the project + a revision from the build time, so every rebuild is offered as an update
    $base = (BaseVersion $a) -split '\.'
    while ($base.Count -lt 3) { $base += '0' }
    $version = "$($base[0]).$($base[1]).$($base[2]).$(Revision $stamp)"
    $iconName = "$($a.id).ico"
    if ($a.icon -and (Test-Path (Join-Path $PSScriptRoot $a.icon))) { Copy-Item (Join-Path $PSScriptRoot $a.icon) (Join-Path $payload $iconName) } else { $iconName = $null }
    $entry = [ordered]@{
        id = $a.id; name = $a.name; version = $version; description = $a.description; kind = $a.kind; file = $file
        sha256 = (Sha $dest); downloadSize = (Get-Item $dest).Length; installSize = [long]$installSize
        folder = $a.folder; exe = $a.exe; process = $a.process; exitArgs = $a.exitArgs; icon = $iconName
        shortcutDescription = $a.shortcutDescription; defaultSelected = $(if ($null -eq $a.defaultSelected) { $true } else { [bool]$a.defaultSelected })
        desktop = $(if ($null -eq $a.desktop) { $true } else { [bool]$a.desktop }); upgradeCode = $a.upgradeCode; notes = $a.notes; defaultApps = $a.defaultApps
        registry = @($a.registry | Where-Object { $_ } | ForEach-Object { [ordered]@{ key = $_.key; name = $(if ($_.name) { $_.name } else { '' }); value = $(if ($null -ne $_.value) { $_.value } else { '' }); owned = [bool]$_.owned; associations = [bool]$_.associations } })
        shortcuts = @($a.shortcuts | Where-Object { $_ })
    }
    $apps += $entry
    Log ("  {0,-14} {1,-16} {2,8:N1} MB" -f $a.name, $version, ((Get-Item $dest).Length / 1MB)) Green
}
if ($apps.Count -eq 0) { "BUILD FAILED: no apps were found to package" | Out-File $log -Append -Encoding utf8; throw "No apps to package" }

$setupExe = Join-Path $PSScriptRoot 'out\setup.exe'
$setupVer = (Get-Item $setupExe).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*','$1'
$manifest = [ordered]@{
    product = 'Valco Office'; version = $cfg.suiteVersion; publisher = 'ItsRaiden'; updateUrl = $cfg.updateUrl
    setupVersion = $setupVer; setupFile = 'setup.exe'; setupSha256 = (Sha $setupExe); apps = $apps
    retired = @($cfg.retired | Where-Object { $_ })
}
$json = $manifest | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $payload 'valco-office.json'), $json, (New-Object Text.UTF8Encoding $false))
if (Test-Path (Join-Path $PSScriptRoot $cfg.license)) { Copy-Item (Join-Path $PSScriptRoot $cfg.license) (Join-Path $payload 'License.rtf') }
Copy-Item $setupExe (Join-Path $dist 'setup.exe')
Copy-Item 'assets\ValcoOffice.ico' (Join-Path $dist 'ValcoOffice.ico')
"[AutoRun]`r`nopen=setup.exe`r`nicon=ValcoOffice.ico`r`nlabel=Valco Office" | Out-File (Join-Path $dist 'autorun.inf') -Encoding ascii

# release\: flat files to attach to a GitHub release (Valco Office Update reads valco-office.json from there)
Get-ChildItem $payload -File | Copy-Item -Destination $release
Copy-Item $setupExe (Join-Path $release 'setup.exe')

# one zip of the whole setup folder, for sharing
$zip = Join-Path $PSScriptRoot 'dist\ValcoOffice-Setup.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($dist, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

"BUILD SUCCEEDED: $dist" | Out-File $log -Append -Encoding utf8
Log "`nDone! Setup: $dist\setup.exe" Green
Log "Update files for GitHub: $release" Green
