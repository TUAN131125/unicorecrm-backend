<#
.SYNOPSIS
    Runs the required repository-level Platform quality gate and emits commit-bound evidence.

.DESCRIPTION
    The default mode requires a clean Git working tree so a PASS result is attributable to exactly
    one commit. -AllowDirtyWorkingTree exists only for local candidate verification; evidence from
    that mode is labelled WORKING_TREE and is not commit-bound CI evidence.
#>
[CmdletBinding()]
param(
    [string] $EvidenceDirectory = 'artifacts/platform-ci',
    [string] $RunId = '',
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [switch] $AllowDirtyWorkingTree
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidenceRoot = if ([IO.Path]::IsPathRooted($EvidenceDirectory)) {
    $EvidenceDirectory
}
else {
    Join-Path $repositoryRoot $EvidenceDirectory
}
$logsRoot = Join-Path $evidenceRoot 'logs'

$script:Results = New-Object 'System.Collections.Generic.List[object]'
$script:GateStartedAt = [DateTimeOffset]::UtcNow
$headCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headCommit)) {
    throw 'Could not resolve the repository HEAD commit.'
}
$branchOutput = @(& git -C $repositoryRoot symbolic-ref --short -q HEAD)
$branch = ($branchOutput -join '').Trim()
if ([string]::IsNullOrWhiteSpace($branch)) { $branch = 'DETACHED' }
$initialWorkingTree = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
$subjectKind = if ($initialWorkingTree.Count -eq 0) { 'COMMIT' } else { 'WORKING_TREE' }
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
$safeRunId = if ([string]::IsNullOrWhiteSpace($RunId)) {
    [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss') + '_' + $PID
}
else {
    $RunId -replace '[^A-Za-z0-9_]', '_'
}
if ($safeRunId.Length -gt 36) { $safeRunId = $safeRunId.Substring(0, 36) }
$databasePrefix = "PlatformCI_$($headCommit.Substring(0, 12))_$safeRunId"

function Get-RelativePath([string] $path) {
    $rootUri = [Uri]::new(($repositoryRoot.TrimEnd('\') + '\'))
    $pathUri = [Uri]::new($path)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Format-Command([string] $filePath, [string[]] $arguments) {
    $tokens = @($filePath) + @($arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    })
    return $tokens -join ' '
}

function Format-ArgumentLine([string[]] $arguments) {
    return (@($arguments | ForEach-Object {
        if ([string]::IsNullOrEmpty($_)) { '""' }
        elseif ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' }
        else { $_ }
    }) -join ' ')
}

function Add-Result(
    [string] $name,
    [string] $category,
    [string] $status,
    [Nullable[int]] $exitCode,
    [double] $durationSeconds,
    [string] $command,
    [string] $logPath,
    [string] $reason,
    [string] $scriptPath,
    [string] $databaseName
) {
    $scriptHash = $null
    if (-not [string]::IsNullOrWhiteSpace($scriptPath) -and (Test-Path -LiteralPath $scriptPath)) {
        $scriptHash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $script:Results.Add([pscustomobject]@{
        name = $name
        category = $category
        required = $true
        status = $status
        exitCode = $exitCode
        durationSeconds = [Math]::Round($durationSeconds, 3)
        command = $command
        log = if ([string]::IsNullOrWhiteSpace($logPath)) { $null } else { Get-RelativePath $logPath }
        reason = if ([string]::IsNullOrWhiteSpace($reason)) { $null } else { $reason }
        script = if ([string]::IsNullOrWhiteSpace($scriptPath)) { $null } else { Get-RelativePath $scriptPath }
        scriptSha256 = $scriptHash
        databaseName = if ([string]::IsNullOrWhiteSpace($databaseName)) { $null } else { $databaseName }
    })
}

function Invoke-RequiredCommand(
    [string] $name,
    [string] $category,
    [string] $filePath,
    [string[]] $arguments,
    [string] $logFileName,
    [string] $scriptPath = '',
    [string] $databaseName = ''
) {
    $logPath = Join-Path $logsRoot $logFileName
    $standardOutputPath = "$logPath.stdout.tmp"
    $standardErrorPath = "$logPath.stderr.tmp"
    $command = Format-Command $filePath $arguments
    Write-Host ''
    Write-Host "===== REQUIRED: $name ====="
    Write-Host $command
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = 1
    $reason = ''
    Push-Location $repositoryRoot
    try {
        # Waiting on the direct Process object avoids a PowerShell pipeline being held open by an
        # orphaned verifier descendant. Each verifier remains responsible for its own host cleanup.
        $process = Start-Process -FilePath $filePath -ArgumentList (Format-ArgumentLine $arguments) `
            -WorkingDirectory $repositoryRoot -WindowStyle Hidden -Wait `
            -RedirectStandardOutput $standardOutputPath -RedirectStandardError $standardErrorPath -PassThru
        $process.Refresh()
        if (-not $process.HasExited -or $null -eq $process.ExitCode) {
            throw "Could not observe the exit code for required command '$name'."
        }
        $exitCode = [int] $process.ExitCode
    }
    catch {
        $reason = $_.Exception.Message
        $exitCode = 1
    }
    finally {
        Pop-Location
        $stopwatch.Stop()
    }
    $outputText = if (Test-Path -LiteralPath $standardOutputPath) { Get-Content -LiteralPath $standardOutputPath -Raw } else { '' }
    $errorText = if (Test-Path -LiteralPath $standardErrorPath) { Get-Content -LiteralPath $standardErrorPath -Raw } else { '' }
    @($outputText, $errorText, $reason) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Set-Content -LiteralPath $logPath -Encoding UTF8
    if (-not [string]::IsNullOrWhiteSpace($outputText)) { Write-Host $outputText.TrimEnd() }
    if (-not [string]::IsNullOrWhiteSpace($errorText)) { Write-Host $errorText.TrimEnd() }
    if (-not [string]::IsNullOrWhiteSpace($reason)) { Write-Host $reason }
    Remove-Item -LiteralPath $standardOutputPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $standardErrorPath -Force -ErrorAction SilentlyContinue
    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    Add-Result $name $category $status $exitCode $stopwatch.Elapsed.TotalSeconds $command $logPath $reason $scriptPath $databaseName
    return ($exitCode -eq 0)
}

function Add-NotRun([string] $name, [string] $category, [string] $reason, [string] $scriptPath, [string] $databaseName) {
    Add-Result $name $category 'NOT_RUN' $null 0 '' '' $reason $scriptPath $databaseName
}

$subjectReasons = New-Object 'System.Collections.Generic.List[string]'
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_SHA) -and $env:GITHUB_SHA -ne $headCommit) {
    $subjectReasons.Add("GITHUB_SHA '$env:GITHUB_SHA' does not match HEAD '$headCommit'.")
}
if ($initialWorkingTree.Count -gt 0 -and -not $AllowDirtyWorkingTree) {
    $subjectReasons.Add('The working tree is not clean; a PASS result could not be attributed to the HEAD commit.')
}
$subjectStatus = if ($subjectReasons.Count -eq 0) { 'PASS' } else { 'FAIL' }
Add-Result 'repository-subject-integrity' 'subject' $subjectStatus $(if ($subjectStatus -eq 'PASS') { 0 } else { 1 }) 0 'git rev-parse HEAD; git status --porcelain=v1' '' ($subjectReasons -join ' ') '' ''
Write-Host "Verification subject: $subjectKind $headCommit ($branch)"
if ($initialWorkingTree.Count -gt 0) {
    Write-Host 'Initial working-tree changes:'
    $initialWorkingTree | ForEach-Object { Write-Host "  $_" }
}

$subjectPassed = $subjectStatus -eq 'PASS'
$localDbStarted = $false
if ($subjectPassed) {
    $localDbStarted = Invoke-RequiredCommand 'sql-localdb-start' 'database' 'sqllocaldb.exe' @('start', 'MSSQLLocalDB') '00-sql-localdb-start.log'
}
else {
    Add-NotRun 'sql-localdb-start' 'database' 'Exact repository subject prerequisite failed.' '' ''
}

$sqlConnected = $false
if ($localDbStarted) {
    $sqlConnected = Invoke-RequiredCommand 'sql-connectivity' 'database' 'sqlcmd.exe' @('-S', $SqlServer, '-b', '-Q', 'SET NOCOUNT ON; SELECT 1 AS Ready;') '01-sql-connectivity.log'
}
else {
    Add-NotRun 'sql-connectivity' 'database' 'SQL LocalDB start prerequisite failed.' '' ''
}

$toolRestorePassed = $false
if ($subjectPassed) {
    $toolRestorePassed = Invoke-RequiredCommand 'dotnet-tool-restore' 'build' 'dotnet.exe' @('tool', 'restore') '02-dotnet-tool-restore.log'
}
else {
    Add-NotRun 'dotnet-tool-restore' 'build' 'Exact repository subject prerequisite failed.' '' ''
}

$restorePassed = $false
if ($subjectPassed) {
    $restorePassed = Invoke-RequiredCommand 'solution-restore' 'build' 'dotnet.exe' @('restore', (Join-Path $repositoryRoot 'UnicoreCRM.slnx')) '03-solution-restore.log'
}
else {
    Add-NotRun 'solution-restore' 'build' 'Exact repository subject prerequisite failed.' '' ''
}

$buildPassed = $false
if ($restorePassed) {
    $buildPassed = Invoke-RequiredCommand 'solution-build' 'build' 'dotnet.exe' @('build', (Join-Path $repositoryRoot 'UnicoreCRM.slnx'), '--configuration', 'Debug', '--no-restore') '04-solution-build.log'
}
else {
    Add-NotRun 'solution-build' 'build' 'Solution restore prerequisite failed.' '' ''
}

$verifiers = @(
    [pscustomobject]@{ Name='identity-auth-core-and-abuse-protection'; Category='IdentityAuth'; Script='verify-identity-auth-abuse-protection.ps1'; DatabaseSuffix='IdentityAbuse'; Port=5411; Extra=@() },
    [pscustomobject]@{ Name='email-verification-otp'; Category='IdentityAuth'; Script='verify-email-verification-otp.ps1'; DatabaseSuffix='EmailOtp'; Port=$null; Extra=@() },
    [pscustomobject]@{ Name='identity-session-read-audit'; Category='IdentityAuth'; Script='verify-identity-read-audit.ps1'; DatabaseSuffix='IdentityRead'; Port=5412; Extra=@() },
    [pscustomobject]@{ Name='workspace-list-read-audit'; Category='Workspace'; Script='verify-list-my-workspaces-read-audit.ps1'; DatabaseSuffix='WorkspaceList'; Port=5413; Extra=@() },
    [pscustomobject]@{ Name='workspace-bootstrap-trust-read-audit'; Category='Workspace'; Script='verify-get-workspace-bootstrap-read-audit.ps1'; DatabaseSuffix='WorkspaceBootstrap'; Port=5414; Extra=@() },
    [pscustomobject]@{ Name='initial-workspace-provisioning'; Category='Provisioning'; Script='verify-initial-workspace-provisioning.ps1'; DatabaseSuffix='Provisioning'; Port=$null; Extra=@() },
    [pscustomobject]@{ Name='initial-workspace-provisioning-upgrade'; Category='Provisioning'; Script='verify-initial-workspace-provisioning-upgrade.ps1'; DatabaseSuffix='ProvisioningUpgrade'; Port=$null; Extra=@() },
    [pscustomobject]@{ Name='access-control-record-access'; Category='AccessControl'; Script='verify-access-control-record-access.ps1'; DatabaseSuffix='RecordAccess'; Port=5415; Extra=@('-ReadyTimeoutSeconds','420') },
    [pscustomobject]@{ Name='access-control-create-role'; Category='AccessControl'; Script='verify-create-access-role.ps1'; DatabaseSuffix='CreateRole'; Port=5416; Extra=@() },
    [pscustomobject]@{ Name='access-control-replace-role'; Category='AccessControl'; Script='verify-replace-access-role.ps1'; DatabaseSuffix='ReplaceRole'; Port=5417; Extra=@() },
    [pscustomobject]@{ Name='access-control-archive-role'; Category='AccessControl'; Script='verify-archive-access-role.ps1'; DatabaseSuffix='ArchiveRole'; Port=5418; Extra=@() },
    [pscustomobject]@{ Name='access-control-replace-member-access'; Category='AccessControl'; Script='verify-replace-workspace-member-access.ps1'; DatabaseSuffix='ReplaceMember'; Port=5419; Extra=@() },
    [pscustomobject]@{ Name='access-control-directory'; Category='AccessControl'; Script='verify-get-workspace-access-directory.ps1'; DatabaseSuffix='Directory'; Port=5420; Extra=@() },
    [pscustomobject]@{ Name='access-control-administrative-body-limits'; Category='AccessControl'; Script='verify-access-control-administrative-body-limits.ps1'; DatabaseSuffix='BodyLimits'; Port=5421; Extra=@() }
)

foreach ($verifier in $verifiers) {
    $scriptPath = Join-Path $PSScriptRoot $verifier.Script
    $databaseName = "$($databasePrefix)_$($verifier.DatabaseSuffix)"
    if (-not ($buildPassed -and $toolRestorePassed -and $sqlConnected)) {
        Add-NotRun $verifier.Name $verifier.Category 'Build, local tool, or database prerequisite failed.' $scriptPath $databaseName
        continue
    }
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Add-Result $verifier.Name $verifier.Category 'FAIL' 1 0 '' '' 'Required verifier script is missing.' $scriptPath $databaseName
        continue
    }
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath, '-DatabaseName', $databaseName)
    if ($null -ne $verifier.Port) {
        $arguments += @('-SqlServer', $SqlServer, '-Port', [string]$verifier.Port)
    }
    $arguments += @($verifier.Extra)
    Invoke-RequiredCommand $verifier.Name $verifier.Category 'powershell.exe' $arguments ("10-{0}.log" -f $verifier.Name) $scriptPath $databaseName | Out-Null
}

$unicodeScript = Join-Path $PSScriptRoot 'verify-create-access-role-unicode-upgrade.ps1'
$unicodeDatabases = @(
    "$($databasePrefix)_UnicodeFresh",
    "$($databasePrefix)_UnicodeHistorical",
    "$($databasePrefix)_UnicodeCollision"
)
if (-not ($buildPassed -and $toolRestorePassed -and $sqlConnected)) {
    Add-NotRun 'access-control-role-unicode-migration-upgrade' 'AccessControl' 'Build, local tool, or database prerequisite failed.' $unicodeScript ($unicodeDatabases -join ',')
}
elseif (-not (Test-Path -LiteralPath $unicodeScript)) {
    Add-Result 'access-control-role-unicode-migration-upgrade' 'AccessControl' 'FAIL' 1 0 '' '' 'Required verifier script is missing.' $unicodeScript ($unicodeDatabases -join ',')
}
else {
    $unicodeArguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $unicodeScript,
        '-FreshDatabaseName', $unicodeDatabases[0],
        '-HistoricalDatabaseName', $unicodeDatabases[1],
        '-CollisionDatabaseName', $unicodeDatabases[2],
        '-SqlServer', $SqlServer,
        '-Port', '5422'
    )
    Invoke-RequiredCommand 'access-control-role-unicode-migration-upgrade' 'AccessControl' 'powershell.exe' $unicodeArguments '10-access-control-role-unicode-migration-upgrade.log' $unicodeScript ($unicodeDatabases -join ',') | Out-Null
}

$passedCount = @($script:Results | Where-Object { $_.status -eq 'PASS' }).Count
$failedCount = @($script:Results | Where-Object { $_.status -eq 'FAIL' }).Count
$notRunCount = @($script:Results | Where-Object { $_.status -eq 'NOT_RUN' }).Count
$gateStatus = if ($failedCount -eq 0 -and $notRunCount -eq 0) { 'PASS' } else { 'FAIL' }
$gateScriptHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$workflowPath = Join-Path $repositoryRoot '.github/workflows/platform-ci.yml'
$workflowHash = if (Test-Path -LiteralPath $workflowPath) { (Get-FileHash -LiteralPath $workflowPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
$manifest = [pscustomobject]@{
    schemaVersion = 1
    gate = 'PLAT-QA-01-platform-required-gate'
    status = $gateStatus
    subjectKind = $subjectKind
    commit = $headCommit
    expectedCiCommit = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) { $null } else { $env:GITHUB_SHA }
    branch = $branch
    initialWorkingTree = $initialWorkingTree
    allowDirtyWorkingTree = [bool]$AllowDirtyWorkingTree
    runId = $safeRunId
    startedAtUtc = $script:GateStartedAt.ToString('o')
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    gateScriptSha256 = $gateScriptHash
    workflowSha256 = $workflowHash
    requiredCheckCount = $script:Results.Count
    passedCount = $passedCount
    failedCount = $failedCount
    notRunCount = $notRunCount
    checks = $script:Results
}
$manifestPath = Join-Path $evidenceRoot 'platform-ci-evidence.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$summaryLines = New-Object 'System.Collections.Generic.List[string]'
$summaryLines.Add('# Platform CI required gate')
$summaryLines.Add('')
$summaryLines.Add("- Status: **$gateStatus**")
$summaryLines.Add(('- Subject: `{0} {1}`' -f $subjectKind, $headCommit))
$summaryLines.Add("- Required checks: $($script:Results.Count); PASS: $passedCount; FAIL: $failedCount; NOT_RUN: $notRunCount")
$summaryLines.Add('')
$summaryLines.Add('| Required check | Area | Status |')
$summaryLines.Add('| --- | --- | --- |')
foreach ($result in $script:Results) {
    $summaryLines.Add("| $($result.name) | $($result.category) | $($result.status) |")
}
$summaryPath = Join-Path $evidenceRoot 'platform-ci-summary.md'
$summaryLines | Set-Content -LiteralPath $summaryPath -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $summaryLines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}

Write-Host ''
Write-Host '===== PLATFORM CI REQUIRED GATE ====='
$script:Results | ForEach-Object { Write-Host ("{0,-52} {1}" -f $_.name, $_.status) }
Write-Host "STATUS=$gateStatus PASS=$passedCount FAIL=$failedCount NOT_RUN=$notRunCount"
Write-Host "Evidence: $manifestPath"

if ($gateStatus -ne 'PASS') { exit 1 }
exit 0
