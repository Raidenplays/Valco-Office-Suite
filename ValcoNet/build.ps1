# Builds ValcoNet:  .\build.ps1          -> dist\ValcoNet (what Valco Office Setup packs) + portable zip
#                   .\build.ps1 -Msi     -> also dist\ValcoNet-Setup.msi (standalone installer)
#                   .\build.ps1 -EngineSetup C:\path\Firefox-Setup-ESR.exe   (use a downloaded engine)
#
# The Gecko engine is the latest Firefox ESR for Windows x64, rebuilt into ValcoNet every time
# you build, so rebuilding is also how ValcoNet picks up engine security fixes.
# Needs nothing beyond Windows itself: 7-Zip and a C compiler are used when present.
param([switch]$Msi, [string]$EngineSetup = "")
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar on PowerShell 5.1
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$log = Join-Path $PSScriptRoot 'build.log'
"=== Build started $(Get-Date)" | Out-File $log -Encoding utf8
function Log($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c; $t | Out-File $log -Append -Encoding utf8 }
function Fail($t) { "BUILD FAILED: $t" | Out-File $log -Append -Encoding utf8; throw $t }

$version = (Get-Content valconet.json -Raw | ConvertFrom-Json).version
$work = Join-Path $PSScriptRoot 'work'
$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist 'ValcoNet'
$engine = Join-Path $stage 'engine'

# Clear the old build. Files of a ValcoNet that is still running can't be deleted, but they can be moved aside.
foreach ($d in @($work, $stage)) {
    if (Test-Path $d) {
        foreach ($f in [IO.Directory]::GetFiles((Resolve-Path $d).Path, '*', 'AllDirectories')) {
            try { [IO.File]::Delete($f) } catch { try { [IO.File]::Move($f, (Join-Path $env:TEMP ("valconet-old-" + [guid]::NewGuid().ToString('N')))) } catch { } }
        }
        Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
    }
}
New-Item -ItemType Directory -Force $work, $engine | Out-Null

# ---------------- 1. Gecko engine (Firefox ESR, x64, en-US) ----------------
if (-not $EngineSetup) {
    $EngineSetup = Join-Path $work 'engine-setup.exe'
    Log "`n== Downloading the Gecko engine (latest Firefox ESR)" Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://download.mozilla.org/?product=firefox-esr-latest-ssl&os=win64&lang=en-US' -OutFile $EngineSetup -UseBasicParsing
}
Log "`n== Unpacking the engine" Cyan
$unpack = Join-Path $work 'setup'
New-Item -ItemType Directory -Force $unpack | Out-Null
$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) { foreach ($p in "$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") { if (Test-Path $p) { $sevenZip = $p } } }
if ($sevenZip) {
    & $sevenZip x -y "-o$unpack" $EngineSetup | Out-Null
} else {
    # The engine setup is a 7z archive behind a small stub: cut it out at the 7z signature.
    $bytes = [IO.File]::ReadAllBytes($EngineSetup)
    $at = -1
    $i = [Array]::IndexOf($bytes, [byte]0x37)
    while ($i -ge 0 -and $i -lt $bytes.Length - 6) {
        if ($bytes[$i + 1] -eq 0x7A -and $bytes[$i + 2] -eq 0xBC -and $bytes[$i + 3] -eq 0xAF -and $bytes[$i + 4] -eq 0x27 -and $bytes[$i + 5] -eq 0x1C) { $at = $i; break }
        $i = [Array]::IndexOf($bytes, [byte]0x37, $i + 1)
    }
    if ($at -lt 0) { Fail "The engine setup isn't a 7z archive." }
    $archive = Join-Path $work 'engine.7z'
    $fs = [IO.File]::Create($archive)
    try { $fs.Write($bytes, $at, $bytes.Length - $at) } finally { $fs.Dispose() }
    $bytes = $null
    # Windows' own tar (libarchive) reads 7z; if this Windows can't, fetch the official stand-alone 7zr.exe.
    $ErrorActionPreference = 'Continue'
    & tar.exe -xf $archive -C $unpack 2>&1 | Out-Null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$unpack\core\firefox.exe")) {
        $sevenZr = Join-Path $work '7zr.exe'
        Invoke-WebRequest 'https://www.7-zip.org/a/7zr.exe' -OutFile $sevenZr -UseBasicParsing
        & $sevenZr x -y "-o$unpack" $archive | Out-Null
    }
}
$core = Join-Path $unpack 'core'
if (-not (Test-Path "$core\firefox.exe")) { Fail "The engine setup didn't contain core\firefox.exe" }
Copy-Item "$core\*" $engine -Recurse -Force
$engineVersion = ((Get-Content "$engine\application.ini") -match '^Version=' | Select-Object -First 1) -replace '^Version=', ''
Log "  Gecko $engineVersion" Green

# The engine's updater, uninstaller and background agents aren't shipped: ValcoNet updates come from ValcoNet builds.
foreach ($f in 'updater.exe', 'updater.ini', 'update-settings.ini', 'maintenanceservice.exe', 'maintenanceservice_installer.exe',
               'default-browser-agent.exe', 'pingsender.exe', 'private_browsing.exe', 'uninstall', 'precomplete', 'removed-files',
               'firefox.VisualElementsManifest.xml', 'private_browsing.VisualElementsManifest.xml', 'desktop-launcher') {
    Remove-Item (Join-Path $engine $f) -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------- 2. ValcoNet layer ----------------
Log "`n== Adding the ValcoNet layer" Cyan
Copy-Item 'app\valconet' (Join-Path $engine 'valconet') -Recurse -Force
Copy-Item 'app\valconet.cfg' $engine -Force
New-Item -ItemType Directory -Force "$engine\defaults\pref", "$engine\distribution", "$engine\browser\chrome\icons\default" | Out-Null
Copy-Item 'app\defaults\pref\valconet-autoconfig.js' "$engine\defaults\pref\" -Force
Copy-Item 'app\distribution\policies.json' "$engine\distribution\" -Force
Copy-Item 'app\chrome-icons\*.ico' "$engine\browser\chrome\icons\default\" -Force
# Stamp the version into the About box.
$coreModule = Join-Path $engine 'valconet\modules\ValcoNet.sys.mjs'
$text = [IO.File]::ReadAllText($coreModule) -replace 'VALCONET_VERSION = "[^"]*"', "VALCONET_VERSION = `"$version`""
[IO.File]::WriteAllText($coreModule, $text, (New-Object Text.UTF8Encoding $false))

# Engine exe: ValcoNet's icon and name in Explorer and Task Manager (skipped if rcedit can't be fetched).
try {
    $rcedit = Join-Path $work 'rcedit.exe'
    Invoke-WebRequest 'https://github.com/electron/rcedit/releases/download/v2.0.0/rcedit-x64.exe' -OutFile $rcedit -UseBasicParsing
    $ErrorActionPreference = 'Continue'
    & $rcedit "$engine\firefox.exe" --set-icon 'assets\ValcoNet.ico' `
        --set-version-string 'FileDescription' 'ValcoNet' --set-version-string 'ProductName' 'ValcoNet' `
        --set-version-string 'CompanyName' 'ItsRaiden (Gecko engine by the Mozilla project)' `
        --set-version-string 'LegalCopyright' 'ValcoNet (c) 2026 ItsRaiden. Gecko (c) Mozilla contributors, MPL 2.0.'
    if ($LASTEXITCODE -ne 0) { Log "  rcedit couldn't update the engine exe; it keeps the engine's icon." Yellow }
} catch { Log "  rcedit skipped: $_" Yellow }
$ErrorActionPreference = 'Stop'

# ---------------- 3. Launcher ----------------
Log "`n== Launcher" Cyan
Push-Location launcher
$ErrorActionPreference = 'Continue'
try {
    if (Get-Command cl.exe -ErrorAction SilentlyContinue) {
        & rc.exe /nologo /fo valconet.res valconet.rc
        & cl.exe /nologo /O2 /W3 /DUNICODE /D_UNICODE valconet.c valconet.res "/Fe:$stage\valconet.exe" /link /SUBSYSTEM:WINDOWS shlwapi.lib shell32.lib user32.lib advapi32.lib | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail 'Compiling the launcher (MSVC) failed' }
        Log '  compiled with MSVC' Green
    } elseif (Get-Command gcc.exe -ErrorAction SilentlyContinue) {
        & windres.exe valconet.rc -O coff -o valconet.res
        & gcc.exe -O2 -municode -mwindows valconet.c valconet.res -o "$stage\valconet.exe" -lshlwapi -lshell32
        if ($LASTEXITCODE -ne 0) { Fail 'Compiling the launcher (MinGW) failed' }
        Log '  compiled with MinGW' Green
    } else {
        Copy-Item 'prebuilt\valconet.exe' "$stage\valconet.exe" -Force
        Log '  no C compiler found: using launcher\prebuilt\valconet.exe' Yellow
    }
} finally {
    Remove-Item valconet.res, valconet.obj -Force -ErrorAction SilentlyContinue
    Pop-Location
    $ErrorActionPreference = 'Stop'
}
Copy-Item 'LICENSE' "$stage\LICENSE.txt" -Force
Copy-Item 'NOTICE.md' "$stage\NOTICE.txt" -Force
"ValcoNet $version`r`nGecko $engineVersion" | Out-File "$stage\version.txt" -Encoding ascii

# ---------------- 4. Portable zip ----------------
$zip = Join-Path $dist "ValcoNet-$version-portable-x64.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$z = [IO.Compression.ZipFile]::Open($zip, 'Update')
try {
    # portable.ini keeps the profile in ValcoNet\Profile next to valconet.exe.
    $e = $z.CreateEntry('ValcoNet/portable.ini')
    $w = New-Object IO.StreamWriter($e.Open())
    try { $w.Write("[ValcoNet]`r`nPortable=1`r`n") } finally { $w.Dispose() }
} finally { $z.Dispose() }
Log "  portable: $zip" Green

# ---------------- 5. Standalone MSI (optional) ----------------
if ($Msi) {
    Log "`n== Building MSI" Cyan
    $ErrorActionPreference = 'Continue'
    & dotnet build installer\ValcoNet.Installer.wixproj -c Release -o $dist "-p:ValcoNetVersion=$version" 2>&1 | Tee-Object -FilePath $log -Append
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) { Fail 'Building the MSI failed' }
}

"BUILD SUCCEEDED: $stage" | Out-File $log -Append -Encoding utf8
Log "`nDone! ValcoNet $version on Gecko ${engineVersion}: $stage" Green
