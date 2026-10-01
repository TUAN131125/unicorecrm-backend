param(
    [Parameter(Mandatory=$true)][string] $DatabaseName,
    # The runtime owner must explicitly release the shared build/migration lane first.
    [switch] $RuntimeReady
)
$ErrorActionPreference='Stop'
if ($DatabaseName -notmatch '^UnicoreCRM_O2Claim_O4_[A-Za-z0-9_]+$') { throw 'Use an isolated UnicoreCRM_O2Claim_O4_ database.' }
if (!$RuntimeReady) { throw 'Wait for the runtime owner to confirm build/migration readiness, then supply -RuntimeReady.' }
# The inherited O1 fixture recreates its database. Refuse existing databases before invoking it.
$exists=& sqlcmd -S '(localdb)\MSSQLLocalDB' -d master -b -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='$DatabaseName';"
if ($LASTEXITCODE -ne 0 -or (($exists -join '').Trim()) -ne '0') { throw 'A fresh database name is required; this verifier never replaces an existing database.' }
& dotnet build "$PSScriptRoot/../UnicoreCRM.slnx" --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Full backend build failed.' }
& dotnet build "$PSScriptRoot/LeadHandoverRealVerifier/LeadHandoverRealVerifier.csproj" --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Real recovery verifier build failed.' }
# Keep O1/O2 checks and helpers unchanged, including real ingress, JWT actors and SQL migrations.
. "$PSScriptRoot/verify-lead-queue-claim.ps1" -DatabaseName $DatabaseName
$client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromSeconds(30)
$hostProcess=$null
$o4=[Collections.Generic.List[string]]::new()
$races=[Collections.Generic.List[object]]::new()
$verifierDll=(Resolve-Path "$PSScriptRoot/LeadHandoverRealVerifier/bin/Debug/net10.0/UnicoreCRM.LeadHandover.RealVerifier.dll").Path
function Check-O4([bool] $condition,[string] $name) { if (!$condition) { throw "O4: $name" };$o4.Add($name) }
function Handover-Body([string] $owner,[string] $reason='Territory handover') {
    return @{nextOwnerId=$owner;reason=$reason}|ConvertTo-Json -Compress
}
function Handover([string] $id,[string] $owner,[string] $key,[long] $version=0,[string] $reason='Territory handover') {
    return Send-Json 'POST' "/workflows/lead-handover/$id" (Handover-Body $owner $reason) (Claim-Headers $authorization ("handover-"+$key) $version)
}
function Owned-Lead([string] $name) {
    $id=New-QueueLead $name
    Invoke-Sql "UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile,'$.ownerId','$memberId'),ScopeOwnerId='$memberId' WHERE LeadId='$id';"
    return $id
}
function Lead-Version([string] $id) { return [long](Invoke-SqlScalar "SELECT Version FROM leads.Leads WHERE LeadId='$id';") }
function Hash-Tasks([string] $predicate) {
    return Invoke-SqlScalar "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM tasks.Tasks WHERE $predicate ORDER BY TaskId FOR JSON PATH,INCLUDE_NULL_VALUES)),2);"
}
function Task-Fixtures([string] $id) {
    foreach($kind in @('open_a','open_b','complete','cancel','archive','foreign','module')) {
        $status=if($kind -eq 'complete'){1}elseif($kind -eq 'cancel'){2}else{0}
        $record=if($kind -eq 'foreign'){'lead_other_o4'}else{$id}
        $module=if($kind -eq 'module'){'deals'}else{'leads'}
        $assignee=if($kind -eq 'open_b'){$bMember}else{$memberId}
        Invoke-Sql "INSERT INTO tasks.Tasks(TaskId,WorkspaceId,Title,Status,Priority,AssigneeId,DueAt,RecordModuleKey,RecordId,RecordLabel,CreatedAt,UpdatedAt,Version,ArchivedAt) VALUES ('o4_${id}_$kind','$workspaceId','O4 fixture',$status,1,'$assignee',DATEADD(day,1,SYSUTCDATETIME()),'$module','$record','O4',SYSUTCDATETIME(),SYSUTCDATETIME(),0,$(if($kind -eq 'archive'){'SYSUTCDATETIME()'}else{'NULL'}));"
    }
    Invoke-Sql "INSERT INTO tasks.Activities(ActivityId,WorkspaceId,Type,Subject,Body,ActorId,OccurredAt,RecordModuleKey,RecordId,RecordLabel,Version) VALUES ('o4_activity_$id','$workspaceId',0,'Historical O4','Keep authorship','$memberId',SYSUTCDATETIME(),'leads','$id','O4',0);"
}
function Assert-Completion([string] $id,$doc,[string] $owner,[int] $sla) {
    Check-O4 ($doc.result.lead.ownerId -eq $owner) 'Result contains authoritative Lead owner'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$id' AND ScopeOwnerId='$owner' AND JSON_VALUE(Profile,'$.ownerId')='$owner' AND PendingHandoverId IS NULL;") -eq '1') 'Lead completion clears exact reservation'
    $task=$doc.result.handoverTaskId;$anchor=$doc.commandId
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.LeadHandoverAnchors WHERE HandoverId='$anchor' AND LeadId='$id' AND WorkspaceId='$workspaceId' AND NewOwnerId='$owner' AND Reason='Territory handover' AND LEN(RequestFingerprint)=64;") -eq '1') 'Anchor persists canonical owner reason and request fingerprint'

    Check-O4 ($doc.version -eq (Lead-Version $id)) 'Response Lead version matches committed SQL version'
    # PowerShell 7 can deserialize ISO strings as DateTime; a direct cast preserves subsecond ticks.
    Check-O4 ([DateTimeOffset]$doc.result.handoverTaskDueAt -eq [DateTimeOffset]::Parse((Invoke-SqlScalar "SELECT CONVERT(varchar(40),TakeoverDueAt,127) FROM workflow.LeadHandoverAnchors WHERE HandoverId='$anchor';"))) 'Public dueAt matches frozen SQL instant'
    $proofCount=1+@($doc.result.reassignedTaskIds).Count
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.OutboxMessages WHERE JSON_VALUE(PayloadJson,'$.handoverId')='$anchor';") -eq "$proofCount") 'Task outbox exactly once for snapshot and takeover'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.AuditRecords WHERE Operation='leadHandoverTasks' AND AggregateId='$task';") -eq '1') 'Takeover audit exactly once'
    Check-O4 (@($doc.emittedEventIds|Select-Object -Unique).Count -eq ($proofCount+2) -and @($doc.auditEvidenceIds|Select-Object -Unique).Count -eq ($proofCount+2)) 'Response includes all Lead and Task participant evidence'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId='$task' AND WorkspaceId='$workspaceId' AND SourceType='LEAD_HANDOVER' AND SourceId='$anchor' AND SourceEvidence='Territory handover' AND RecordModuleKey='leads' AND RecordId='$id' AND AssigneeId='$owner' AND Status=0 AND Priority=1;") -eq '1') 'Takeover canonical source reason owner status NORMAL'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE SourceType='LEAD_HANDOVER' AND SourceId='$anchor';") -eq '1') 'Exactly one takeover per workflow'
    Check-O4 ($doc.result.resolvedHandoverAcceptanceSlaHours -eq $sla) 'Returned resolved SLA'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.LeadHandoverAnchors a JOIN tasks.Tasks t ON t.SourceId=a.HandoverId WHERE a.HandoverId='$anchor' AND a.CompletedAt IS NOT NULL AND a.ActiveLeadKey IS NULL AND a.ResolvedSlaHours=$sla AND a.TakeoverDueAt=DATEADD(hour,$sla,a.HandoverOccurredAt) AND t.DueAt=a.TakeoverDueAt;") -eq '1') 'Frozen anchor SLA and Task dueAt agree'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$id' AND Operation='handoverLead:complete' AND JSON_VALUE(EvidenceJson,'$.handoverId')='$anchor';") -eq '1') 'Exactly one completion audit per workflow'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId='$id' AND EventType='LEAD_HANDOVER_COMPLETED' AND JSON_VALUE(PayloadJson,'$.handoverId')='$anchor';") -eq '1') 'Exactly one completion event per workflow'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.IntegrationOutboxMessages WHERE JSON_VALUE(IntegrationEnvelopeJson,'$.data.handoverId')='$anchor';") -eq '1') 'Exactly one workflow integration outbox per completed handover'
}
function New-O4Message([string] $path,[string] $body,[hashtable] $headers) {
    $message=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$baseUrl$path")
    $message.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
    foreach($k in $headers.Keys){$null=$message.Headers.TryAddWithoutValidation($k,[string]$headers[$k])};return $message
}
function Race-O4($left,$right) {
    $a=$client.SendAsync($left);$b=$client.SendAsync($right)
    try {
        Check-O4 ([Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($a,$b),35000)) 'Real HTTP race bounded within 35 seconds'
        return @([int]$a.Result.StatusCode,[int]$b.Result.StatusCode)
    } finally { $left.Dispose();$right.Dispose();if($a.Status -eq 'RanToCompletion'){$a.Result.Dispose()};if($b.Status -eq 'RanToCompletion'){$b.Result.Dispose()} }
}
try {
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('workflow.LeadHandoverAnchors') AND name='OpenTaskPolicy';") -eq '0') 'Fresh migrated anchor has no OpenTaskPolicy column'
    # NextOwnerId deliberately maps to historical NewOwnerId storage; migration history is preserved.
    foreach($column in @('PreviousOwnerId','NewOwnerId','Reason','RequestFingerprint','ResolvedSlaHours','HandoverOccurredAt','TakeoverDueAt','ActiveLeadKey','OriginalPrincipalId','ExecutionPrincipalId','ResponseJson')) {
        Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('workflow.LeadHandoverAnchors') AND name='$column';") -eq '1') "Canonical persisted anchor field $column exists"
    }
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.__EFMigrationsHistory WHERE MigrationId IN ('20261001044323_LeadHandoverAnchor','20261001105512_CanonicalLeadHandoverContract');") -eq '2') 'Original and corrective Workflow migration history both retained'
    $canonicalMigration=Get-Content "$PSScriptRoot/../src/UnicoreCRM.Workflows/Atomic/Infrastructure/Persistence/Migrations/20261001105512_CanonicalLeadHandoverContract.cs" -Raw
    $canonicalUp=$canonicalMigration.Substring($canonicalMigration.IndexOf('protected override void Up'),$canonicalMigration.IndexOf('protected override void Down')-$canonicalMigration.IndexOf('protected override void Up'))
    Check-O4 ($canonicalUp.Contains('[ActiveLeadKey] IS NOT NULL') -and $canonicalUp.IndexOf('THROW 51000') -ge 0 -and $canonicalUp.IndexOf('THROW 51000') -lt $canonicalUp.IndexOf('migrationBuilder.DropColumn')) 'Corrective Up guards active historical anchors before policy drop'
    # The inherited ingress bootstrap predates Studio. Use current production defaults for this fixture.
    $env:LeadHandoverVerifier__WorkspaceId=$workspaceId
    & dotnet $verifierDll --seed-studio | Out-Host
    if($LASTEXITCODE -ne 0){throw 'Studio fixture initialization failed'}
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.WorkspaceServiceCapabilityGrants WHERE WorkspaceId='$workspaceId' AND ServicePrincipalId='svc_lead_handover_recovery' AND Capability='leads.handover.recover';") -eq '1') 'Development workspace has exactly one canonical recovery service grant'
    $leadDown=Get-Content "$PSScriptRoot/../src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/20261001043955_LeadHandoverReservation.cs" -Raw
    $workflowDown=Get-Content "$PSScriptRoot/../src/UnicoreCRM.Workflows/Atomic/Infrastructure/Persistence/Migrations/20261001044323_LeadHandoverAnchor.cs" -Raw
    Check-O4 ($leadDown.Contains('THROW 51000') -and $leadDown.IndexOf('THROW 51000') -lt $leadDown.IndexOf('migrationBuilder.DropColumn')) 'Static Leads rollback guard precedes reservation column removal'
    Check-O4 ($workflowDown.Contains('THROW 51000') -and $workflowDown.IndexOf('THROW 51000') -lt $workflowDown.IndexOf('migrationBuilder.DropTable')) 'Static Workflow rollback guard precedes anchor removal'
    $hostProcess=Start-ApiHost $false $workspaceId $memberId
    Invoke-Sql "UPDATE integration.InboundBindings SET IsEnabled=1,DelegatedMemberId='$memberId' WHERE IntegrationId='int_inbound_lead_webhook';"
    $lead=Owned-Lead 'O4 Validation';$nullLead=New-QueueLead 'O4 Unassigned'
    Assert-Status (Handover $lead $bMember 'o4-no-assign') 403 'Missing leads.assign'
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','leads.assign');"
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','studio.read'),('$roleId','studio.configure');"
    $studio=Send-Json 'GET' '/workspace-configuration' $null $authorization
    Assert-Status $studio 200 'Read real Studio SLA configuration'
    $studioDoc=$studio.Body|ConvertFrom-Json
    Check-O4 ($studioDoc.blueprint.workflow.handoverAcceptanceSlaHours -eq 24) 'Default Studio SLA 24'
    foreach($hours in @(0,169)) {
        $studioDoc.blueprint.workflow.handoverAcceptanceSlaHours=$hours
        $body=@{blueprint=$studioDoc.blueprint;features=$studioDoc.features}|ConvertTo-Json -Depth 20 -Compress
        $rangeResponse=Send-Json 'PATCH' '/workspace-configuration/blueprint' $body (Claim-Headers $authorization "o4-sla-range-$hours" $studioDoc.revision)
        Assert-Status $rangeResponse 422 'Studio rejects SLA outside 1..168'
        Check-O4 ($null -ne ($rangeResponse.Body|ConvertFrom-Json).fieldErrors.'blueprint.workflow.handoverAcceptanceSlaHours') 'SLA range error identifies exact field'
    }
    Assert-Status (Handover $nullLead $bMember 'o4-unassigned') 409 'Unassigned cannot handover'
    Assert-Status (Handover $lead $memberId 'o4-same') 409 'Same owner rejected'
    foreach($target in @('member_missing','bad id','')) { Assert-Status (Handover $lead $target ('o4-invalid-'+[Guid]::NewGuid().ToString('N'))) 422 'Invalid target rejected' }
    foreach($reason in @('', '   ')) { Assert-Status (Handover $lead $bMember ('o4-reason-'+[Guid]::NewGuid().ToString('N')) 0 $reason) 422 'Reason validation' }
    Assert-Status (Handover $lead $bMember 'o4-reason-1001' 0 ('r'*1001)) 422 'Reason 1001 characters rejected'
    $oldOwnerBody='{"newOwnerId":"'+$bMember+'","reason":"r"}'
    Assert-Status (Send-Json 'POST' "/workflows/lead-handover/$lead" $oldOwnerBody (Claim-Headers $authorization 'o4-old-owner-body')) 422 'Malformed obsolete newOwnerId body rejected'
    Assert-Status (Send-Json 'POST' "/leads/$lead/handover" (Handover-Body $bMember) (Claim-Headers $authorization 'o4-old-route')) 404 'Obsolete route absent'
    foreach($body in @('{}',('{"newOwnerId":"'+$bMember+'","reason":"r"}'),('{"nextOwnerId":"'+$bMember+'","reason":"r","openTaskPolicy":"obsolete"}'),('{"nextOwnerId":"'+$bMember+'","reason":"r","taskTargets":[]}'))) {
        Assert-Status (Send-Json 'POST' "/workflows/lead-handover/$lead" $body (Claim-Headers $authorization ('o4-body-'+[Guid]::NewGuid().ToString('N')))) 422 'Closed canonical body rejects obsolete members'
    }
    Assert-Status (Handover $lead $bMember 'o4-version' 9) 412 'Stale version'
    foreach($header in @('If-Match','Idempotency-Key','X-Request-Id','X-Correlation-Id')) {
        $h=Claim-Headers $authorization ('o4-header-'+$header);$h.Remove($header)
        $response=Send-Json 'POST' "/workflows/lead-handover/$lead" (Handover-Body $bMember) $h
        Check-O4 ($response.Status -in @(400,422,428)) "Required header $header rejected"
    }
    foreach($flag in @('ArchivedAt','PendingCustomerConversionId')) {
        $value=if($flag -eq 'ArchivedAt'){'SYSUTCDATETIME()'}else{"'o4_conversion'"}
        Invoke-Sql "UPDATE leads.Leads SET $flag=$value WHERE LeadId='$lead';"
        Assert-Status (Handover $lead $bMember "o4-$flag") 409 "$flag blocks handover"
        Invoke-Sql "UPDATE leads.Leads SET $flag=NULL WHERE LeadId='$lead';"
    }
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Inactive' WHERE MembershipId='wsm_claim_b';"
    Assert-Status (Handover $lead $bMember 'o4-inactive') 422 'Inactive target'
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Active',WorkspaceId='$foreignWorkspaceId' WHERE MembershipId='wsm_claim_b';"
    Assert-Status (Handover $lead $bMember 'o4-foreign-target') 422 'Foreign target'
    Invoke-Sql "UPDATE workspace.Memberships SET WorkspaceId='$workspaceId' WHERE MembershipId='wsm_claim_b';"
    $maxReasonLead=Owned-Lead 'O4 Max Reason'
    $maxReason=Handover $maxReasonLead $bMember 'o4-max-reason' 0 (' '+('r'*1000)+' ')
    Assert-Status $maxReason 200 'Trimmed 1000-character reason accepted'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE RecordId='$maxReasonLead' AND SourceType='LEAD_HANDOVER' AND LEN(SourceEvidence)=1000 AND SourceEvidence=REPLICATE('r',1000);") -eq '1') 'Takeover preserves complete trimmed reason without truncation'
    foreach($capability in @('tasks.create','tasks.assign')) {
        Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$capability';"
        Assert-Status (Handover $lead $bMember "o4-missing-$capability") 403 "$capability mandatory even with empty snapshot"
        Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','$capability');"
    }
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.handover';") -eq '0') 'Human role needs no handover capability'
    Task-Fixtures $lead
    foreach($field in @(@('leads','ownerId'),@('tasks','assigneeId'))) {
        Invoke-Sql "INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('o4_field_write','$workspaceId','$roleId','$($field[0])','$($field[1])','ReadOnly');"
        Assert-Status (Handover $lead $bMember ('o4-field-'+$field[0])) 403 'Required participant field-write authority enforced before mutation'
        Check-O4 ((Lead-Version $lead) -eq 0 -and (Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE RecordId='$lead' AND SourceType='LEAD_HANDOVER';") -eq '0') 'Field denial has no business mutation'
        Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='o4_field_write';"
    }
    # Both resources use OWN: the admitted execution must return success after A -> B.
    $ownLead=Owned-Lead 'O4 OWN initial success';Task-Fixtures $ownLead
    Invoke-Sql "UPDATE tasks.Tasks SET AssigneeId='$memberId' WHERE RecordId='$ownLead'; INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('o4_own_lead','$workspaceId','$roleId','leads','OWN','[]'),('o4_own_task','$workspaceId','$roleId','tasks','OWN','[]');"
    $ownResponse=Handover $ownLead $bMember 'o4-own'
    Assert-Status $ownResponse 200 'OWN A to B initial admitted execution returns success'
    $ownDoc=$ownResponse.Body|ConvertFrom-Json
    Assert-Completion $ownLead $ownDoc $bMember 24
    Check-O4 ((@($ownDoc.result.reassignedTaskIds|Sort-Object) -join ',') -eq (@("o4_${ownLead}_open_a","o4_${ownLead}_open_b"|Sort-Object) -join ',')) 'OWN exact eligible snapshot'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId IN ('o4_${ownLead}_open_a','o4_${ownLead}_open_b') AND AssigneeId='$bMember';") -eq '2') 'OWN eligible Tasks belong to B'
    $ownHash=Hash-Tasks "RecordId='$ownLead'"
    $ownReplay=Handover $ownLead $bMember 'o4-own'
    Assert-Status $ownReplay 200 'OWN completed replay uses current resource capabilities without post-transfer record scope'
    $ownReplayed=$ownReplay.Body|ConvertFrom-Json
    Check-O4 ($ownReplayed.outcome -eq 'REPLAYED' -and $ownReplayed.commandId -eq $ownDoc.commandId -and (($ownReplayed.result|ConvertTo-Json -Depth 20 -Compress) -eq ($ownDoc.result|ConvertTo-Json -Depth 20 -Compress))) 'OWN replay retains exact committed outcome'
    Check-O4 ((Hash-Tasks "RecordId='$ownLead'") -eq $ownHash) 'OWN replay has no duplicate or mutation'
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('o4_replay_lead_readonly','$workspaceId','$roleId','leads','ownerId','ReadOnly');"
    Assert-Status (Handover $ownLead $bMember 'o4-own') 200 'Completed replay requires readable owner but no field-write permission'
    Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='o4_replay_lead_readonly';"
    # Reproduce stored pre-repair wire hashes and result shape without legacy public input.
    $historicalAnchor=$ownDoc.commandId
    $oldIntent=[ordered]@{leadId=$ownLead;expectedVersion=0;newOwnerId=$bMember;reason='Territory handover';openTaskPolicy='MOVE_LEAD_OPEN_TASKS_TO_NEW_OWNER'}|ConvertTo-Json -Compress
    $oldHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($oldIntent)))
    $oldTaskIntent=[ordered]@{leadId=$ownLead;handoverId=$historicalAnchor;newOwnerId=$bMember;reason='Territory handover';openTaskPolicy='MOVE_LEAD_OPEN_TASKS_TO_NEW_OWNER';frozenDueAt=([DateTimeOffset]$ownDoc.result.handoverTaskDueAt).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",[Globalization.CultureInfo]::InvariantCulture);originalActorId=$memberId}|ConvertTo-Json -Compress
    $oldTaskHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($oldTaskIntent)))
    Invoke-Sql "SET QUOTED_IDENTIFIER ON; UPDATE workflow.LeadHandoverAnchors SET RequestFingerprint='$oldHash',ResponseJson=JSON_MODIFY(ResponseJson,'$.result.openTaskPolicy','MOVE_LEAD_OPEN_TASKS_TO_NEW_OWNER') WHERE HandoverId='$historicalAnchor'; UPDATE tasks.IdempotencyRecords SET Fingerprint='$oldTaskHash' WHERE IdempotencyKey='$historicalAnchor' AND Operation='leadHandoverTasks';"
    $historicalReplay=Handover $ownLead $bMember 'o4-own'
    Assert-Status $historicalReplay 200 'Historical committed anchor replays through canonical request after wire repair'
    $historicalDoc=$historicalReplay.Body|ConvertFrom-Json
    Check-O4 ($historicalDoc.commandId -eq $ownDoc.commandId -and $historicalDoc.outcome -eq 'REPLAYED' -and -not($historicalDoc.result.PSObject.Properties.Name -contains 'openTaskPolicy')) 'Historical outcome is retained without obsolete public policy'
    Check-O4 ((Hash-Tasks "RecordId='$ownLead'") -eq $ownHash) 'Historical replay does not mutate or rediscover Tasks'
    Assert-Status (Handover $ownLead $bMember 'o4-own' 0 'different reason') 409 'Historical key still rejects changed canonical intent'
    foreach($capability in @('leads.assign','tasks.assign','tasks.create')) {
        Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$capability';"
        Assert-Status (Handover $ownLead $bMember 'o4-own') 403 "OWN completed replay requires current $capability resource capability"
        Check-O4 ((Hash-Tasks "RecordId='$ownLead'") -eq $ownHash) 'Capability-denied replay preserves committed Tasks'
        Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','$capability');"
    }
    Invoke-Sql "INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('o4_replay_proof_field','$workspaceId','$roleId','tasks','dueAt','Hidden');"
    Assert-Status (Handover $ownLead $bMember 'o4-own') 403 'Completed replay cannot disclose a hidden Task proof field'
    Check-O4 ((Hash-Tasks "RecordId='$ownLead'") -eq $ownHash) 'Field-denied replay preserves committed Tasks'
    Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='o4_replay_proof_field';"
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId IN ('o4_own_lead','o4_own_task');"
    $moveLead=Owned-Lead 'O4 MOVE';Task-Fixtures $moveLead
    $activityHash=Invoke-SqlScalar $activitySnapshotSql
    $untouched="TaskId LIKE 'o4_${moveLead}_%' AND TaskId NOT IN ('o4_${moveLead}_open_a','o4_${moveLead}_open_b')"
    $unchanged=Hash-Tasks $untouched
    # One eligible Task outside OWN must deny the entire handover, including takeover creation.
    Invoke-Sql "INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('o4_task_scope','$workspaceId','$roleId','tasks','OWN','[]');"
    $allBefore=Hash-Tasks "TaskId LIKE 'o4_${moveLead}_%'"
    Assert-Status (Handover $moveLead $bMember 'o4-task-scope' 0) 404 'Any hidden eligible Task denies entire handover'
    Check-O4 ((Hash-Tasks "TaskId LIKE 'o4_${moveLead}_%'") -eq $allBefore -and (Lead-Version $moveLead) -eq 0) 'Denied handover has no partial changes'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE RecordId='$moveLead' AND SourceType='LEAD_HANDOVER';") -eq '0') 'Denied handover creates no takeover'
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='o4_task_scope';"
    Invoke-Sql "UPDATE workspace.StudioConfigurations SET BlueprintJson=JSON_MODIFY(BlueprintJson,'$.workflow.handoverAcceptanceSlaHours',72) WHERE WorkspaceId='$workspaceId';"
    $response=Handover $moveLead $bMember 'o4-move' 0;Assert-Status $response 200 'Automatic A to B'
    $moved=$response.Body|ConvertFrom-Json;Assert-Completion $moveLead $moved $bMember 72
    Check-O4 ((@($moved.result.reassignedTaskIds|Sort-Object) -join ',') -eq (@("o4_${moveLead}_open_a","o4_${moveLead}_open_b"|Sort-Object) -join ',')) 'Automatic transfer exact authoritative Task set'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId IN ('o4_${moveLead}_open_a','o4_${moveLead}_open_b') AND AssigneeId='$bMember';") -eq '2') 'All eligible Tasks moved'
    Check-O4 ((Hash-Tasks $untouched) -eq $unchanged) 'Completed cancelled archived foreign and other module byte unchanged'
    Invoke-Sql "UPDATE workspace.StudioConfigurations SET BlueprintJson=JSON_MODIFY(BlueprintJson,'$.workflow.handoverAcceptanceSlaHours',12) WHERE WorkspaceId='$workspaceId';"
    Invoke-Sql "INSERT INTO tasks.Tasks(TaskId,WorkspaceId,Title,Status,Priority,AssigneeId,DueAt,RecordModuleKey,RecordId,RecordLabel,CreatedAt,UpdatedAt,Version) VALUES ('o4_late_$moveLead','$workspaceId','After snapshot',0,1,'$memberId',DATEADD(day,1,SYSUTCDATETIME()),'leads','$moveLead','O4',SYSUTCDATETIME(),SYSUTCDATETIME(),0);"
    $lateHash=Hash-Tasks "TaskId='o4_late_$moveLead'"
    $replay=Handover $moveLead $bMember 'o4-move' 0;Assert-Status $replay 200 'Same intent replay'
    $replayed=$replay.Body|ConvertFrom-Json
    Check-O4 ($replayed.outcome -eq 'REPLAYED' -and $replayed.commandId -eq $moved.commandId -and $replayed.version -eq $moved.version -and (($replayed.result|ConvertTo-Json -Depth 20 -Compress) -eq ($moved.result|ConvertTo-Json -Depth 20 -Compress))) 'Replay exact result version dueAt after SLA change'
    Assert-Completion $moveLead $replayed $bMember 72
    Check-O4 ((Hash-Tasks "TaskId='o4_late_$moveLead'") -eq $lateHash) 'Replay excludes Task created after authoritative snapshot commit'
    foreach($argsChanged in @(@($memberId,0,'Territory handover'),@($bMember,1,'Territory handover'),@($bMember,0,'Changed reason'))) {
        Assert-Status (Handover $moveLead $argsChanged[0] 'o4-move' $argsChanged[1] $argsChanged[2]) 409 'Changed intent idempotency conflict'
    }
    # Register a third real member for B -> C repeatability proof.
    $cEmail='handover.c@example.test'
    Assert-Status (Send-Json 'POST' '/auth/accounts' (@{email=$cEmail;password=$password;displayName='O4 C'}|ConvertTo-Json -Compress) (Claim-Headers $authorization 'o4-register-c')) 201 'Register target C'
    $parts=(Invoke-SqlScalar "SELECT AccountId+'|'+MemberId FROM iam.Accounts WHERE Email='$cEmail';").Split('|');$cMember=$parts[1]
    Invoke-Sql "UPDATE iam.Accounts SET Status='Active',EmailVerifiedAt=SYSUTCDATETIME() WHERE AccountId='$($parts[0])'; INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('o4_c','$workspaceId','$($parts[0])','$cMember','Active',SYSUTCDATETIME());"
    $second=Send-Json 'POST' "/workflows/lead-handover/$moveLead" (Handover-Body $cMember) (Claim-Headers $bAuth 'handover-o4-repeat' $moved.version);Assert-Status $second 200 'Later B to C succeeds'
    Assert-Completion $moveLead ($second.Body|ConvertFrom-Json) $cMember 12
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.LeadHandoverAnchors WHERE LeadId='$moveLead' AND CompletedAt IS NOT NULL AND ActiveLeadKey IS NULL;") -eq '2') 'Both historical anchors retained'
    Check-O4 ((Invoke-SqlScalar $activitySnapshotSql) -eq $activityHash) 'Activities and historical authorship unchanged'
    $hiddenLead=Owned-Lead 'O4 Hidden';$foreignLead=Owned-Lead 'O4 Foreign'
    Invoke-Sql "UPDATE leads.Leads SET ScopeOwnerId='$bMember',Profile=JSON_MODIFY(Profile,'$.ownerId','$bMember') WHERE LeadId='$hiddenLead'; UPDATE leads.Leads SET WorkspaceId='$foreignWorkspaceId' WHERE LeadId='$foreignLead'; INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('o4_lead_scope','$workspaceId','$roleId','leads','OWN','[]');"
    $hidden=@()
    foreach($id in @('lead_o4_missing',$hiddenLead,$foreignLead)) {
        $response=Handover $id $cMember "o4-hidden-$id";Assert-Status $response 404 'Hidden Lead response'
        $hidden+=(($response.Body|ConvertFrom-Json)|Select-Object type,title,status,code,retryable|ConvertTo-Json -Compress)
    }
    Check-O4 (@($hidden|Select-Object -Unique).Count -eq 1) 'Missing foreign and inaccessible are indistinguishable'
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='o4_lead_scope';"
    $reserved=Owned-Lead 'O4 Direct Reservation'
    Invoke-Sql "UPDATE leads.Leads SET PendingHandoverId='handover_o4_reserved' WHERE LeadId='$reserved';"
    Assert-Status (Handover $reserved $bMember 'o4-reserved') 409 'Second Handover reservation guard'
    Assert-Status (Send-Json 'POST' "/leads/$reserved/assign" (@{ownerId=$bMember;reason='r'}|ConvertTo-Json -Compress) (Claim-Headers $authorization 'o4-reserved-assign')) 409 'Assign reservation guard'
    $profile=Send-Json 'PUT' "/leads/$reserved" (@{displayName='Reserved';email='claim@example.test';ownerId=$bMember}|ConvertTo-Json -Compress) (Claim-Headers $authorization 'o4-reserved-profile')
    Check-O4 ($profile.Status -in @(403,409)) 'Profile owner cannot bypass reservation'
    Assert-Status (Send-Json 'PUT' "/leads/$reserved" (@{displayName='Reserved';email='claim@example.test';ownerId=$memberId}|ConvertTo-Json -Compress) (Claim-Headers $authorization 'o4-reserved-profile-same-owner')) 409 'Reserved profile mutation with unchanged owner still blocked'
    Assert-Status (Send-Json 'GET' "/leads/$reserved" $null $authorization) 200 'Reservation permits normal Lead reads'
    Invoke-Sql "UPDATE leads.Leads SET PendingHandoverId='handover_o4_null_reserved' WHERE LeadId='$nullLead';"
    Assert-Status (Send-Json 'POST' "/workflows/lead-queue/$nullLead/claim" '{}' (Claim-Headers $authorization 'o4-reserved-claim')) 409 'Claim respects active Handover reservation'
    Check-O4 ((Lead-Version $reserved) -eq 0 -and (Invoke-SqlScalar "SELECT ScopeOwnerId FROM leads.Leads WHERE LeadId='$reserved';") -eq $memberId) 'Reservation denial preserves Lead'
    foreach($kind in @('assign','claim','handover')) {
        for($iteration=0;$iteration -lt 3;$iteration++) {
            $id=Owned-Lead "O4 Race $kind $iteration"
            $left=New-O4Message "/workflows/lead-handover/$id" (Handover-Body $bMember) (Claim-Headers $authorization "o4-race-$kind-$iteration-h")
            $right=if($kind -eq 'assign'){New-O4Message "/leads/$id/assign" (@{ownerId=$cMember;reason='race'}|ConvertTo-Json -Compress) (Claim-Headers $bAuth "o4-race-$kind-$iteration-r")}elseif($kind -eq 'claim'){New-ClaimMessage $id (Claim-Headers $bAuth "o4-race-$kind-$iteration-r")}else{New-O4Message "/workflows/lead-handover/$id" (Handover-Body $cMember) (Claim-Headers $bAuth "o4-race-$kind-$iteration-r")}
            $statuses=Race-O4 $left $right
            Check-O4 (@($statuses|Where-Object {$_ -eq 200}).Count -eq 1 -and @($statuses|Where-Object {$_ -in @(409,412)}).Count -eq 1) "Real $kind race single winner"
            if($kind -eq 'claim'){Check-O4 ($statuses[0] -eq 200 -and $statuses[1] -eq 409) 'Claim on owned Lead cannot win ownership race'}
            $owner=if($statuses[0] -eq 200){$bMember}else{$cMember}
            Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$id' AND ScopeOwnerId='$owner' AND JSON_VALUE(Profile,'$.ownerId')='$owner' AND PendingHandoverId IS NULL;") -eq '1') 'Race owner fields agree and reservation cleared'
            $completed=[int](Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.LeadHandoverAnchors WHERE LeadId='$id' AND CompletedAt IS NOT NULL;")
            Check-O4 ($completed -eq $(if($kind -eq 'assign' -and $statuses[1] -eq 200){0}else{1})) 'Race durable handover count'
            Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE RecordId='$id' AND SourceType='LEAD_HANDOVER';") -eq "$completed") 'Race no duplicate takeover'
            Check-O4 ((Lead-Version $id) -eq $(if($completed -eq 1){2}else{1})) 'Race version reflects only admitted ownership operation'
            Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$id' AND Operation IN ('assignLeadOwner','handoverLead:complete','claimLeadFromQueue');") -eq '1') 'Race single ownership completion audit'
            Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId='$id' AND EventType IN ('LEAD_OWNER_ASSIGNED','LEAD_HANDOVER_COMPLETED','LEAD_CLAIMED_FROM_QUEUE');") -eq '1') 'Race single ownership completion event'
            $races.Add(@{Kind=$kind;LeadId=$id;Statuses=$statuses;Owner=$owner;Version=(Lead-Version $id);CompletedHandovers=$completed})
        }
    }
    # Prepare a real fixture before replacing the normal host with the test-only fault composition.
    $fenceLead=Owned-Lead 'O4 Reservation Fence';$proofLead=Owned-Lead 'O4 Reservation Proof'
    $recoveryLead=Owned-Lead 'O4 Recovery';Task-Fixtures $recoveryLead
    Stop-ApiHost $hostProcess;$hostProcess=$null
    $verifierDll=(Resolve-Path "$PSScriptRoot/LeadHandoverRealVerifier/bin/Debug/net10.0/UnicoreCRM.LeadHandover.RealVerifier.dll").Path
    $env:LeadHandoverVerifier__ControlKey=[Guid]::NewGuid().ToString('N')
    $env:LeadHandoverVerifier__InjectFault='true'
    $control=@{'X-Verifier-Control'=$env:LeadHandoverVerifier__ControlKey}
    $hostProcess=Start-Process dotnet -ArgumentList @($verifierDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $temporaryDirectory 'recovery.out.log') -RedirectStandardError (Join-Path $temporaryDirectory 'recovery.err.log') -PassThru
    $ready=$false
    for($i=0;$i -lt 80;$i++) {
        if($hostProcess.HasExited){throw "Fault host exited: $(Get-Content (Join-Path $temporaryDirectory 'recovery.out.log') -Raw) $(Get-Content (Join-Path $temporaryDirectory 'recovery.err.log') -Raw)"}
        try {if((Send-Json 'GET' '/auth/session' $null @{}).Status -eq 401){$ready=$true;break}}catch{}
        Start-Sleep -Milliseconds 250
    }
    Check-O4 $ready 'Real fault host listening'
    $probeHeaders=$authorization.Clone();$probeHeaders['X-Verifier-Control']=$env:LeadHandoverVerifier__ControlKey
    $fenceBody=@{leadId=$fenceLead;handoverId='handover_o4_cancelled';nextOwnerId=$bMember;operation='resolve'}
    $fenced=Send-Json 'POST' '/__verifier/lead-reservation' ($fenceBody|ConvertTo-Json -Compress) $probeHeaders
    Assert-Status $fenced 409 'Service creates durable reservation cancellation fence'
    Check-O4 (($fenced.Body|ConvertFrom-Json).errorCode -eq 'HANDOVER_RESERVATION_FENCED') 'Cancellation fence has canonical code'
    $fenceBody.operation='reserve'
    $late=Send-Json 'POST' '/__verifier/lead-reservation' ($fenceBody|ConvertTo-Json -Compress) $probeHeaders
    Assert-Status $late 409 'Late real human Reserve rejected after service fence'
    Check-O4 (($late.Body|ConvertFrom-Json).errorCode -eq 'HANDOVER_RESERVATION_FENCED') 'Late Reserve observes persisted cancellation proof'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$fenceLead' AND ScopeOwnerId='$memberId' AND PendingHandoverId IS NULL AND Version=0;") -eq '1') 'Fence and late Reserve never mutate Lead version or reservation'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$fenceLead' AND Operation='handoverLead:reserve:fenced';") -eq '1') 'Exactly one durable reservation fence audit'
    $proofBody=@{leadId=$proofLead;handoverId='handover_o4_existing_reservation';nextOwnerId=$bMember;operation='reserve'}
    $reserve=Send-Json 'POST' '/__verifier/lead-reservation' ($proofBody|ConvertTo-Json -Compress) $probeHeaders
    Assert-Status $reserve 200 'Real human Reserve commits before service resolution'
    $proofBody.operation='resolve'
    $resolved=Send-Json 'POST' '/__verifier/lead-reservation' ($proofBody|ConvertTo-Json -Compress) $probeHeaders
    Assert-Status $resolved 200 'Service Resolve returns existing committed reservation proof'
    $reserveDoc=($reserve.Body|ConvertFrom-Json).response;$resolvedDoc=($resolved.Body|ConvertFrom-Json).response
    Check-O4 ($resolvedDoc.outcome -eq 'REPLAYED' -and $resolvedDoc.commandId -eq $reserveDoc.commandId -and $resolvedDoc.version -eq $reserveDoc.version -and (@($resolvedDoc.auditEvidenceIds) -join ',') -eq (@($reserveDoc.auditEvidenceIds) -join ',')) 'Resolved reservation reuses exact committed command version audit proof'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$proofLead' AND ScopeOwnerId='$memberId' AND PendingHandoverId='handover_o4_existing_reservation' AND Version=1;") -eq '1') 'Service resolution preserves existing exact reservation'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$proofLead' AND Operation='handoverLead:reserve';") -eq '1') 'Service resolution never duplicates Reserve commit'
    $proofBody.operation='release'
    Assert-Status (Send-Json 'POST' '/__verifier/lead-reservation' ($proofBody|ConvertTo-Json -Compress) $probeHeaders) 200 'Release completed verifier reservation under real service authority'
    $failed=Handover $recoveryLead $bMember 'o4-recovery' 0
    Assert-Status $failed 500 'Failure after Tasks commit never returns false success'
    $anchor=Invoke-SqlScalar "SELECT HandoverId FROM workflow.LeadHandoverAnchors WHERE LeadId='$recoveryLead';"
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$recoveryLead' AND ScopeOwnerId='$memberId' AND PendingHandoverId='$anchor';") -eq '1') 'Failed request retains recoverable exact Lead reservation'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId IN ('o4_${recoveryLead}_open_a','o4_${recoveryLead}_open_b') AND AssigneeId='$bMember';") -eq '2') 'Tasks committed before failure'
    $committedHash=Hash-Tasks "RecordId='$recoveryLead'"
    $frozenDue=Invoke-SqlScalar "SELECT CONVERT(varchar(40),TakeoverDueAt,127) FROM workflow.LeadHandoverAnchors WHERE HandoverId='$anchor';"
    # Stop the process, then restart with the original human's Task grants revoked. Only SQL state carries over.
    Stop-ApiHost $hostProcess;$hostProcess=$null
    $env:LeadHandoverVerifier__InjectFault='false'
    Invoke-Sql "SET QUOTED_IDENTIFIER ON; UPDATE workflow.LeadHandoverAnchors SET ExecutionLeaseExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()),NextRetryAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE HandoverId='$anchor'; UPDATE workspace.StudioConfigurations SET BlueprintJson=JSON_MODIFY(BlueprintJson,'$.workflow.handoverAcceptanceSlaHours',48) WHERE WorkspaceId='$workspaceId'; DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability IN ('tasks.create','tasks.assign');"
    # Restart the verifier with faults disabled; manual recovery keeps denial evidence inspectable.
    $hostProcess=Start-Process dotnet -ArgumentList @($verifierDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $temporaryDirectory 'restarted.out.log') -RedirectStandardError (Join-Path $temporaryDirectory 'restarted.err.log') -PassThru
    $ready=$false
    for($i=0;$i -lt 80;$i++){try{if((Send-Json 'GET' '/auth/session' $null @{}).Status -eq 401){$ready=$true;break}}catch{};if($hostProcess.HasExited){throw 'Restarted verifier exited'};Start-Sleep -Milliseconds 250}
    Check-O4 $ready 'Restarted recovery host listening'
    Invoke-Sql "DELETE FROM access.WorkspaceServiceCapabilityGrants WHERE WorkspaceId='$workspaceId' AND ServicePrincipalId='svc_lead_handover_recovery' AND Capability='leads.handover.recover';"
    # A human retry still has Lead authority, but lost Task admission after the irreversible commit.
    # With service recovery temporarily denied, it must retain coordination rather than abort/release.
    $humanRetry=Handover $recoveryLead $bMember 'o4-recovery' 0
    Check-O4 ($humanRetry.Status -in @(409,503)) 'Committed retry with revoked Task grants waits for recovery authority'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$recoveryLead' AND ScopeOwnerId='$memberId' AND PendingHandoverId='$anchor';") -eq '1') 'Human retry after Tasks commit never releases reservation'
    Check-O4 ((Invoke-SqlScalar "SELECT COUNT(*) FROM workflow.LeadHandoverAnchors WHERE HandoverId='$anchor' AND CompletedAt IS NULL AND ActiveLeadKey IS NOT NULL AND Stage<>'ManualReview';") -eq '1') 'Human retry keeps committed workflow recoverable'
    Check-O4 ((Hash-Tasks "RecordId='$recoveryLead'") -eq $committedHash) 'Revoked-grant human retry never duplicates or compensates Tasks'
    Invoke-Sql "SET QUOTED_IDENTIFIER ON; UPDATE workflow.LeadHandoverAnchors SET ExecutionLeaseExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()),NextRetryAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE HandoverId='$anchor'; DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.assign';"
    $denied=Send-Json 'POST' '/__verifier/recover' '{}' $control;Assert-Status $denied 200 'Service recovery scan without grant'
    Check-O4 (($denied.Body|ConvertFrom-Json).completed -eq 0) 'Recovery denied without service grant'
    Check-O4 ((Hash-Tasks "RecordId='$recoveryLead'") -eq $committedHash) 'Denied recovery never compensates Tasks'
    Invoke-Sql "INSERT INTO access.WorkspaceServiceCapabilityGrants(WorkspaceId,ServicePrincipalId,Capability,GrantedAt) VALUES ('$workspaceId','svc_lead_handover_recovery','leads.handover.recover',SYSUTCDATETIME());"
    $recovered=Send-Json 'POST' '/__verifier/recover' '{}' $control
    Assert-Status $recovered 200 'Service recovery scan'
    Check-O4 (($recovered.Body|ConvertFrom-Json).completed -eq 1) 'Recovery completes without original human grants'
    Check-O4 ((Hash-Tasks "RecordId='$recoveryLead'") -eq $committedHash) 'Recovery neither duplicates nor compensates Tasks'
    Check-O4 ((Invoke-SqlScalar "SELECT CONVERT(varchar(40),TakeoverDueAt,127) FROM workflow.LeadHandoverAnchors WHERE HandoverId='$anchor';") -eq $frozenDue) 'In-flight SLA frozen across config change and restart'
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','leads.assign');"
    Assert-Status (Handover $recoveryLead $bMember 'o4-recovery' 0) 403 'Completed replay rechecks current Task capability'
    Check-O4 ((Hash-Tasks "RecordId='$recoveryLead'") -eq $committedHash) 'Denied completed replay preserves committed Tasks'
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','tasks.create'),('$roleId','tasks.assign'); INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('o4_completed_task_scope','$workspaceId','$roleId','tasks','OWN','[]');"
    Assert-Status (Handover $recoveryLead $bMember 'o4-recovery' 0) 200 'Completed replay checks resource capability without post-transfer Task scope'
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='o4_completed_task_scope';"
    $final=Handover $recoveryLead $bMember 'o4-recovery' 0;Assert-Status $final 200 'Recovered final replay'
    $finalDoc=$final.Body|ConvertFrom-Json;Assert-Completion $recoveryLead $finalDoc $bMember 12
    Check-O4 ($finalDoc.outcome -eq 'REPLAYED') 'Recovered intent returns stable successful replay'
    [pscustomobject]@{Status='PASS';Database=$DatabaseName;O4Checks=$o4.Count;HttpChecks=$checks.Count;RealRaces=$races;Recovery=@{LeadId=$recoveryLead;HandoverId=$anchor;FailureHttp=$failed.Status;RevokedTaskGrantRetryHttp=$humanRetry.Status;TakeoverCount=1;Recovered=$true;FrozenDueAt=$frozenDue};Limitations=@('Lease expiry accelerated by SQL; race coverage is bounded to three real requests per pairing.','Migration Down guards checked statically; rollback not executed.')}|ConvertTo-Json -Depth 8
} catch {
    [pscustomobject]@{Status='FAIL';Database=$DatabaseName;O4ChecksPassed=$o4.Count;HttpChecksPassed=$checks.Count;CompletedRaces=$races.Count;Failure=$_.Exception.Message;HostLogs=$temporaryDirectory.FullName}|ConvertTo-Json -Depth 5
    throw
} finally {Stop-ApiHost $hostProcess;$client.Dispose()}
