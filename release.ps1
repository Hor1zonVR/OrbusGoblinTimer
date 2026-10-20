# OrbusGoblinTimer - one-command LOCAL build, push and GitHub Release for RebornManager.
# Default: publish a STABLE release offered to RebornManager users.
# Use -Preview to test a GitHub prerelease (RM does not offer previews).
# GameDir supplies build REFERENCES ONLY. This script NEVER installs into the game.
#
# Usage:
#   .\release.ps1
#   .\release.ps1 -Preview
#   .\release.ps1 -GameDir 'C:\Users\you\AppData\Local\OrbusRebornModdingInstances\Modded Orbus'

param(
    [string]$GameDir = '',
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'Hor1zonVR/OrbusGoblinTimer'
$root = $PSScriptRoot
$project = Join-Path $root 'src\OrbusGoblinTimer\OrbusGoblinTimer.csproj'
$pluginFile = Join-Path $root 'src\OrbusGoblinTimer\Plugin.cs'
$dll = Join-Path $root 'src\OrbusGoblinTimer\bin\Release\net6.0\OrbusGoblinTimer.dll'

function Assert-Exit([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit code $LASTEXITCODE); release cancelled." }
}

try {
    Set-Location $root
    Write-Host ''
    Write-Host '===========================================' -ForegroundColor Cyan
    Write-Host '  OrbusGoblinTimer -> GitHub -> RebornManager' -ForegroundColor Cyan
    Write-Host '===========================================' -ForegroundColor Cyan
    Write-Host ''

    foreach ($command in @('git','gh','dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Missing '$command'. Install Git, GitHub CLI and the .NET 8 SDK."
        }
    }
    & gh auth status
    Assert-Exit 'GitHub authentication (run gh auth login)'

    if (!(Test-Path $project) -or !(Test-Path $pluginFile)) {
        throw 'Cannot find OrbusGoblinTimer source. Run release.ps1 from its cloned repository.'
    }
    $branch = (& git branch --show-current).Trim()
    Assert-Exit 'Checking Git branch'
    if ($branch -ne 'main') { throw 'Switch to the main branch before publishing.' }
    $origin = (& git remote get-url origin).Trim()
    Assert-Exit 'Checking Git origin'
    if ($origin -notmatch '[/\\:]Hor1zonVR/OrbusGoblinTimer(\.git)?$') {
        throw 'Git remote does not point to Hor1zonVR/OrbusGoblinTimer.'
    }

    & git fetch origin main --tags
    Assert-Exit 'Fetching GitHub changes/tags'
    $localHead = (& git rev-parse HEAD).Trim()
    Assert-Exit 'Checking local commit'
    $remoteHead = (& git rev-parse origin/main).Trim()
    Assert-Exit 'Checking remote commit'
    if ($localHead -ne $remoteHead) {
        throw 'Local main differs from GitHub main. Sync it first (git pull --rebase --autostash origin main), inspect conflicts, then retry.'
    }

    $changesBefore = @(& git status --porcelain)
    Assert-Exit 'Checking local changes'

    # The references choice is local PC state, never a committed repo setting.
    # Reuse it on subsequent builds; if no cache exists, choose a reasonable
    # installed instance automatically instead of prompting for a number.
    $referenceCache = Join-Path $root '.build-refs-path.txt'
    $referenceFiles = @(
        'BepInEx\core\BepInEx.Core.dll',
        'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
        'BepInEx\core\Il2CppInterop.Runtime.dll',
        'BepInEx\core\0Harmony.dll',
        'BepInEx\interop\Il2Cppmscorlib.dll',
        'BepInEx\interop\UnityEngine.CoreModule.dll',
        'BepInEx\interop\UnityEngine.TextRenderingModule.dll'
    )

    function Normalize-GameDir([string]$rawPath) {
        return [IO.Path]::GetFullPath(
            [Environment]::ExpandEnvironmentVariables($rawPath.Trim().Trim('"'))
        )
    }

    function Has-BuildReferences([string]$path) {
        if ([string]::IsNullOrWhiteSpace($path)) { return $false }
        try {
            $resolved = Normalize-GameDir $path
            foreach ($file in $referenceFiles) {
                if (!(Test-Path -LiteralPath (Join-Path $resolved $file) -PathType Leaf)) {
                    return $false
                }
            }
            return $true
        } catch { return $false }
    }

    if (![string]::IsNullOrWhiteSpace($GameDir)) {
        $GameDir = Normalize-GameDir $GameDir
        if (!(Has-BuildReferences $GameDir)) {
            throw "Explicit -GameDir '$GameDir' is missing required BepInEx/interop DLLs."
        }
        Write-Host "Using explicitly selected build references." -ForegroundColor Cyan
    } else {
        if (Test-Path -LiteralPath $referenceCache -PathType Leaf) {
            $saved = (Get-Content -LiteralPath $referenceCache -Raw).Trim()
            if (Has-BuildReferences $saved) {
                $GameDir = Normalize-GameDir $saved
                Write-Host "Reusing saved BepInEx build references; no selection needed." -ForegroundColor Green
            } else {
                Write-Host "Previously saved build-reference folder is no longer valid; searching installed instances." -ForegroundColor Yellow
            }
        }

        if ([string]::IsNullOrWhiteSpace($GameDir)) {
            $instanceRoot = Join-Path $env:LOCALAPPDATA 'OrbusRebornModdingInstances'
            $candidates = @()
            if (Test-Path -LiteralPath $instanceRoot -PathType Container) {
                $candidates = @(Get-ChildItem -LiteralPath $instanceRoot -Directory |
                    Where-Object { Has-BuildReferences $_.FullName })
            }
            if ($candidates.Count -eq 0) {
                throw "No compatible local BepInEx build references found in $instanceRoot. Use .\release.ps1 -GameDir 'C:\path\to\your\modded\instance'."
            }

            # Prefer whichever instance was used most recently; tie-break to
            # Community's original readable interop, then stable name order.
            $ordered = @($candidates | ForEach-Object {
                $logPath = Join-Path $_.FullName 'BepInEx\LogOutput.log'
                $lastUsed = if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                    (Get-Item -LiteralPath $logPath).LastWriteTimeUtc
                } else {
                    [DateTime]::MinValue
                }
                [PSCustomObject]@{
                    Path = $_.FullName
                    LastUsed = $lastUsed
                    Community = (Test-Path -LiteralPath (Join-Path $_.FullName 'vrclient.exe') -PathType Leaf)
                }
            } | Sort-Object @{ Expression = 'LastUsed'; Descending = $true },
                            @{ Expression = 'Community'; Descending = $true },
                            @{ Expression = 'Path'; Descending = $false })
            $GameDir = $ordered[0].Path
            Write-Host "Auto-selected a compatible BepInEx instance: $GameDir" -ForegroundColor Cyan
            if ($candidates.Count -gt 1) {
                Write-Host "To use another instance permanently, run .\release.ps1 -GameDir 'C:\path\to\instance' once." -ForegroundColor DarkGray
            }
        }
    }

    # Save only a validated path; this local ignored file survives git pulls.
    # Calling -GameDir again updates the saved choice for every future build.
    $GameDir = Normalize-GameDir $GameDir
    if (!(Has-BuildReferences $GameDir)) {
        throw "Build reference validation failed for '$GameDir'."
    }
    [IO.File]::WriteAllText($referenceCache, $GameDir + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))

    # If this version has already been released, automatically bump the PATCH version,
    # avoiding the repetitive manual edit needed on each RM test release.
    $source = Get-Content -LiteralPath $pluginFile -Raw
    $pattern = '(?s)\[BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"(?<version>\d+\.\d+\.\d+)"'
    $match = [regex]::Match($source, $pattern)
    if (-not $match.Success) { throw 'Could not find the BepInPlugin version in Plugin.cs.' }
    $version = $match.Groups['version'].Value
    $tag = "v$version"

    $remoteTagLines = @(& git ls-remote --tags --refs origin 'refs/tags/v*')
    Assert-Exit 'Checking published Git tags'
    $existingTags = @{}
    foreach ($line in $remoteTagLines) {
        if ($line -match 'refs/tags/(v\d+\.\d+\.\d+)$') {
            $existingTags[$Matches[1]] = $true
        }
    }
    if ($existingTags.ContainsKey($tag)) {
        if ($changesBefore.Count -eq 0) {
            throw "$tag is already published and no local code has changed. The published release already exists."
        }
        $parts = $version.Split('.')
        $major = [int]$parts[0]
        $minor = [int]$parts[1]
        $patch = [int]$parts[2]
        do {
            $patch++
            $version = "$major.$minor.$patch"
            $tag = "v$version"
        } while ($existingTags.ContainsKey($tag))
        $group = $match.Groups['version']
        $source = $source.Substring(0, $group.Index) + $version +
                  $source.Substring($group.Index + $group.Length)
        [IO.File]::WriteAllText($pluginFile, $source, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Existing release detected. Automatically bumped plugin to $version." -ForegroundColor Yellow
    }



    Write-Host "Build references: $GameDir" -ForegroundColor Cyan
    Write-Host 'This script WILL NOT install or change files in that game folder.' -ForegroundColor DarkGray
    Write-Host "Publishing version: $tag" -ForegroundColor Cyan
    if ($Preview) {
        Write-Host 'Mode: PREVIEW (NOT shown in RebornManager)' -ForegroundColor Yellow
    } else {
        Write-Host 'Mode: STABLE (shown in RebornManager)' -ForegroundColor Yellow
    }

    Write-Host '[0/4] Checking countdown calculations...' -ForegroundColor Yellow
    & dotnet run --project (Join-Path $root 'tests\ClockSmokeTests\ClockSmokeTests.csproj') -c Release
    Assert-Exit 'Countdown tests'

    Write-Host '[1/4] Building DLL locally...' -ForegroundColor Yellow
    & dotnet build $project -c Release --no-incremental "-p:GameDir=$GameDir"
    Assert-Exit 'Local build'
    if (!(Test-Path $dll -PathType Leaf)) { throw "DLL not found after successful build: $dll" }

    Write-Host '[2/4] Committing changed OrbusGoblinTimer source...' -ForegroundColor Yellow
    & git add -A
    Assert-Exit 'Staging source'
    $staged = @(& git diff --cached --name-only)
    Assert-Exit 'Inspecting staged files'
    $unsafe = @($staged | Where-Object {
        $_ -match '(?i)(^|/)(BepInEx|interop|\.build-refs)/|\.(dll|zip|pfx|pem|key|env)$'
    })
    if ($unsafe.Count -gt 0) {
        & git reset
        throw ('Refusing to commit binaries, interop or secrets: ' + ($unsafe -join ', '))
    }
    if ($staged.Count -gt 0) {
        & git commit -m "OrbusGoblinTimer $tag update"
        Assert-Exit 'Committing changed source'
    } else {
        Write-Host 'Source is already committed; publishing existing commit.' -ForegroundColor DarkGray
    }

    Write-Host '[3/4] Pushing main to GitHub...' -ForegroundColor Yellow
    & git push origin main
    Assert-Exit 'Pushing GitHub main'
    $commit = (& git rev-parse HEAD).Trim()
    Assert-Exit 'Getting release commit'

    Write-Host "[4/4] Publishing $tag with OrbusGoblinTimer.dll..." -ForegroundColor Yellow
    $notes = "OrbusGoblinTimer ${tag}: experimental local Trickster Goblin headset countdown. Starts after observing a confirmed spawn announcement or manual Ctrl+Shift+F6 sync. Community/Spades event interception and current server interval need in-game verification. No position tracking. See README."
    $args = @('release','create',$tag,$dll,'--repo',$repo,'--target',$commit,
              '--title',"OrbusGoblinTimer $tag",'--notes',$notes)
    if ($Preview) { $args += '--prerelease' }
    & gh @args
    Assert-Exit 'Creating GitHub Release'

    $releaseData = & gh release view $tag --repo $repo --json tagName,isPrerelease,assets,url
    Assert-Exit 'Verifying release'
    $release = $releaseData | ConvertFrom-Json
    if ($release.tagName -ne $tag -or
        @($release.assets | Where-Object { $_.name -eq 'OrbusGoblinTimer.dll' }).Count -ne 1) {
        throw 'Release did not contain exactly one OrbusGoblinTimer.dll.'
    }
    Write-Host ''
    Write-Host "SUCCESS: $($release.url)" -ForegroundColor Green
    if ($Preview) {
        Write-Host 'Preview: not visible to RM. Use a new stable version when ready.' -ForegroundColor Yellow
    } else {
        Write-Host 'Stable: close Orbus and refresh RebornManager to install/update OrbusGoblinTimer.' -ForegroundColor Green
    }
} catch {
    Write-Host ''
    Write-Host "RELEASE STOPPED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
