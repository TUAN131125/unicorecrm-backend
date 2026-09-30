param([Parameter(Mandatory=$true)][string] $DatabaseName)
$ErrorActionPreference='Stop'
if ($DatabaseName -notmatch '^UnicoreCRM_O2Claim_O3_[A-Za-z0-9_]+$') {throw 'Use an isolated UnicoreCRM_O2Claim_O3_ database.'}
# Frozen Claim/O1 regressions run unchanged; reuse their real HTTP/SQL host fixtures.
. "$PSScriptRoot/verify-lead-queue-claim.ps1" -DatabaseName $DatabaseName
$client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromSeconds(20)
$hostProcess=$null
$assignChecks=[Collections.Generic.List[string]]::new()
function Assert-Assign([bool] $condition,[string] $name) {if(!$condition){throw $name};$assignChecks.Add($name)}
function Assign-Body([string] $owner,[string] $reason='Territory coverage') {return @{ownerId=$owner;reason=$reason}|ConvertTo-Json -Compress}
function Assign([string] $id,[string] $owner,[string] $key,[long] $version=0,[hashtable] $auth=$authorization,[string] $reason='Territory coverage') {
    return Send-Json 'POST' "/leads/$id/assign" (Assign-Body $owner $reason) (Claim-Headers $auth $key $version)
}
function New-AssignMessage([string] $id,[string] $owner,[hashtable] $headers) {
    $m=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$baseUrl/leads/$id/assign")
    $m.Content=[Net.Http.StringContent]::new((Assign-Body $owner),[Text.Encoding]::UTF8,'application/json')
    foreach($k in $headers.Keys){$null=$m.Headers.TryAddWithoutValidation($k,[string]$headers[$k])};return $m
}
function Race([Net.Http.HttpRequestMessage] $left,[Net.Http.HttpRequestMessage] $right) {
    $l=$client.SendAsync($left);$r=$client.SendAsync($right)
    [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($l,$r))
    $script:raceLeft=$l.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    $script:raceRight=$r.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    $result=@([int]$l.Result.StatusCode,[int]$r.Result.StatusCode)
    $left.Dispose();$right.Dispose();$l.Result.Dispose();$r.Result.Dispose();return $result
}
function Assert-OneCommit([string] $id) {
    Assert-Assign ((Invoke-SqlScalar "SELECT Version FROM leads.Leads WHERE LeadId='$id';") -eq '1') "Single version $id"
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$id' AND Operation IN ('assignLeadOwner','claimLeadFromQueue');") -eq '1') "Single audit $id"
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId='$id' AND EventType IN ('LEAD_OWNER_ASSIGNED','LEAD_CLAIMED_FROM_QUEUE');") -eq '1') "Single event $id"
}
try {
    $hostProcess=Start-ApiHost $false $workspaceId $memberId
    Invoke-Sql "UPDATE integration.InboundBindings SET IsEnabled=1,DelegatedMemberId='$memberId' WHERE IntegrationId='int_inbound_lead_webhook';"
    $cEmail='assign.actor.c@example.test'
    Assert-Status (Send-Json 'POST' '/auth/accounts' (@{email=$cEmail;password=$password;displayName='Assign Target C'}|ConvertTo-Json -Compress) @{'X-Request-Id'='req-assign-register-c';'X-Correlation-Id'='corr-assign-register-c';'Idempotency-Key'='assign-register-c'}) 201 'Register target C'
    $cParts=(Invoke-SqlScalar "SELECT AccountId+'|'+MemberId FROM iam.Accounts WHERE Email='$cEmail';").Split('|');$cAccount=$cParts[0];$cMember=$cParts[1]
    Invoke-Sql "UPDATE iam.Accounts SET Status='Active',EmailVerifiedAt=SYSUTCDATETIME() WHERE AccountId='$cAccount'; INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt) VALUES ('wsm_assign_c','$workspaceId','$cAccount','$cMember','Active',SYSUTCDATETIME());"
    # A real active foreign membership must not be an assignable target.
    Invoke-Sql "UPDATE workspace.Memberships SET WorkspaceId='$foreignWorkspaceId' WHERE MembershipId='wsm_assign_c';"
    $nullLead=New-QueueLead 'Assign Null';$assigned=New-QueueLead 'Assign Existing';$assignRace=New-QueueLead 'Assign Race';$mixedRace=New-QueueLead 'Claim Assign Race';$denied=New-QueueLead 'Assign Denied'
    Invoke-Sql "UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile,'$.ownerId','$memberId'),ScopeOwnerId='$memberId' WHERE LeadId IN ('$assigned','$assignRace');"
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.assign';") -eq '0') 'Custom role no automatic assign grant'
    Assert-Status (Assign $nullLead $bMember 'assign-missing-cap') 403 'Assign missing capability'
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','leads.assign');"
    foreach($cap in @('leads.read','leads.queue.read')) {
        Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='$cap';"
        Assert-Status (Assign $nullLead $bMember "assign-missing-$cap") 404 "Assign hidden without $cap"
        Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','$cap');"
    }
    $effectiveBody=@{resourceKey='leads';recordId=$nullLead;requestedCommands=@('lead.assign-owner','lead.claim-from-queue');requestedFields=@('ownerId')}|ConvertTo-Json -Compress -Depth 3
    $workspaceEffective=Send-Json 'POST' '/access/records/evaluate' $effectiveBody $authorization
    Assert-Status $workspaceEffective 200 'WORKSPACE Assign effective authority'
    Assert-Assign ('lead.assign-owner' -in @(($workspaceEffective.Body|ConvertFrom-Json).allowedCommands)) 'WORKSPACE null Assign command exposed'
    foreach($scope in @('OWN','TEAM','CUSTOM')) {
        Invoke-Sql "INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('scope_assign_verify','$workspaceId','$roleId','leads','$scope','[]');"
        Assert-Status (Assign $nullLead $bMember "assign-null-$scope") 404 "No null assignment bypass $scope"
        if($scope -eq 'OWN') {
            $ownEffective=Send-Json 'POST' '/access/records/evaluate' $effectiveBody $authorization
            Assert-Status $ownEffective 200 'OWN null effective authority'
            $ownDecision=$ownEffective.Body|ConvertFrom-Json
            Assert-Assign ('lead.assign-owner' -notin @($ownDecision.allowedCommands) -and 'lead.claim-from-queue' -in @($ownDecision.allowedCommands)) 'OWN Claim exception does not expose Assign'
            Invoke-Sql "UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile,'$.ownerId','$bMember'),ScopeOwnerId='$bMember' WHERE LeadId='$denied';"
            $hidden=@()
            foreach($id in @('lead_assign_missing',$denied,'lead_claim_missing')) {
                $response=Assign $id $memberId "assign-hidden-$id"
                Assert-Status $response 404 'Assign hidden response'
                $d=$response.Body|ConvertFrom-Json
                $hidden+=($d|Select-Object type,title,status,code,retryable|ConvertTo-Json -Compress)
            }
            Assert-Assign (@($hidden|Select-Object -Unique).Count -eq 1) 'Hidden responses indistinguishable'
        }
        Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_assign_verify';"
    }
    Invoke-Sql "UPDATE leads.Leads SET WorkspaceId='ws_assign_foreign' WHERE LeadId='$denied';"
    Assert-Status (Assign $denied $bMember 'assign-cross-workspace') 404 'Cross workspace hidden'
    foreach($invalid in @('member_missing',$cMember,'bad id')) {Assert-Status (Assign $nullLead $invalid "assign-invalid-$([Guid]::NewGuid().ToString('N'))") 422 'Invalid target'}
    Invoke-Sql "UPDATE workspace.Memberships SET WorkspaceId='$workspaceId' WHERE MembershipId='wsm_assign_c';"
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Inactive' WHERE MembershipId='wsm_claim_b';"
    Assert-Status (Assign $nullLead $bMember 'assign-inactive') 422 'Inactive target'
    Invoke-Sql "UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='wsm_claim_b';"
    foreach($body in @('{}', ('{"ownerId":"'+$bMember+'","reason":"   "}'), ('{"targetOwnerId":"'+$bMember+'","reason":"r"}'), ('{"ownerId":"'+$bMember+'","reason":"r","actorId":"fake"}'))) {
        Assert-Status (Send-Json 'POST' "/leads/$nullLead/assign" $body (Claim-Headers $authorization ('assign-validation-'+[Guid]::NewGuid().ToString('N')))) 422 'Closed assignment body'
    }
    Assert-Status (Assign $nullLead $bMember 'assign-stale' 9) 412 'Stale assignment'
    foreach($id in @($assigned,$assignRace,$mixedRace)) {foreach($status in @(0,1,2)) {
        Invoke-Sql "INSERT INTO tasks.Tasks(TaskId,WorkspaceId,Title,Status,Priority,AssigneeId,DueAt,RecordModuleKey,RecordId,RecordLabel,CreatedAt,UpdatedAt,Version) VALUES ('task_assign_$($id)_$status','$workspaceId','Assign unchanged',$status,0,'$memberId',DATEADD(day,1,SYSUTCDATETIME()),'leads','$id','Assign fixture',SYSUTCDATETIME(),SYSUTCDATETIME(),0);"
    }}
    foreach($id in @($assigned,$assignRace,$mixedRace)) {
        Invoke-Sql "INSERT INTO tasks.Activities(ActivityId,WorkspaceId,Type,Subject,Body,ActorId,OccurredAt,RecordModuleKey,RecordId,RecordLabel,Version) VALUES ('activity_assign_$id','$workspaceId',0,'Unchanged ownership fixture','Historical note','$memberId',SYSUTCDATETIME(),'leads','$id','Assign fixture',0);"
    }
    Assert-Assign ((Invoke-SqlScalar 'SELECT COUNT(*) FROM tasks.Activities;') -eq '3') 'Nonempty Activities fixtures for ownership neutrality'
    $beforeTasks=Invoke-SqlScalar $taskSnapshotSql;$beforeActivities=Invoke-SqlScalar $activitySnapshotSql
    $nullResult=Assign $nullLead $bMember 'assign-null-success'
    Assert-Status $nullResult 200 'Null to B assignment'
    Assert-Assign (($nullResult.Body|ConvertFrom-Json).result.ownerId -eq $bMember) 'Authoritative owner result'
    $page=(Send-Json 'GET' '/leads?assignmentState=UNASSIGNED' $null $authorization).Body|ConvertFrom-Json
    Assert-Assign ($nullLead -notin @($page.items.id)) 'Assigned Lead leaves queue'
    $existingResult=Assign $assigned $bMember 'assign-existing-success'
    Assert-Status $existingResult 200 'A to B assignment'
    $replay=Assign $assigned $bMember 'assign-existing-success'
    Assert-Status $replay 200 'Assign replay'
    Assert-Assign (($replay.Body|ConvertFrom-Json).outcome -eq 'REPLAYED') 'Replay outcome'
    Assert-OneCommit $assigned
    Assert-Status (Assign $assigned $memberId 'assign-existing-success') 409 'Reused key changed owner'
    Assert-Status (Assign $assigned $bMember 'assign-existing-success' 0 $authorization 'Changed reason') 409 'Reused key changed reason'
    Assert-Status (Assign $assigned $bMember 'assign-existing-success' 1) 409 'Reused key changed version'
    $same=Assign $assigned $bMember 'assign-same-owner' 1
    Assert-Status $same 409 'Fresh same owner conflict'
    Assert-Assign (($same.Body|ConvertFrom-Json).code -eq 'LEAD_OWNER_ALREADY_ASSIGNED') 'Same owner typed code'
    Assert-OneCommit $assigned
    Assert-Status (Send-Json 'PUT' "/leads/$assigned" (Assign-Body $memberId) (Claim-Headers $authorization 'assign-profile-bypass' 1)) 422 'Profile body is not assignment command'
    # Real frozen profile path bypass must be rejected for owner changes.
    Assert-Status (Send-Json 'PUT' "/leads/$assigned" (@{displayName='Assign Existing';email='claim@example.test';ownerId=$memberId}|ConvertTo-Json -Compress) (Claim-Headers $authorization 'assign-profile-owner' 1)) 403 'Profile cannot transfer owner'
    Invoke-Sql "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='leads.assign';"
    Assert-Status (Assign $assigned $bMember 'assign-existing-success') 403 'Replay current capability enforced'
    Invoke-Sql "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES ('$roleId','leads.assign');"
    $own=New-QueueLead 'Assign OWN owned'
    Invoke-Sql "UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile,'$.ownerId','$memberId'),ScopeOwnerId='$memberId' WHERE LeadId='$own'; INSERT INTO access.RoleDataScopes(PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('scope_assign_verify','$workspaceId','$roleId','leads','OWN','[]');"
    Assert-Status (Assign $own $bMember 'assign-own-transfer') 200 'OWN assigned record transfer permitted'
    Assert-Status (Assign $own $bMember 'assign-own-transfer') 404 'Replay current record scope enforced after transfer'
    Assert-OneCommit $own
    Invoke-Sql "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_assign_verify';"
    $arch=New-QueueLead 'Assign Archived';$conversion=New-QueueLead 'Assign Reserved';$closed=New-QueueLead 'Assign Closed'
    Invoke-Sql "UPDATE leads.Leads SET ArchivedAt=SYSUTCDATETIME() WHERE LeadId='$arch'; UPDATE leads.Leads SET PendingCustomerConversionId='conversion_reserved' WHERE LeadId='$conversion'; UPDATE leads.Leads SET WorkState=3 WHERE LeadId='$closed';"
    Assert-Status (Assign $arch $bMember 'assign-archived') 409 'Archived assignment rejected'
    Assert-Status (Assign $conversion $bMember 'assign-reserved') 409 'Reserved conversion rejected'
    Assert-Status (Assign $closed $bMember 'assign-closed') 200 'Closed does not automatically forbid assignment'
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId='$assigned' AND Operation='assignLeadOwner' AND JSON_VALUE(EvidenceJson,'$.previousOwnerId')='$memberId' AND JSON_VALUE(EvidenceJson,'$.newOwnerId')='$bMember' AND JSON_VALUE(EvidenceJson,'$.reason')='Territory coverage' AND JSON_VALUE(EvidenceJson,'$.actorId')='$memberId' AND JSON_VALUE(EvidenceJson,'$.workspaceId')='$workspaceId' AND JSON_VALUE(EvidenceJson,'$.idempotencyKey')='assign-existing-success' AND JSON_VALUE(EvidenceJson,'$.priorVersion')='0' AND JSON_VALUE(EvidenceJson,'$.newVersion')='1' AND JSON_VALUE(EvidenceJson,'$.requestId') IS NOT NULL AND JSON_VALUE(EvidenceJson,'$.correlationId') IS NOT NULL AND JSON_VALUE(EvidenceJson,'$.occurredAt') IS NOT NULL;") -eq '1') 'Durable immutable ownership audit evidence'
    $aa=Race (New-AssignMessage $assignRace $bMember (Claim-Headers $authorization 'race-assign-a')) (New-AssignMessage $assignRace $cMember (Claim-Headers $bAuth 'race-assign-b'))
    Assert-Assign (($aa|Sort-Object) -join ',' -eq '200,412') 'Assign vs Assign exactly one winner'
    Assert-Assign ((Invoke-SqlScalar "SELECT ScopeOwnerId FROM leads.Leads WHERE LeadId='$assignRace';") -eq $(if($aa[0] -eq 200){$bMember}else{$cMember})) 'Assign race authoritative winner'
    $aaLoser=if($aa[0] -eq 412){$raceLeft}else{$raceRight}
    Assert-Assign ($aaLoser.code -eq 'VERSION_CONFLICT') 'Assign race loser canonical VERSION_CONFLICT'
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$assignRace' AND ScopeOwnerId=JSON_VALUE(Profile,'$.ownerId');") -eq '1') 'Assign race both authoritative owner fields agree'
    Assert-OneCommit $assignRace
    $mixed=Race (New-ClaimMessage $mixedRace (Claim-Headers $authorization 'race-mixed-claim')) (New-AssignMessage $mixedRace $bMember (Claim-Headers $bAuth 'race-mixed-assign'))
    Assert-Assign ((($mixed[0] -eq 200) -and ($mixed[1] -eq 412)) -or (($mixed[0] -eq 409) -and ($mixed[1] -eq 200))) 'Claim vs Assign exactly one winner'
    $mixedOwner=if($mixed[0] -eq 200){$memberId}else{$bMember}
    $mixedLoser=if($mixed[0] -eq 200){$raceRight}else{$raceLeft}
    $mixedCode=if($mixed[0] -eq 200){'VERSION_CONFLICT'}else{'LEAD_QUEUE_CLAIM_CONFLICT'}
    Assert-Assign ($mixedLoser.code -eq $mixedCode) 'Mixed race loser canonical typed code'
    Assert-Assign ((Invoke-SqlScalar "SELECT COUNT(*) FROM leads.Leads WHERE LeadId='$mixedRace' AND ScopeOwnerId='$mixedOwner' AND JSON_VALUE(Profile,'$.ownerId')='$mixedOwner';") -eq '1') 'Mixed race owner matches successful actor/target'
    Assert-OneCommit $mixedRace
    Assert-Assign ((Invoke-SqlScalar $taskSnapshotSql) -eq $beforeTasks) 'All OPEN COMPLETED CANCELLED Tasks byte unchanged'
    Assert-Assign ((Invoke-SqlScalar $activitySnapshotSql) -eq $beforeActivities) 'Activities byte unchanged'
    [pscustomobject]@{Status='PASS';AssignChecks=$assignChecks.Count;HttpChecks=$checks.Count;Database=$DatabaseName;ActorA=$memberId;ActorB=$bMember;TargetB=$bMember;TargetC=$cMember;
        AssignRace=@{LeadId=$assignRace;InitialOwner=$memberId;InitialVersion=0;Results=$aa;FinalOwner=(Invoke-SqlScalar "SELECT ScopeOwnerId FROM leads.Leads WHERE LeadId='$assignRace';");Version=1;Audit=1;Event=1};
        MixedRace=@{LeadId=$mixedRace;InitialOwner=$null;InitialVersion=0;Results=$mixed;FinalOwner=(Invoke-SqlScalar "SELECT ScopeOwnerId FROM leads.Leads WHERE LeadId='$mixedRace';");Version=1;Audit=1;Event=1};TaskChanges=0;ActivityChanges=0}|ConvertTo-Json -Depth 6
} finally {Stop-ApiHost $hostProcess;$client.Dispose()}
