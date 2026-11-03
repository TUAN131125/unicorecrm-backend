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
 $q=$saved.quoteId;$c=$saved.caseId;$identityPath="/quotes/$q/acceptance-cases/$c/identity-authority"
 $r=Api $emails[0] GET $identityPath;Check $r 200;$view=$r.body;$subject=$view.binding.subjectId
 # Revocation and replay reauthorization use official AccessControl writes only.
 $role=Api $emails[1] POST '/access/roles' @{name="B63 own record scope $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.assessAcceptanceIdentityAuthority','quotes.recordAcceptanceSourceCheck','quotes.readAcceptanceProofContent','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='OWN'});fieldSecurity=@()} "b63-fls-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $originalRoles=@((SQLRows 'SELECT RoleId FROM access.MembershipRoleAssignments WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).RoleId)
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b63-fls-assign-$run" $memberVersion) 200
 Check (Api $emails[2] GET $identityPath) 404;Check (Api $emails[2] GET "$identityPath/history") 404
 Check (Api $emails[2] POST "$identityPath/source-checks" @{subjectId=$subject} "b63-no-content-$run" $view.resourceVersion) 404
 $initialCases=Get-Content -LiteralPath (Join-Path (Split-Path $FixtureResult) 'cases.json') -Raw|ConvertFrom-Json -AsHashtable
 $oldCheck=$initialCases|Where-Object {$_.path -like '*/identity-authority/source-checks' -and $_.response.status -eq 200}|Select-Object -First 1
 Check (Api $emails[2] POST "$identityPath/source-checks" $oldCheck.body $oldCheck.key $oldCheck.version) 404
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=$originalRoles;teamIds=@()} "b63-fls-restore-$run" $memberVersion) 200
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
 Write-Host "$status B6.3 OWN record scope and replay $script:stateDirectory"
}
