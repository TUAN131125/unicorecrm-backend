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
 $old=Api $emails[0] GET "/quotes/$($saved.quoteId)";Check $old 200;$contactId=$old.body.buyerRef.id
 $role=Api $emails[1] POST '/access/roles' @{name="B63 CRM affiliation provisioning $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.issueDocument','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','contacts.read','contacts.update','organizations.read','organizations.create');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'},@{resourceKey='contacts';scope='WORKSPACE'},@{resourceKey='organizations';scope='WORKSPACE'});fieldSecurity=@()} "b63-crm-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[0];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $originalRoles=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId)
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b63-crm-assign-$run" $memberVersion) 200
 $org1=Api $emails[0] POST '/organizations' @{displayName="SYNTHETIC Primary Org $run";status='active'} "b63-org1-$run";Check $org1 200
 $org2=Api $emails[0] POST '/organizations' @{displayName="SYNTHETIC Other Org $run";status='active'} "b63-org2-$run";Check $org2 200
 $o1=$org1.body.result.id;$o2=$org2.body.result.id
 $contact=Api $emails[0] GET "/contacts/$contactId";Check $contact 200
 $r1=Api $emails[0] POST "/contacts/$contactId/organization-relationships" @{organizationId=$o1;role='employee';isPrimaryAffiliation=$true} "b63-rel1-$run" $contact.body.version;Check $r1 200
 $contact=Api $emails[0] GET "/contacts/$contactId";Check $contact 200
 $r2=Api $emails[0] POST "/contacts/$contactId/organization-relationships" @{organizationId=$o2;role='buyer';isPrimaryAffiliation=$false} "b63-rel2-$run" $contact.body.version;Check $r2 200
 $contact=Api $emails[0] GET "/contacts/$contactId";Check $contact 200
 $relationship=$r2.body.result.organizationRelationship;$wrongRelationship=$r1.body.result.organizationRelationship
 $body=Draft 'SYNTHETIC explicit secondary affiliation' '1000';$body.buyerRef=@{type='ORGANIZATION_ACCOUNT';id=$o2.ToUpperInvariant()};$body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $draft=Api $emails[0] POST '/quotes' $body "b63-orgquote-$run";Check $draft 201;$quote=Assessed $draft.body.result.quote 'org-affiliation';$issued=Issue $quote 'org-affiliation';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($quote.id)/send" @{deliveryId="b63_org_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic organization only'} "b63-org-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
 $base="/quotes/$($quote.id)/acceptance-cases";$created=Api $emails[0] POST $base @{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint} "b63-org-case-$run" $quote.resourceVersion;Check $created 201;$case=$created.body.result.case
 $bytes=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'));$uploaded=Upload $quote.id $case.caseId $bytes "b63-org-proof-$run" 1;Check $uploaded 201;$proof=$uploaded.body.result.proof;$null=WaitScan "$base/$($case.caseId)/proofs/$($proof.proofId)" 'CLEAN'
 $path="$base/$($case.caseId)/identity-authority";$input=@{contactId=$contactId;identityProofIds=@($proof.proofId);authorityProofIds=@($proof.proofId);reason='SYNTHETIC explicit secondary affiliation'}
 Check (Api $emails[0] PUT "$path/binding" $input "b63-no-affiliation-$run" 0) 403
 $input.relationshipId=$wrongRelationship.relationshipId;Check (Api $emails[0] PUT "$path/binding" $input "b63-wrong-affiliation-$run" 0) 403
 $input.relationshipId=$relationship.relationshipId;$r=Api $emails[0] PUT "$path/binding" $input "b63-secondary-affiliation-$run" 0;Check $r 200
 Assert-Demo (!$r.body.result.binding.party.primaryAffiliation -and $r.body.result.binding.party.relationshipId -ceq $relationship.relationshipId -and !$r.body.result.assessment.readyForIndependentReview) 'B63_EXPLICIT_AFFILIATION' 'Secondary CRM affiliation is captured explicitly, never promoted to legal authority'
 $ended=Api $emails[0] POST "/contacts/$contactId/organization-relationships/$($relationship.relationshipId)/end" @{endedReason='Synthetic affiliation ended'} "b63-end-rel-$run" $contact.body.version;Check $ended 200
 $v=Api $emails[0] GET $path;Check $v 200;Assert-Demo ($v.body.assessment.reasons -contains 'CRM_PARTY_OR_AFFILIATION_INACTIVE' -and $v.body.assessment.reasons -contains 'CRM_PARTY_SNAPSHOT_STALE') 'B63_AFFILIATION_VERSION' 'Owner mutation invalidates captured affiliation facts'
 $status='PASS'
}finally{
 if($originalRoles -and $member){$memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version;Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63-crm-restore-$run" $memberVersion) 200}
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;quoteId=$quote.id;caseId=$case.caseId;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.3 CRM owner affiliation $script:stateDirectory"
}
