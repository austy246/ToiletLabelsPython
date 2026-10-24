<#
.SYNOPSIS
  Deploys ToiletLabels to the shared VPS from this machine: build + push to GHCR, upload deploy files, run
  remote-deploy.sh over SSH, check /healthz.

.DESCRIPTION
  Without -Tag: refuses to run with tracked changes (outside .claude/), tags the image with the full SHA of HEAD,
  runs `dotnet test`, builds linux/amd64 and pushes <sha> and latest (needs `docker login ghcr.io` beforehand).
  With -Tag: builds nothing, only checks that the image exists in GHCR. Use it for a rollback (also to the
  last Python image 5a76bb39ff751ca37a34d6b0771c5bc9bda143dd, which has no --selftest).
  The deploy files (deploy/) are always uploaded from the commit of the tag; remote-deploy.sh itself is
  streamed from the CURRENT working copy.

.PARAMETER PrepareOnly   Only migrate deploy/.env on the server and run the read-only `--check-data`; deploys nothing.
.PARAMETER UpdateSecrets Pipe deploy/secrets.env (gitignored, KEY=VALUE lines) into update-env-secrets.sh on the server.
.PARAMETER DryRun        Print the commands, change nothing and call neither SSH nor the registry.
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$PrepareOnly,
    [switch]$UpdateSecrets,
    [switch]$DryRun,
    [string]$SshTarget = "deploy@57.131.165.173",
    [string]$SshKey = "$HOME/.ssh/id_rsa_austy",
    [string]$Domain = "toiletlabels.austy.cz"
)

$ErrorActionPreference = 'Stop'
$ImageRepo = 'ghcr.io/austy246/toiletlabels-app'
$RemoteDir = '/srv/toiletlabels'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }

# Runs (or, with -DryRun, only prints) one external command line.
function Invoke-Native([string]$Display, [scriptblock]$Action) {
    if ($DryRun) { Write-Host "[dry-run] $Display"; return }
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE): $Display" }
}

# Binary-safe pipes and redirects: PowerShell pipes re-encode bytes, cmd.exe does not.
function Invoke-Cmd([string]$Line) {
    Invoke-Native $Line { cmd /c $Line }
}

Push-Location $repoRoot
try {
    # --- 0. Which image -----------------------------------------------------------------------------------
    $built = $false
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        $dirty = @(git status --porcelain --untracked-files=no -- . ':(exclude).claude')
        if ($dirty.Count -gt 0) {
            $msg = "Tracked changes outside .claude/ (commit or stash first):`n" + ($dirty -join "`n")
            if ($DryRun) { Write-Warning $msg } else { throw $msg }
        }
        $Tag = (git rev-parse HEAD).Trim()
        $built = $true
    }
    elseif ($Tag -match '^[0-9a-f]{7,39}$') {
        $Tag = (git rev-parse --verify "$Tag^{commit}").Trim()   # image tags are full SHAs
    }
    if ($Tag -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$') { throw "Invalid tag '$Tag'." }
    $commit = (git rev-parse --verify --quiet "$Tag^{commit}" 2>$null)
    if (-not $commit) { throw "'$Tag' is not a commit of this repository; it is needed to upload deploy/ from that commit." }
    $commit = $commit.Trim()
    $image = "${ImageRepo}:$Tag"
    Write-Host "Image: $image  (commit $commit)"

    $key = $SshKey.Replace('\', '/')   # ssh on Windows accepts forward slashes; avoids mixed separators
    $ssh = "ssh -i `"$key`" $SshTarget"
    if (-not $DryRun -and -not (Test-Path -LiteralPath $SshKey)) { throw "SSH key not found: $SshKey" }

    $remoteScript = Join-Path $repoRoot 'deploy\remote-deploy.sh'
    if ([System.IO.File]::ReadAllBytes($remoteScript) -contains 13) {
        throw "deploy\remote-deploy.sh has CRLF line endings; bash on the server needs LF (check .gitattributes, re-checkout the file)."
    }

    # --- 1. Test, build, push -----------------------------------------------------------------------------
    if ($built) {
        Write-Step "dotnet test"
        Invoke-Native "dotnet test" { dotnet test }
        Write-Step "docker buildx build + push"
        $latest = "${ImageRepo}:latest"
        Invoke-Native "docker buildx build --platform linux/amd64 -f src/ToiletLabels/Dockerfile -t $image -t $latest --push ." {
            docker buildx build --platform linux/amd64 -f src/ToiletLabels/Dockerfile -t $image -t $latest --push .
        }
    }
    else {
        Write-Step "Checking that $image exists in the registry"
        Invoke-Native "docker manifest inspect $image" { docker manifest inspect $image | Out-Null }
    }

    # --- 2. Deploy files from the commit of the tag -----------------------------------------------------------
    Write-Step "Uploading deploy/ from $commit"
    Invoke-Cmd "git archive --format=tar $commit deploy | $ssh tar -x -C $RemoteDir"

    # --- 3. Secrets (optional) ---------------------------------------------------------------------------
    if ($UpdateSecrets) {
        $secrets = Join-Path $repoRoot 'deploy\secrets.env'
        if (-not $DryRun -and -not (Test-Path -LiteralPath $secrets)) { throw "deploy\secrets.env not found (gitignored KEY=VALUE file)." }
        Write-Step "Updating secrets on the server (names only are printed)"
        Invoke-Cmd "$ssh bash $RemoteDir/deploy/update-env-secrets.sh < deploy\secrets.env"
    }

    # --- 4. remote-deploy.sh from the working copy ---------------------------------------------------------
    $flags = @()
    git cat-file -e "${commit}:src/ToiletLabels/ToiletLabels.csproj" 2>$null
    if ($LASTEXITCODE -eq 0) { $flags += '--selftest' }   # Python-era images have no --selftest
    if ($PrepareOnly) { $flags += '--prepare-only' }
    $global:LASTEXITCODE = 0
    $remoteArgs = (@($image) + $flags) -join ' '
    Write-Step "remote-deploy.sh $remoteArgs"
    Invoke-Cmd "$ssh bash -s -- $remoteArgs < deploy\remote-deploy.sh"

    # --- 5. Health check ----------------------------------------------------------------------------------
    if ($PrepareOnly) {
        Write-Host "Prepare-only finished: nothing was deployed." -ForegroundColor Green
    }
    else {
        $url = "https://$Domain/healthz"
        Write-Step "GET $url (5 attempts, 3 s apart)"
        if ($DryRun) { Write-Host "[dry-run] Invoke-WebRequest $url" }
        else {
            $healthy = $false
            for ($i = 1; $i -le 5 -and -not $healthy; $i++) {
                try {
                    $healthy = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200
                }
                catch { Write-Host "attempt ${i}: $($_.Exception.Message)" }
                if (-not $healthy -and $i -lt 5) { Start-Sleep -Seconds 3 }
            }
            if (-not $healthy) { throw "$url did not answer 200 after 5 attempts." }
            Write-Host "Deployed $image, $url is healthy." -ForegroundColor Green
        }
    }
}
finally {
    Pop-Location
}
