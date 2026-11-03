#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='read'
. (Join-Path $PSScriptRoot 'scanner/QuoteIdentityVerificationRuntime.ps1')
try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'PARTIAL_FUNCTIONAL_PASS' -and $saved.workspaceKey.StartsWith('unicore-identity-authority-')) 'B62_FIXTURE' 'Only an existing B6.2 synthetic fixture is admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-adjusted-v1'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "QuoteIdentityPolicies__Workspaces__$($script:workspace)__Version" 'b63-approved-v1'
 StartHost;LoginAll
 $q=$saved.quoteId;$c=$saved.caseId;$identityPath="/quotes/$q/acceptance-cases/$c/identity-authority";$sourcesPath="/quotes/$q/acceptance-evidence-sources"
 $current=Api $emails[0] GET $identityPath;Check $current 200;$view=$current.body
 $contactId=$view.binding.party.contactId;$subject=$view.binding.subjectId
 $input=@{contactId=$contactId;expectedSubjectId=$subject;identityProofIds=@($saved.proofId);authorityProofIds=@($saved.proofId);reason='Synthetic rules assessment'}
 foreach($scenario in @('expired','future','amount','currency')){
  $input.authorityClaim=@{effectiveFrom=[DateTime]::UtcNow.AddHours(-1).ToString('yyyy-MM-ddTHH:mm:ssZ');effectiveUntil=[DateTime]::UtcNow.AddHours(1).ToString('yyyy-MM-ddTHH:mm:ssZ');currency='VND';amountLimit='1000';scopeDescription='SYNTHETIC staff claim only'}
  $expected=switch($scenario){'expired'{$input.authorityClaim.effectiveUntil=[DateTime]::UtcNow.AddMinutes(-1).ToString('yyyy-MM-ddTHH:mm:ssZ');'CLAIMED_AUTHORITY_OUTSIDE_VALIDITY'}'future'{$input.authorityClaim.effectiveFrom=[DateTime]::UtcNow.AddMinutes(1).ToString('yyyy-MM-ddTHH:mm:ssZ');'CLAIMED_AUTHORITY_OUTSIDE_VALIDITY'}'amount'{$input.authorityClaim.amountLimit='1';'CLAIMED_AUTHORITY_SCOPE_LIMIT'}'currency'{$input.authorityClaim.currency='USD';'CLAIMED_AUTHORITY_SCOPE_LIMIT'}}
  $r=Api $emails[0] PUT "$identityPath/binding" $input "b63-$scenario-$run" $view.resourceVersion;Check $r 200;$view=$r.body.result
  Assert-Demo ($view.assessment.reasons -contains $expected -and !$view.assessment.readyForIndependentReview) 'B63_CLAIM_RULE' 'Claims are assessed but never made authoritative'
 }
 $invalid=$input.Clone();$invalid.authorityClaim=@{amountLimit='not-money'};Check (Api $emails[0] PUT "$identityPath/binding" $invalid "b63-invalid-money-$run" $view.resourceVersion) 422
 $bad=$input.Clone();$bad.identityProofIds=@('qproof_missing');Check (Api $emails[0] PUT "$identityPath/binding" $bad "b63-wrong-proof-$run" $view.resourceVersion) 404
 $bad=$input.Clone();$bad.contactId='contact_unrelated';Check (Api $emails[0] PUT "$identityPath/binding" $bad "b63-buyer-$run" $view.resourceVersion) 403
 Check (Api $emails[0] GET "/quotes/$q/acceptance-cases/qcase_wrong/identity-authority") 404
 $foreign=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=('unicore-b6-isolation-'+$securityKey.Split('-')[-1])}).Rows[0].WorkspaceId
 Assert-Demo (!!$foreign) 'B63_ISOLATION_FIXTURE' 'Official companion workspace required'
 $own=$script:workspace;$script:workspace=$foreign;Check (Api $emails[0] GET $identityPath) 403;$script:workspace=$own
 $sourceInput=@{name='SYNTHETIC concurrency draft';sourceType='INTERNAL_PROCEDURE';issuerReference='SYNTHETIC';verificationMethod='UNADMITTED';provenanceReference='test-only';requirements='No actual source'}
 $r=Api $emails[1] POST $sourcesPath $sourceInput "b63-concurrent-source-$run" 0;Check $r 200;$source=$r.body.result
 $sourceInput.reason='Concurrent source change'
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($email in $emails[1..2]){
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Put,"http://127.0.0.1:5098$sourcesPath/$($source.sourceId)")
   foreach($h in @{Authorization="Bearer $($script:tokens[$email])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b63-race-$email-$run";'X-Correlation-Id'="b63-race-$email-$run";'Idempotency-Key'="b63-race-$email-$run";'If-Match'='"1"'}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($sourceInput|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{request=$request;task=$client.SendAsync($request)}
  }
  $responses=@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$r=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario='CONCURRENT_SOURCE';response=$r});$r}finally{$response.Dispose()}})
  Assert-Demo ((@($responses.status|Sort-Object)-join ',') -ceq '200,412') 'B63_SOURCE_CONCURRENCY' 'One append and one stale response'
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
 $sourceHistory=Api $emails[1] GET "$sourcesPath/$($source.sourceId)/history";Check $sourceHistory 200;Assert-Demo ($sourceHistory.body.items.Count -eq 2) 'B63_SOURCE_HISTORY' 'No lost update or overwrite'
 # Revocation and replay reauthorization use official AccessControl writes only.
 $role=Api $emails[1] POST '/access/roles' @{name="B63 hidden identity total $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.assessAcceptanceIdentityAuthority','quotes.recordAcceptanceSourceCheck','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "b63-fls-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $originalRoles=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId)
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b63-fls-assign-$run" $memberVersion) 200
 Check (Api $emails[2] GET $identityPath) 403;Check (Api $emails[2] GET "$identityPath/history") 403
 Check (Api $emails[2] POST "$identityPath/source-checks" @{subjectId=$subject} "b63-no-content-$run" $view.resourceVersion) 403
 $initialCases=Get-Content -LiteralPath (Join-Path (Split-Path $FixtureResult) 'cases.json') -Raw|ConvertFrom-Json -AsHashtable
 $oldCheck=$initialCases|Where-Object {$_.path -like '*/identity-authority/source-checks' -and $_.response.status -eq 200}|Select-Object -First 1
 Check (Api $emails[2] POST "$identityPath/source-checks" $oldCheck.body $oldCheck.key $oldCheck.version) 403
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63-fls-restore-$run" $memberVersion) 200
 $sqlBefore=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) Versions,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Commands,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Outbox' @{'@ws'=$script:workspace}
 Stop-DemoHost;EnvValue 'QuoteIdentityVerification__Enabled' 'true';EnvValue 'QuoteIdentityVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteIdentityVerification__FailAfterFlush' 'true';StartHost;LoginAll
 Check (Api $emails[1] POST "$identityPath/assessments" @{subjectId=$subject} "b63-rollback-$run" $view.resourceVersion) 500
 Stop-DemoHost;EnvValue 'QuoteIdentityVerification__Enabled' 'false';StartHost;LoginAll
 $sqlAfter=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.IdentityAuthorityVersions WHERE WorkspaceId=@ws) Versions,(SELECT COUNT(*) FROM quotes.CommandRecords WHERE WorkspaceId=@ws) Commands,(SELECT COUNT(*) FROM quotes.OutboxMessages WHERE WorkspaceId=@ws) Outbox' @{'@ws'=$script:workspace}
 Assert-Demo (($sqlBefore|ConvertTo-Json -Compress) -ceq ($sqlAfter|ConvertTo-Json -Compress)) 'B63_SQL_ROLLBACK' 'No identity version, receipt or outbox after rollback'
 @{before=$sqlBefore;after=$sqlAfter}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'rollback-sql.json')
 $r=Api $emails[1] POST "$identityPath/assessments" @{subjectId=$subject} "b63-rollback-$run" $view.resourceVersion;Check $r 200
 $status='PASS'
}finally{
 if($originalRoles -and $member -and $script:hostProcess -and !$script:hostProcess.HasExited){
  $assigned=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId)
  if((@($assigned|Sort-Object)-join ',') -cne (@($originalRoles|Sort-Object)-join ',')){
   $rv=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
   Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63-finally-restore-$run" $rv) 200
  }
 }
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 security and transaction verification $script:stateDirectory"
}
