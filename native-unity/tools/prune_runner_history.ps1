# Keep the latest three GitHub Actions run archives on a self-hosted Windows Runner.
# Only numeric run-ID directories (and paired visual-evidence ZIPs) are eligible.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Root,
    [Parameter(Mandatory = $true)]
    [ValidateSet('Builds', 'VisualEvidence', 'EditorEvidence')]
    [string] $Kind,
    [ValidateRange(1, 100)]
    [int] $Keep = 3,
    [string] $ProtectRunId = $env:GITHUB_RUN_ID
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    Write-Host "RETENTION_SKIP: missing root=$Root"
    return
}

# Build lists from exact names only. Do not touch latest/, workspace, cache,
# champion sources, or other unrecognised files in the runner directories.
$byId = @{}
foreach ($dir in @(Get-ChildItem -LiteralPath $Root -Directory)) {
    if ($dir.Name -match '^\d{8,}$') { $byId[$dir.Name] = $true }
}
if ($Kind -eq 'VisualEvidence') {
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File)) {
        if ($file.Name -match '^hextech-gpu-(\d{8,})\.zip$') {
            $byId[$Matches[1]] = $true
        }
    }
}

$sorted = @($byId.Keys | Sort-Object { [long]$_ } -Descending)
$keepers = @{}
foreach ($id in @($sorted | Select-Object -First $Keep)) { $keepers[$id] = $true }
if ($ProtectRunId -match '^\d{8,}$') { $keepers[$ProtectRunId] = $true }

# Protect currently running, versioned Player directories. An active
# build is never forcibly killed just to enforce the storage quota.
$livePlayerPaths = @()
if ($Kind -eq 'Builds') {
    try {
        $livePlayerPaths = @(Get-CimInstance Win32_Process -Filter "Name = 'HextechSeekers.exe'" -ErrorAction Stop |
            ForEach-Object { $_.ExecutablePath } | Where-Object { $_ })
    } catch {
        Write-Warning "RETENTION_PROCESS_PROBE_FAILED: $($_.Exception.Message)"
    }
}

foreach ($id in $sorted) {
    if ($keepers.ContainsKey($id)) { continue }
    $versionDir = Join-Path $Root $id
    $versionZip = if ($Kind -eq 'VisualEvidence') {
        Join-Path $Root ("hextech-gpu-$id.zip")
    } else { $null }

    if ($Kind -eq 'Builds' -and (Test-Path -LiteralPath $versionDir -PathType Container)) {
        $prefix = $versionDir.TrimEnd('\') + '\'
        if (@($livePlayerPaths | Where-Object {
            $_.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0) {
            Write-Warning "RETENTION_PROTECT_RUNNING_PLAYER: $versionDir"
            continue
        }
        $exe = Join-Path $versionDir 'Windows\HextechSeekers.exe'
        if (Test-Path -LiteralPath $exe -PathType Leaf) {
            try {
                $stream = [System.IO.File]::Open($exe,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::None)
                $stream.Dispose()
            } catch {
                Write-Warning "RETENTION_PROTECT_LOCKED_BUILD: $versionDir ($($_.Exception.Message))"
                continue
            }
        }
    }

    try {
        if (Test-Path -LiteralPath $versionDir -PathType Container) {
            Remove-Item -LiteralPath $versionDir -Recurse -Force -ErrorAction Stop
            Write-Host "RETENTION_REMOVED_DIR: $versionDir"
        }
        if ($versionZip -and (Test-Path -LiteralPath $versionZip -PathType Leaf)) {
            Remove-Item -LiteralPath $versionZip -Force -ErrorAction Stop
            Write-Host "RETENTION_REMOVED_ZIP: $versionZip"
        }
    } catch {
        # Keep any remaining evidence on failure; later successful runs can retry.
        Write-Warning "RETENTION_DELETE_FAILED: id=$id kind=$Kind $($_.Exception.Message)"
    }
}

Write-Host "RETENTION_COMPLETE: kind=$Kind keep=$Keep protected=$ProtectRunId versionsBefore=$($sorted.Count)"
