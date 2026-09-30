param([Parameter(Mandatory=$true)][string] $DatabaseName)
$ErrorActionPreference='Stop'
if ($DatabaseName -notmatch '^UnicoreCRM_O2Claim_[A-Za-z0-9_]+$') { throw 'Use an isolated UnicoreCRM_O2Claim_ database.' }
# Run the frozen O1 real ingress/access regressions first; reuse its host/transport helpers.
. "$PSScriptRoot/verify-inbound-lead-webhook.ps1" -DatabaseName $DatabaseName
$client=[System.Net.Http.HttpClient]::new()
$client.Timeout=[TimeSpan]::FromSeconds(20)
$hostProcess=$null
function Claim-Headers([hashtable] $auth,[string] $key,[long] $version=0) {
    $h=$auth.Clone();$h['Idempotency-Key']=$key;$h['If-Match']='"'+$version+'"'; return $h
}
function New-QueueLead([string] $name) {
    $payload=@{displayName=$name;email='claim@example.test'}|ConvertTo-Json -Compress
    $delivery='claim-ingress-'+[Guid]::NewGuid().ToString('N')
    Assert-Status (Send-Webhook 'int_inbound_lead_webhook' $delivery $payload ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())) 200 "External fixture $name"
    return Invoke-SqlScalar "SELECT LeadId FROM leads.Leads WHERE WorkspaceId='$workspaceId' AND JSON_VALUE(Profile,'$.displayName')='$name';"
}
function New-ClaimMessage([string] $id,[hashtable] $h) {
    $m=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$baseUrl/workflows/lead-queue/$id/claim")
    $m.Content=[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json')
    foreach($k in $h.Keys){$null=$m.Headers.TryAddWithoutValidation($k,[string]$h[$k])}; return $m
}
try {
    $hostProcess=Start-ApiHost $false $workspaceId $memberId
    Invoke-Sql "UPDATE integration.InboundBindings SET IsEnabled=1,DelegatedMemberId='$memberId' WHERE IntegrationId='int_inbound_lead_webhook';"
    # O1 custom development role must not silently acquire the newly admitted capability.
    if ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.claim';") -ne '0') {throw 'Custom role was auto-granted Claim.'}
    $a=New-QueueLead 'Claim A';$b=New-QueueLead 'Claim B';$race=New-QueueLead 'Claim Race';$archived=New-QueueLead 'Claim Archived'
    $path="/workflows/lead-queue/$a/claim"
    Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-no-cap')) 403 'CLAIM-06 no Claim'
    Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','leads.claim');"
    foreach($cap in @('leads.read','leads.queue.read')) {
        Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$cap';"
        Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization "claim-no-$cap")) 403 "CLAIM missing $cap"
        $q=Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization
        if($cap -eq 'leads.read'){Assert-Status $q 403 'Queue requires normal read'} elseif(($q.Body|ConvertFrom-Json).pageInfo.totalCount -ne 0){throw 'Queue disclosed without queue.read'}
        Invoke-Sql "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','$cap');"
    }
    foreach($scope in @('TEAM','CUSTOM')) {
        Invoke-Sql "INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('scope_claim_verify','$workspaceId','$roleId','leads','$scope','[]');"
        Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization "claim-$scope")) 403 "$scope Claim fail closed"
        Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_claim_verify';"
    }
    foreach($field in @('ownerId','targetOwnerId','memberId','workspaceId','reason')) {
        Assert-Status (Send-Json 'POST' $path ('{"'+$field+'":"member_other"}') (Claim-Headers $authorization "claim-body-$field")) 422 "CLAIM-03 closed body $field"
    }
    $foreign=Invoke-SqlScalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='inbound-webhook-foreign';"
    $foreignH=$authorization.Clone();$foreignH['X-Workspace-Id']=$foreign
    $denied=Send-Json 'POST' $path '{}' (Claim-Headers $foreignH 'claim-foreign')
    if($denied.Status -notin @(403,404)){throw 'Cross-workspace Claim was not denied'}
    Invoke-Sql "UPDATE leads.Leads SET ArchivedAt=SYSUTCDATETIME() WHERE LeadId='$archived';"
    Assert-Status (Send-Json 'POST' "/workflows/lead-queue/$archived/claim" '{}' (Claim-Headers $authorization 'claim-archived')) 409 'CLAIM-05 archived'
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='$membershipId';"
    Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-inactive')) 403 'CLAIM-09 inactive'
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='$membershipId';"
    $stale=Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-stale' 7)
    Assert-Status $stale 412 'CLAIM-11 version';if(($stale.Body|ConvertFrom-Json).code -ne 'VERSION_CONFLICT'){throw 'Wrong version error'}
    foreach($accessValue in @('ReadOnly','Hidden')) {
        Invoke-Sql "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_claim_owner','$workspaceId','$roleId','leads','ownerId','$accessValue');"
        Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization "claim-field-$accessValue")) 403 "Claim owner field $accessValue"
        Invoke-Sql "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='field_claim_owner';"
    }
    # Explicitly granted custom role is eligible in OWN; ordinary O1 null-owned mutations remain denied.
    Invoke-Sql "INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('scope_claim_verify','$workspaceId','$roleId','leads','OWN','[]');"
    $effectiveRequest=@{resourceKey='leads';recordId=$a;requestedCommands=@('lead.claim-from-queue','lead.update');requestedFields=@('ownerId')}|ConvertTo-Json -Compress -Depth 3
    $effective=Send-Json 'POST' '/access/records/evaluate' $effectiveRequest $authorization
    Assert-Status $effective 200 'OWN Queue effective access'
    $effectiveDoc=$effective.Body|ConvertFrom-Json
    if('lead.claim-from-queue' -notin @($effectiveDoc.allowedCommands) -or 'lead.update' -in @($effectiveDoc.allowedCommands) -or $effectiveDoc.canUpdate){throw 'Claim authority broadened ordinary OWN Queue mutation'}
    $beforeCount=[int](($((Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization).Body)|ConvertFrom-Json).pageInfo.totalCount)
    # Controlled fixtures cover every Task status linked to the contested Lead. No Task runtime changes.
    foreach($status in @(0,1,2)) {
        Invoke-Sql "INSERT INTO tasks.Tasks (TaskId,WorkspaceId,Title,Status,Priority,AssigneeId,DueAt,RecordModuleKey,RecordId,RecordLabel,CreatedAt,UpdatedAt,Version) VALUES ('task_claim_status_$status','$workspaceId','Claim unchanged fixture',$status,0,'$memberId',DATEADD(day,1,SYSUTCDATETIME()),'leads','$race','Claim Race',SYSUTCDATETIME(),SYSUTCDATETIME(),0);"
    }
    $taskSnapshotSql="SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM tasks.Tasks ORDER BY TaskId FOR JSON PATH,INCLUDE_NULL_VALUES)),2);"
    $taskHash=Invoke-SqlScalar $taskSnapshotSql
    $activitySnapshotSql="SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM tasks.Activities ORDER BY ActivityId FOR JSON PATH,INCLUDE_NULL_VALUES)),2);"
    $activityHash=Invoke-SqlScalar $activitySnapshotSql
    $success=Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-original')
    Assert-Status $success 200 'CLAIM-01 OWN success';$doc=$success.Body|ConvertFrom-Json
    if($doc.result.ownerId -ne $memberId -or $doc.version -ne 1 -or $doc.result.leadWorkState -ne 'NEW'){throw 'Claim changed wrong owner/version/lifecycle'}
    $replay=Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-original')
    Assert-Status $replay 200 'CLAIM-12 replay';$replayed=$replay.Body|ConvertFrom-Json
    if($replayed.outcome -ne 'REPLAYED' -or $replayed.commandId -ne $doc.commandId -or $replayed.version -ne 1){throw 'Replay evidence changed'}
    Assert-Status (Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-original' 1)) 409 'Changed key fingerprint rejected'
    $newIntent=Send-Json 'POST' $path '{}' (Claim-Headers $authorization 'claim-new-intent' 1)
    Assert-Status $newIntent 409 'CLAIM-04 assigned same actor';if(($newIntent.Body|ConvertFrom-Json).code -ne 'LEAD_QUEUE_CLAIM_CONFLICT'){throw 'Assigned Claim wrong conflict'}
    if((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$a' AND Operation='claimLeadFromQueue';") -ne '1'){throw 'Replay duplicated audit'}
    $afterPage=(Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization).Body|ConvertFrom-Json
    if($afterPage.pageInfo.totalCount -ne ($beforeCount-1) -or $a -in @($afterPage.items.id) -or $b -notin @($afterPage.items.id)){throw 'Queue predicate/count after Claim wrong'}
    # A second distinct authenticated account, not a spoofed actor header.
    $bEmail='claim.actor.b@example.test'
    $reg=Send-Json 'POST' '/auth/accounts' (@{email=$bEmail;password=$password;displayName='Claim Actor B'}|ConvertTo-Json -Compress) @{'X-Request-Id'='req-claim-register-b';'X-Correlation-Id'='corr-claim-register-b';'Idempotency-Key'='claim-register-b'}
    Assert-Status $reg 201 'Register actor B'
    $actorParts=(Invoke-SqlScalar "SELECT AccountId+'|'+MemberId FROM iam.Accounts WHERE Email='$bEmail';").Split('|')
    $bAccount=$actorParts[0];$bMember=$actorParts[1]
    Invoke-Sql "UPDATE iam.Accounts SET Status='Active',EmailVerifiedAt=SYSUTCDATETIME() WHERE AccountId='$bAccount'; INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('wsm_claim_b','$workspaceId','$bAccount','$bMember','Active',SYSUTCDATETIME()); INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt) VALUES ('assignment_claim_b','$workspaceId','wsm_claim_b','$roleId',SYSUTCDATETIME());"
    $bSign=Send-Json 'POST' '/auth/sessions' (@{email=$bEmail;password=$password}|ConvertTo-Json -Compress) @{'X-Request-Id'='req-claim-signin-b';'X-Correlation-Id'='corr-claim-signin-b';'Idempotency-Key'='claim-signin-b'}
    Assert-Status $bSign 200 'Sign in actor B'
    $bAuth=$authorization.Clone();$bAuth['Authorization']='Bearer '+($bSign.Body|ConvertFrom-Json).accessToken
    $mhA=Claim-Headers $authorization 'race-claim-a';$mhB=Claim-Headers $bAuth 'race-claim-b'
    $mA=New-ClaimMessage $race $mhA;$mB=New-ClaimMessage $race $mhB
    $tA=$client.SendAsync($mA);$tB=$client.SendAsync($mB)
    [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($tA,$tB))
    $aStatus=[int]$tA.Result.StatusCode;$bStatus=[int]$tB.Result.StatusCode
    $aBody=$tA.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    $bBody=$tB.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    $mA.Dispose();$mB.Dispose()
    if(@($aStatus,$bStatus|Where-Object {$_ -eq 200}).Count -ne 1 -or @($aStatus,$bStatus|Where-Object {$_ -eq 409}).Count -ne 1){throw "Race $aStatus/$bStatus failed"}
    $loser=if($aStatus -eq 409){$aBody}else{$bBody}
    if($loser.code -ne 'LEAD_QUEUE_CLAIM_CONFLICT'){throw 'Race loser wrong code'}
    $winner=if($aStatus -eq 200){$memberId}else{$bMember}
    $owner=Invoke-SqlScalar "SELECT ScopeOwnerId FROM leads.Leads WHERE LeadId='$race';"
    $version=Invoke-SqlScalar "SELECT Version FROM leads.Leads WHERE LeadId='$race';"
    $audit=Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$race' AND Operation='claimLeadFromQueue' AND ActorId='$winner' AND PriorVersion=0 AND NewVersion=1;"
    if($owner -ne $winner -or $version -ne '1' -or $audit -ne '1'){throw 'Race durable evidence failed'}
    if((Invoke-SqlScalar $taskSnapshotSql) -ne $taskHash){throw 'Claim changed Tasks'}
    $eventCount=Invoke-SqlScalar "SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId='$race' AND EventType='LEAD_CLAIMED_FROM_QUEUE' AND JSON_VALUE(PayloadJson,'$.previousOwnerId') IS NULL AND JSON_VALUE(PayloadJson,'$.newOwnerId')='$winner' AND JSON_VALUE(PayloadJson,'$.actorId')='$winner' AND JSON_VALUE(PayloadJson,'$.idempotencyKey') IS NOT NULL;"
    if($eventCount -ne '1'){throw 'Claim ownership evidence incomplete or duplicated'}
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_claim_verify';"
    Assert-Status (Send-Json 'POST' "/workflows/lead-queue/$b/claim" '{}' (Claim-Headers $authorization 'claim-workspace-success')) 200 'WORKSPACE Claim success'
    $profileHeaders=Claim-Headers $authorization 'claim-profile-bypass' 1
    Assert-Status (Send-Json 'PUT' "/leads/$a" (@{displayName='Cannot reassign';ownerId=$bMember;email='claim@example.test'}|ConvertTo-Json -Compress) $profileHeaders) 403 'Permanent profile cannot assign after Claim'
    if((Invoke-SqlScalar $taskSnapshotSql) -ne $taskHash){throw 'WORKSPACE Claim changed Tasks'}
    if((Invoke-SqlScalar $activitySnapshotSql) -ne $activityHash){throw 'Claim changed Activities'}
    [pscustomobject]@{TaskStatusesVerified=@('OPEN','COMPLETED','CANCELLED');ActivityChanges=0;Status='PASS';Database=$DatabaseName;LeadId=$race;ActorA=$memberId;ActorB=$bMember;ActorAResult=$aStatus;ActorBResult=$bStatus;FinalOwner=$owner;Version=$version;SuccessfulAuditCount=$audit;TaskChanges=0;Checks=$checks}|ConvertTo-Json -Depth 5
} finally {Stop-ApiHost $hostProcess;$client.Dispose()}
