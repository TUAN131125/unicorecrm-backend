#requires -Version 7.0
$ErrorActionPreference='Stop'
$entry=Join-Path $PSScriptRoot 'seed-realistic-demo.ps1'
$clock='2026-10-22T05:00:00Z'
$first=& pwsh -NoProfile -File $entry -AsOf $clock | ConvertFrom-Json -AsHashtable
$second=& pwsh -NoProfile -File $entry -AsOf $clock | ConvertFrom-Json -AsHashtable
if($first.mode -ne 'DRY_RUN' -or $first.sample.lead.estimatedValue.amount -ne $second.sample.lead.estimatedValue.amount -or $first.sample.organization.employeeCount -ne $second.sample.organization.employeeCount){throw 'Deterministic dry run failed'}
$different=& pwsh -NoProfile -File $entry -AsOf $clock -RandomSeed 7 | ConvertFrom-Json -AsHashtable
if($first.sample.lead.estimatedValue.amount -eq $different.sample.lead.estimatedValue.amount){throw 'Random seed does not influence dataset'}
$before=$env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT='Production'
    $null=& pwsh -NoProfile -File $entry -AsOf $clock 2>&1
    if($LASTEXITCODE -eq 0){throw 'Production dry run must fail closed'}
    $env:ASPNETCORE_ENVIRONMENT='Development'
    $null=& pwsh -NoProfile -File $entry -Apply 2>&1
    if($LASTEXITCODE -eq 0){throw 'Apply without database confirmation must fail closed'}
    $null=& pwsh -NoProfile -File $entry -Apply -ConfirmedSyntheticDevelopmentDatabase -ExpectedServer 'invalid.example.com' -ExpectedDatabase 'UnicoreCRM_Development' 2>&1
    if($LASTEXITCODE -eq 0){throw 'Different configured database identity must fail before any mutation'}
} finally {$env:ASPNETCORE_ENVIRONMENT=$before}
Write-Host 'PASS: deterministic dry run, seed variation, Production rejection, confirmation requirement, database identity mismatch'
