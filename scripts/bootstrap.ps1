# Project-local Windows dependencies. No administrator rights or persistent PATH changes.
$Toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$ToolsRoot = Join-Path $RepoRoot '.tools'
$script:ToolchainReady = $false

function Assert-ToolPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($ToolsRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Tool operation outside the project's .tools folder: $full"
    }
}

function Test-WritableDirectory([string]$Path) {
    if (-not $Path) { return $false }
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $false }
        $probe = Join-Path $Path ('dsh-write-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $probe -ErrorAction Stop | Out-Null
        Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
        return $true
    } catch { return $false }
}

function Test-TempUsableByDotnet([string]$DotnetExe, [string]$TempPath) {
    # Writing a folder from PowerShell is not proof: child processes can run with a more restricted
    # token, so `dotnet` may still fail to create its temporary subdirectory. Probe with dotnet itself.
    if (-not $DotnetExe -or -not (Test-Path -LiteralPath $DotnetExe -PathType Leaf)) { return $true }
    if (-not (Test-WritableDirectory $TempPath)) { return $false }
    $savedTemp = $env:TEMP
    $savedTmp = $env:TMP
    try {
        $env:TEMP = $TempPath
        $env:TMP = $TempPath
        # --info is the command that actually creates a temporary subdirectory; --list-sdks does not,
        # so it would report a broken temp folder as fine.
        $null = & $DotnetExe --info 2>&1
        return $LASTEXITCODE -eq 0
    } catch { return $false }
    finally {
        $env:TEMP = $savedTemp
        $env:TMP = $savedTmp
    }
}

function Initialize-TempDirectory([string]$DotnetExe) {
    # The .NET CLI creates temporary subdirectories while probing the SDK. When %TEMP% is locked down
    # (sandbox policies, "Controlled folder access", a profile whose ACLs were reset) `dotnet --info`
    # fails with UnauthorizedAccessException and exits with 1, which looks exactly like a broken SDK.
    # Fall back to a private temp folder inside .tools so the toolchain still works.
    $current = $env:TEMP
    if ((Test-TempUsableByDotnet $DotnetExe $current) -and (Test-TempUsableByDotnet $DotnetExe $env:TMP)) { return }
    $fallback = Join-Path $ToolsRoot 'temp'
    New-Item -ItemType Directory -Path $fallback -Force | Out-Null
    if (-not (Test-WritableDirectory $fallback)) { return }   # nothing better to offer; report later
    $env:TEMP = $fallback
    $env:TMP = $fallback
    Write-Host "Note: %TEMP% cannot be used by the .NET SDK; using $fallback instead."
}

function Clear-StaleToolFolders {
    # A launcher that was killed mid-unpack (closing the window, antivirus, power loss) leaves
    # staging-*/previous-* folders behind. They are pure garbage and can hold gigabytes.
    foreach ($dir in Get-ChildItem -LiteralPath $ToolsRoot -Directory -ErrorAction SilentlyContinue) {
        if ($dir.Name -notmatch '^(staging|previous)-[0-9a-f]{32}$') { continue }
        try {
            Assert-ToolPath $dir.FullName
            Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
            Write-Host "Removed leftover $($dir.Name)"
        } catch {
            Write-Host "Could not remove leftover $($dir.Name); close any running build and start again."
        }
    }
}

function Install-ToolArchive($Spec, [string]$RequiredFile, [switch]$Nested) {
    $destination = Join-Path $ToolsRoot $Spec.name
    $downloads = Join-Path $ToolsRoot 'downloads'
    New-Item -ItemType Directory -Path $downloads -Force | Out-Null
    $archive = Join-Path $downloads ($Spec.name + '.zip')
    if ((Test-Path -LiteralPath $archive) -and (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $Spec.sha512) {
        Remove-Item -LiteralPath $archive -Force
    }
    if (-not (Test-Path -LiteralPath $archive)) {
        $partial = $archive + '.partial'
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Write-Host "Downloading $($Spec.name) (attempt $attempt/3) ..."
                Invoke-WebRequest -Uri $Spec.url -OutFile $partial -UseBasicParsing -TimeoutSec 1200
                if ((Get-FileHash -LiteralPath $partial -Algorithm SHA512).Hash -ne $Spec.sha512) {
                    throw 'The download checksum does not match the official release.'
                }
                Move-Item -LiteralPath $partial -Destination $archive -Force
                break
            } catch {
                if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
                if ($attempt -eq 3) { throw "Could not download $($Spec.name). Check the internet connection and run Start.cmd again. $($_.Exception.Message)" }
            }
        }
    }
    Write-Host "Unpacking $($Spec.name) ..."
    $stage = Join-Path $ToolsRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
    $previous = Join-Path $ToolsRoot ('previous-' + [Guid]::NewGuid().ToString('N'))
    Assert-ToolPath $stage
    Assert-ToolPath $destination
    Assert-ToolPath $previous
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        # ExtractToDirectory silently stops early when a file is locked (antivirus, indexer, a running
        # dotnet.exe). Compare the file count against the archive and unpack again if anything is missing.
        $expected = 0
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try { $expected = @($zip.Entries | Where-Object { $_.Name }).Count } finally { $zip.Dispose() }
        $unpacked = $null
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
            [IO.Compression.ZipFile]::ExtractToDirectory($archive, $stage)
            $candidate = if ($Nested) { Join-Path $stage $Spec.name } else { $stage }
            $actual = @(Get-ChildItem -LiteralPath $candidate -Recurse -File -ErrorAction SilentlyContinue).Count
            if ((Test-Path -LiteralPath (Join-Path $candidate $RequiredFile)) -and $actual -ge $expected) { $unpacked = $candidate; break }
            Write-Host "  unpack incomplete ($actual of $expected files), retrying ($attempt/3) ..."
            Start-Sleep -Seconds 2
        }
        if (-not $unpacked) {
            throw "The tool archive could not be unpacked completely. Close any running build, or delete .tools\downloads\$($Spec.name).zip and start again."
        }
        if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $previous }
        try { Move-Item -LiteralPath $unpacked -Destination $destination }
        catch {
            if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $destination }
            throw
        }
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}

function Test-Godot([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $folder = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath (Join-Path $folder 'GodotSharp/Tools/nupkgs'))) { return $false }
    $console = $Path -replace '(_console)?\.exe$', '_console.exe'
    if (-not (Test-Path -LiteralPath $console)) { $console = $Path }
    try {
        $version = (& $console --version 2>$null | Out-String).Trim()
        return $LASTEXITCODE -eq 0 -and $version -match ('^' + [regex]::Escape($Toolchain.godot.version) + '\.stable\.mono\.')
    } catch { return $false }
}

function Test-DotnetSdk([string]$Path, [switch]$Explain) {
    # Records why a candidate was rejected so a failed launch can report something actionable
    # instead of a bare "could not run".
    function Reject([string]$Why) {
        if ($Explain) { Write-Host "  rejected: $Path ($Why)" }
        return $false
    }
    if (-not $Path) { return Reject 'no path' }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return Reject 'dotnet.exe not found' }
    try {
        # Note: with $ErrorActionPreference = 'Stop', redirecting a native command's stderr (2>$null)
        # turns the first stderr line into a terminating error under Windows PowerShell 5.1. The
        # redirection is therefore dropped and the output simply kept out of the transcript.
        $sdks = @(& $Path --list-sdks)
        $code = $LASTEXITCODE
        if ($code -ne 0) { return Reject "dotnet --list-sdks exited with $code" }
        $majors = @($sdks | Where-Object { $_ -match '^(\d+)\.\d+\.\d+ \[' } | ForEach-Object { [int]$Matches[1] } | Where-Object { $_ -ge 8 })
        if (-not $majors.Count) { return Reject 'no .NET SDK 8 or newer is installed' }
        if ($env:OS -eq 'Windows_NT') {
            $info = (& $Path --info | Out-String)
            if ($LASTEXITCODE -ne 0) { return Reject "dotnet --info exited with $LASTEXITCODE" }
            if ($info -notmatch '(?m)^\s*Architecture:\s*x64\s*$') { return Reject 'not an x64 host' }
        }
        # The console harness targets net8.0; a newer SDK alone does not supply that runtime.
        $runtimes = @(& $Path --list-runtimes)
        if ($LASTEXITCODE -ne 0) { return Reject "dotnet --list-runtimes exited with $LASTEXITCODE" }
        if (-not @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.0\.' }).Count) { return Reject 'the Microsoft.NETCore.App 8.0 runtime is missing' }
        return $true
    } catch {
        return Reject $_.Exception.Message
    }
}

function Initialize-Toolchain {
    if ($script:ToolchainReady) { return }
    Clear-StaleToolFolders
    # Repair %TEMP% before anything probes the SDK: a temp folder the CLI cannot write makes a healthy
    # SDK look broken (dotnet --info exits with 1 on UnauthorizedAccessException).
    $probeDotnet = Join-Path (Join-Path $ToolsRoot $Toolchain.dotnet.name) 'dotnet.exe'
    Initialize-TempDirectory $probeDotnet
    # Captures and reports are output files, not runtime assets; do not import thousands of frames.
    $reportsDirectory = Join-Path $RepoRoot 'reports'
    New-Item -ItemType Directory -Path $reportsDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $reportsDirectory '.gdignore'))) {
        New-Item -ItemType File -Path (Join-Path $reportsDirectory '.gdignore') | Out-Null
    }
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if ($env:OS -ne 'Windows_NT') {
        $script:GodotExe = $env:GODOT
        if (-not $script:GodotExe) {
            foreach ($name in @('godot', 'godot4', 'Godot_v4.5.2-stable_mono_linux.x86_64')) {
                $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue
                if ($command) { $script:GodotExe = $command.Source; break }
            }
        }
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
        if (-not $script:GodotExe -or -not $command) { throw 'Install Godot .NET 4.5.2 and the .NET 8 SDK; automatic downloads are supported on Windows x64.' }
        $script:DotnetExe = $command.Source
        $script:ToolchainReady = $true
        return
    }
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $localEngine = Join-Path (Join-Path $ToolsRoot $Toolchain.godot.name) ($Toolchain.godot.name + '.exe')
    $savedFile = Join-Path $RepoRoot '.godot-path'
    $saved = if (Test-Path -LiteralPath $savedFile) { (Get-Content -LiteralPath $savedFile -Raw -Encoding UTF8).Trim().Trim('"') } else { '' }
    $candidates = @($env:GODOT, $localEngine, $saved)
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Godot $candidate.Trim('"'))) {
            $script:GodotExe = [IO.Path]::GetFullPath($candidate.Trim('"'))
            break
        }
    }
    if (-not $script:GodotExe) {
        Install-ToolArchive $Toolchain.godot ($Toolchain.godot.name + '.exe') -Nested
        if (-not (Test-Godot $localEngine)) { throw 'The downloaded Godot .NET executable could not run.' }
        $script:GodotExe = $localEngine
    }
    $script:GodotExe = $script:GodotExe -replace '_console\.exe$', '.exe'
    $localDotnet = Join-Path (Join-Path $ToolsRoot $Toolchain.dotnet.name) 'dotnet.exe'
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    $candidates = @($localDotnet)
    if ($command) { $candidates += $command.Source }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
    foreach ($candidate in $candidates) {
        if (Test-DotnetSdk $candidate) { $script:DotnetExe = $candidate; break }
    }
    if (-not $script:ToolchainExplain) { $script:ToolchainExplain = @() }
    if (-not $script:DotnetExe) {
        Write-Host 'No compatible SDK/runtime pair found; preparing a private .NET 8 SDK ...'
        Install-ToolArchive $Toolchain.dotnet 'dotnet.exe'
        # An unpacked SDK is occasionally still being scanned by antivirus when the first probe runs,
        # which makes it look unusable. Retry briefly before giving up.
        $ready = $false
        for ($attempt = 1; $attempt -le 5 -and -not $ready; $attempt++) {
            $ready = Test-DotnetSdk $localDotnet
            if (-not $ready -and $attempt -lt 5) { Start-Sleep -Seconds 2 }
        }
        if (-not $ready) {
            Test-DotnetSdk $localDotnet -Explain | Out-Null
            throw ("The downloaded .NET SDK could not run. " +
                "Try running Start.cmd again; if it keeps failing, delete the folder .tools\$($Toolchain.dotnet.name) " +
                "and .tools\downloads\$($Toolchain.dotnet.name).zip, then start again.")
        }
        $script:DotnetExe = $localDotnet
    }
    $dotnetFolder = Split-Path -Parent $script:DotnetExe
    $env:DOTNET_ROOT = $dotnetFolder
    $env:DOTNET_ROOT_X64 = $dotnetFolder
    $env:PATH = "$dotnetFolder;$env:PATH"
    # Keep the bundled Godot packages available to a fresh SDK without changing global NuGet settings.
    $feed = Join-Path $ToolsRoot 'godot-nuget'
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $script:GodotExe) 'GodotSharp/Tools/nupkgs') -Filter '*.nupkg' |
        Copy-Item -Destination $feed -Force
    $config = Join-Path $RepoRoot 'NuGet.Config'
    if (-not (Test-Path -LiteralPath $config)) {
        @'
<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by the workshop launcher. Local packages travel with this project. -->
<configuration>
  <packageSources>
    <clear />
    <add key="Godot bundled" value=".tools/godot-nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath $config -Encoding UTF8
    }
    # Other tools also read this file; UTF-8 without a BOM keeps it portable to Python and shells.
    [IO.File]::WriteAllText($savedFile, $script:GodotExe, [Text.UTF8Encoding]::new($false))
    Write-Host "Godot: $script:GodotExe"
    Write-Host ".NET:  $script:DotnetExe"
    $script:ToolchainReady = $true
}
