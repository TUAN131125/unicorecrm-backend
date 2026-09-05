<#
.SYNOPSIS
    Maintained Lead lifecycle and field-write regression suite.
.DESCRIPTION
    Run by verify-access-control-record-access.ps1 against its real HTTP host and isolated SQL
    database. Reuses that verifier's fixture, HTTP/SQL helpers and canonical field-policy setup.
    Execute the record-access verifier to run this suite together with adjacent policy regressions.
#>

function Invoke-LeadLifecycleVerification {
function Get-LifecycleEffects {
    param([string] $LeadId)
    return Get-Scalar -Database $DatabaseName -Query @"
SELECT (SELECT * FROM leads.Leads WHERE LeadId='$LeadId' FOR JSON PATH) AS LeadState,
       (SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$LeadId') AS Audits,
       (SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId='$LeadId') AS Outbox,
       (SELECT COUNT(*) FROM leads.IdempotencyRecords WHERE TargetId='$LeadId') AS Commands
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
"@
}

# Interactive acquisition accepts only the information available at first contact. Owner is
# resolved from the trusted Workspace member; unknown enrichment stays absent rather than becoming
# a fabricated default. The same records exercise the authoritative search and cursor contract.
$minimalOne = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-lead-runtime-minimal-1' `
    -Body (@{ displayName = 'Runtime pagination alpha'; phone = '09077770001' } | ConvertTo-Json -Compress)
$minimalTwo = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-lead-runtime-minimal-2' `
    -Body (@{ displayName = 'Runtime pagination beta'; phone = '09077770002' } | ConvertTo-Json -Compress)
Add-Result 'runtime: name and phone create succeeds' '201|201' "$($minimalOne.Status)|$($minimalTwo.Status)"
Add-Result 'runtime: interactive owner is the trusted member' $callerMemberId ([string]$minimalOne.Body.result.ownerId)
$minimalStored = (Get-Scalar -Database $DatabaseName -Query "SELECT Profile AS Value FROM leads.Leads WHERE LeadId='$($minimalOne.Body.aggregateId)'") | ConvertFrom-Json
Add-Result 'runtime: unknown source remains absent' '' ([string]$minimalStored.source)
Add-Result 'runtime: unknown estimated value remains absent' '' ([string]$minimalStored.estimatedValue)

$firstPage = Invoke-Support -Method 'GET' -Path '/leads?search=Runtime%20pagination&limit=1'
Add-Result 'runtime: first page is bounded' '1' ([string]$firstPage.Body.items.Count)
Add-Result 'runtime: continuation is authoritative' 'True' ([string]($firstPage.Body.pageInfo.hasNextPage -and $firstPage.Body.pageInfo.nextCursor))
$secondPage = Invoke-Support -Method 'GET' -Path "/leads?search=Runtime%20pagination&limit=1&cursor=$($firstPage.Body.pageInfo.nextCursor)"
Add-Result 'runtime: continuation returns a distinct Lead' 'True' ([string]($secondPage.Body.items[0].id -ne $firstPage.Body.items[0].id))
Add-Result 'runtime: stable traversal reports both matching Leads' '2' ([string]$firstPage.Body.pageInfo.totalCount)
$phoneSearch = Invoke-Support -Method 'GET' -Path '/leads?search=09077770002&limit=1'
Add-Result 'runtime: server phone search finds a Lead beyond page one' $minimalTwo.Body.aggregateId ([string]$phoneSearch.Body.items[0].id)

$lifecycleOriginal = @{
    companyName = 'Original company'
    painPoint = 'Original pain'
    nextFollowUpAt = '2026-10-01T09:00:00.0000000Z'
}

foreach ($restriction in @('ReadOnly', 'Hidden', 'Masked')) {
    foreach ($protectedField in @('companyName', 'painPoint', 'nextFollowUpAt')) {
        Clear-GateField
        $label = "lifecycle: $restriction $protectedField"
        $keyPrefix = "idem-8a-$restriction-$protectedField"
        $profile = @{
            displayName = "8A $restriction $protectedField"
            ownerId = $callerMemberId
            source = 'manual'
            phone = '0900000002'
            estimatedValue = @{ amount = '10'; currency = 'USD' }
            companyName = $lifecycleOriginal.companyName
            painPoint = $lifecycleOriginal.painPoint
            nextFollowUpAt = $lifecycleOriginal.nextFollowUpAt
        }
        $created = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey "$keyPrefix-create" `
            -Body ($profile | ConvertTo-Json -Compress -Depth 6)
        Add-Result "$label fixture created" '201' $created.Status
        $lifecycleLeadId = $created.Body.aggregateId
        $route = "/leads/$lifecycleLeadId/advance-work-state"
        $contacting = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey "$keyPrefix-contacting" `
            -IfMatchVersion $created.Body.version -Body '{"targetWorkState":"CONTACTING"}'
        Add-Result "$label enters CONTACTING" '200' $contacting.Status
        $version = $contacting.Body.version
        $before = Get-LifecycleEffects $lifecycleLeadId

        Set-GateField -Resource 'leads' -Field $protectedField -Access $restriction
        $requested = @{
            companyName = 'Changed company'
            painPoint = 'Changed pain'
            nextFollowUpAt = '2026-11-01T09:00:00.0000000Z'
        }
        $denied = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey "$keyPrefix-denied" -IfMatchVersion $version `
            -Body (@{ targetWorkState = 'VERIFYING'; verificationProfile = $requested } | ConvertTo-Json -Compress)
        Add-Result "$label changed value rejected" '403' $denied.Status
        Add-Result "$label uses canonical field denial" 'ACCESS_DENIED' $denied.Body.code
        Add-Result "$label denial identifies protected write" 'True' ([string]($denied.Raw -match $protectedField))
        Add-Result "$label rejection changes no Lead, audit, outbox or idempotency state" $before (Get-LifecycleEffects $lifecycleLeadId)

        # Keep the protected value unchanged and change the two permitted fields. The UTC value
        # deliberately uses different precision to prove comparison by value rather than text.
        $requested[$protectedField] = $lifecycleOriginal[$protectedField]
        if ($protectedField -eq 'nextFollowUpAt') { $requested[$protectedField] = '2026-10-01T09:00:00Z' }
        $allowedBody = @{ targetWorkState = 'VERIFYING'; verificationProfile = $requested } | ConvertTo-Json -Compress
        $stale = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey "$keyPrefix-stale" -IfMatchVersion '0' -Body $allowedBody
        Add-Result "$label stale If-Match remains rejected" '412' $stale.Status
        Add-Result "$label stale If-Match reports version conflict" 'VERSION_CONFLICT' $stale.Body.code
        Add-Result "$label stale rejection has zero Lead effects" $before (Get-LifecycleEffects $lifecycleLeadId)

        $allowed = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey "$keyPrefix-allowed" -IfMatchVersion $version -Body $allowedBody
        Add-Result "$label unchanged protected value allows transition" '200' $allowed.Status
        Add-Result "$label transitions to VERIFYING" 'VERIFYING' $allowed.Body.result.leadWorkState
        $stored = (Get-Scalar -Database $DatabaseName -Query "SELECT Profile AS Value FROM leads.Leads WHERE LeadId='$lifecycleLeadId'") | ConvertFrom-Json
        Add-Result "$label effective company persists" $requested.companyName $stored.companyName
        Add-Result "$label effective pain point persists" $requested.painPoint $stored.painPoint
        Add-Result "$label effective follow-up instant persists" `
            ([DateTimeOffset]::Parse($requested.nextFollowUpAt).UtcTicks.ToString()) `
            ([DateTimeOffset]::Parse([string]$stored.nextFollowUpAt).UtcTicks.ToString())
        $after = Get-LifecycleEffects $lifecycleLeadId
        $replay = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey "$keyPrefix-allowed" -IfMatchVersion $version -Body $allowedBody
        Add-Result "$label completed command still replays" '200|REPLAYED' "$($replay.Status)|$($replay.Body.outcome)"
        Add-Result "$label replay has zero Lead effects" $after (Get-LifecycleEffects $lifecycleLeadId)
        Clear-GateField
    }
}

# Existing capability, state-field policy and lifecycle validation remain mandatory.
$newLead = Invoke-Support -Method 'POST' -Path '/leads' -IdempotencyKey 'idem-8a-lifecycle-guards-create' `
    -Body (@{ displayName = '8A lifecycle guards'; ownerId = $callerMemberId; source = 'manual'; phone = '0900000002'; estimatedValue = @{ amount = '0'; currency = 'USD' } } | ConvertTo-Json -Compress)
Add-Result 'lifecycle: guard fixture created' '201' $newLead.Status
$lifecycleLeadId = $newLead.Body.aggregateId
$route = "/leads/$lifecycleLeadId/advance-work-state"
$before = Get-LifecycleEffects $lifecycleLeadId
$invalid = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey 'idem-8a-invalid-jump' -IfMatchVersion '0' -Body '{"targetWorkState":"VERIFYING"}'
Add-Result 'lifecycle: NEW cannot skip CONTACTING' '409|LEAD_INVALID_TRANSITION' "$($invalid.Status)|$($invalid.Body.code)"
Set-GateField -Resource 'leads' -Field 'leadWorkState' -Access 'ReadOnly'
$stateDenied = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey 'idem-8a-state-denied' -IfMatchVersion '0' -Body '{"targetWorkState":"CONTACTING"}'
Add-Result 'lifecycle: leadWorkState field policy remains mandatory' '403|ACCESS_DENIED' "$($stateDenied.Status)|$($stateDenied.Body.code)"
Clear-GateField
Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.update'"
try {
    $capabilityDenied = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey 'idem-8a-update-denied' -IfMatchVersion '0' -Body '{"targetWorkState":"CONTACTING"}'
    Add-Result 'lifecycle: leads.update remains mandatory' '403|ACCESS_DENIED' "$($capabilityDenied.Status)|$($capabilityDenied.Body.code)"
}
finally {
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','leads.update')"
}
Add-Result 'lifecycle: denied capability, state-field and transition have zero Lead effects' $before (Get-LifecycleEffects $lifecycleLeadId)

$incompleteProfile = @{ displayName = '8A incomplete'; ownerId = $callerMemberId; source = 'manual'; estimatedValue = @{ amount = '0'; currency = 'USD' } } | ConvertTo-Json -Compress
$replaced = Invoke-Support -Method 'PUT' -Path "/leads/$lifecycleLeadId" -IdempotencyKey 'idem-8a-incomplete-profile' -IfMatchVersion '0' -Body $incompleteProfile
Add-Result 'lifecycle: incomplete profile fixture persists' '200' $replaced.Status
$before = Get-LifecycleEffects $lifecycleLeadId
$incomplete = Invoke-Support -Method 'POST' -Path $route -IdempotencyKey 'idem-8a-incomplete-state' -IfMatchVersion $replaced.Body.version -Body '{"targetWorkState":"CONTACTING"}'
Add-Result 'lifecycle: progressive profile validation remains mandatory' '422|LEAD_PROGRESSIVE_PROFILE_INCOMPLETE' "$($incomplete.Status)|$($incomplete.Body.code)"
Add-Result 'lifecycle: incomplete profile rejection has zero Lead effects' $before (Get-LifecycleEffects $lifecycleLeadId)
}

Invoke-LeadLifecycleVerification
