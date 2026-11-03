#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-lifecycle-b4/api-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('SetPolicy','Draft','RequestApproval','EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
function OverrideArray([string]$Prefix,[string[]]$Values){
 if(!$Values.Count){throw 'Empty override array is not admitted'}
 $key=$Prefix.Replace('__',':')
 $indices=@($configuration.Keys|Where-Object{$_ -match ('^'+[regex]::Escape($key)+':\d+$')}|ForEach-Object{[int]($_.Split(':')[-1])})
 $indices+=@([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -match ('^'+[regex]::Escape($Prefix)+'__\d+$')}|ForEach-Object{[int](($_ -split '__')[-1])})
 $maximum=[Math]::Max($Values.Count-1, $(if($indices.Count){($indices|Measure-Object -Maximum).Maximum}else{0}))
 for($i=0;$i -le $maximum;$i++){EnvValue ($Prefix+'__'+$i) $Values[$i % $Values.Count]}
}
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$script:workspace=$otherWorkspace
$emails=@('quote.creator@example.test','quote.approver@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$fixture=Get-Content (Join-Path (Split-Path $repo) 'task-runtime/quote-lifecycle-b4/api-32c88969d33c4371878a3633c9ff3edd/result.json') -Raw|ConvertFrom-Json -AsHashtable
$script:workspace=$fixture.workspaceId;$script:buyer=$fixture.buyerId
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
 $prefix="QuoteCustomerDecisionPolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-customer-v1';AllowReportedAcceptance='true';AllowRejection='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}
function SetClock([string]$Utc){
 EnvValue 'QuoteLifecycleVerification__Enabled' 'true';EnvValue 'QuoteLifecycleVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteLifecycleVerification__UtcNow' $Utc
}
function CreateExpiring([string]$Name){
 $body=Draft "B4 expiry $Name $run" '1000';$body.validUntil=$date
 $r=Api $emails[0] POST '/quotes' $body "b4-expiry-create-$Name-$run";Check $r 201
 $q=RequestApproval $r.body.result.quote "b4-expiry-request-$Name-$run"
 $r=Api $emails[0] POST "/quotes/$($q.id)/send" @{deliveryId="b4_expiry_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-expiry-send-$Name-$run" $q.resourceVersion;Check $r 200
 $r=Api $emails[0] POST "/quotes/$($q.id)/customer-decisions" @{decisionId="b4_expiry_report_$Name`_$run";evidenceType='USER_REPORTED_ACCEPTANCE';sourceChannel='EMAIL';sourceDescription='Synthetic B4 employee report; unverified'} "b4-expiry-report-$Name-$run" $r.body.version;Check $r 200
 return $r.body.result.quote
}
$third='quote.approver2@example.test';$emails+=@($third)
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
$h=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret));try{$passwords[$third]='B1-'+[Convert]::ToHexString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$third)))+'!a9'}finally{$h.Dispose();$secret=$null}
$securityKey=$fixture.workspaceKey
$parseTokens=$null;$parseErrors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$parseTokens,[ref]$parseErrors)
$definition=$ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b4-security-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','products','deals')").Replace('B2','B4')))
$allowedB4Assignments=[Collections.Generic.HashSet[string]]::new()
function AssignThird($Role,[string]$Name){
 $profile=$Role.body.result.memberProfiles|Where-Object email -ceq $third;$member=$Role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 foreach($assignment in $Role.body.result.assignments|Where-Object membershipId -ceq $member.membershipId){[void]$allowedB4Assignments.Add([string]$assignment.assignmentId)}
 $row=(Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows
 $v=if($row.Count){[long]$row[0].Version}else{0}
 $r=Api $emails[0] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($Role.body.aggregateId);teamIds=@()} "b4-security-assign-$Name-$run" $v;Check $r 200
 return $profile
}
function FreshDraft([string]$Name,[string]$Amount='1000'){$r=Api $emails[0] POST '/quotes' (Draft "B4 security $Name $run" $Amount) "b4-security-create-$Name-$run";Check $r 201;return $r.body.result.quote}
function Send($q,[string]$Name){$r=Api $emails[0] POST "/quotes/$($q.id)/send" @{deliveryId="b4_security_$Name`_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-security-send-$Name-$run" $q.resourceVersion;Check $r 200;return $r.body.result.quote}
function DealHash([string]$id){return [string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT d.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM deals.Deals d WHERE WorkspaceId=@ws AND DealId=@d' @{'@ws'=$script:workspace;'@d'=$id}).Rows[0].Hash}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 $caps=@('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete','quotes.approve','contacts.read','deals.read','deals.create')
 ProvisionSecurityMember $third $caps
 $otherKey='unicore-quote-lifecycle-b4-isolation-'+$run.Substring(0,8);$securityKey=$otherKey
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.delete')
 $otherWorkspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@k' @{'@k'=$otherKey}).Rows[0].WorkspaceId
 $securityKey=$fixture.workspaceKey;$savedWorkspace=$script:workspace;$script:workspace=$otherWorkspace;LifecyclePolicy;$script:workspace=$savedWorkspace
 SetPolicy 'b4-approval-v1';LifecyclePolicy;StartHost;LoginAll
 $role=Api $emails[0] POST '/access/roles' @{name="B4 minimum lifecycle tester $run";capabilities=$caps;dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'},@{resourceKey='deals';scope='WORKSPACE'},@{resourceKey='contacts';scope='WORKSPACE'});fieldSecurity=@()} "b4-security-role-$run";Check $role 200
 $profile=AssignThird $role 'baseline'
 $high=RequestApproval (FreshDraft 'approved-source' '150000000') 'b4-security-high'
 $approved=Api $emails[1] POST "/quotes/$($high.id)/approve" @{} "b4-security-source-approve-$run" $high.resourceVersion;Check $approved 200
 $source=Send $approved.body.result.quote 'approved-source'
 $revision=Api $third POST "/quotes/$($source.id)/revise" @{} "b4-security-revise-$run" $source.resourceVersion;Check $revision 201;$q=$revision.body.result.quote
 Assert-Demo (!$q.approvalContentFingerprint -and $q.ownerId -ceq $source.ownerId) 'B4_APPROVAL_RESET' 'Revision keeps owner without approval binding'
 $creator=(Invoke-DemoSql 'SELECT ActorId FROM quotes.CommandRecords WHERE WorkspaceId=@ws AND QuoteId=@q AND Operation=''reviseQuoteCommand''' @{'@ws'=$script:workspace;'@q'=$q.id}).Rows[0].ActorId
 Assert-Demo ($creator -ceq $profile.memberId -and $creator -cne $q.ownerId) 'B4_CREATOR_SEPARATION' 'Revision creator must be new actor, not inherited owner'
 $request=Api $third POST "/quotes/$($q.id)/request-approval" @{} "b4-security-request-$run" $q.resourceVersion;Check $request 200
 Check (Api $third POST "/quotes/$($q.id)/approve" @{} "b4-security-self-$run" $request.body.version) 403 'ACCESS_DENIED' 'SELF_APPROVAL_FORBIDDEN'
 Check (Api $emails[1] POST "/quotes/$($q.id)/approve" @{} "b4-security-independent-$run" $request.body.version) 200
 $di=@{name="B4 owned linked Deal $run";buyerRef=@{type='CONTACT';id=$script:buyer};amount=@{amount='1000';currency='VND'};opportunityScore='70';ownerId=$profile.memberId;expectedCloseDate=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');interestedProductIds=@();lineItems=@();contactId=$script:buyer}
 $dr=Api $third POST '/deals' $di "b4-linked-deal-$run";Check $dr 201;$deal=$dr.body.result.deal;$dealHash=DealHash $deal.id
 $body=Draft "B4 linked source $run" '1000';$body.Remove('buyerRef');$body.Remove('sourcePath')
 $lr=Api $third POST "/deals/$($deal.id)/quotes" $body "b4-linked-create-$run" $deal.resourceVersion;Check $lr 201
 $lq=Api $third POST "/quotes/$($lr.body.aggregateId)/request-approval" @{} "b4-linked-request-$run" $lr.body.version;Check $lq 200
 $ls=Api $third POST "/quotes/$($lr.body.aggregateId)/send" @{deliveryId="b4_linked_$run";channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='buyer@example.test'} "b4-linked-send-$run" $lq.body.version;Check $ls 200
 $lrev=Api $third POST "/quotes/$($lr.body.aggregateId)/revise" @{} "b4-linked-revise-$run" $ls.body.version;Check $lrev 201
 Assert-Demo ($lrev.body.result.quote.sourceDealId -ceq $deal.id -and $lrev.body.result.quote.status -ceq 'DRAFT') 'B4_LINKED_REVISION' 'Revision preserves OPEN Deal source'
 Check (Api $third POST "/quotes/$($lrev.body.aggregateId)/archive" @{reason='Synthetic inactive linked Draft'} "b4-linked-archive-$run" $lrev.body.version) 200
 Assert-Demo ((DealHash $deal.id) -ceq $dealHash) 'B4_DEAL_IMMUTABLE' 'Quote operations must not change Deal, Pipeline or Stage'
 $thirdRevision=Api $third POST "/quotes/$($lr.body.aggregateId)/revise" @{} "b4-linked-third-$run" $ls.body.version;Check $thirdRevision 201
 Assert-Demo ($thirdRevision.body.result.quote.quoteRevision -eq 3 -and $thirdRevision.body.result.quote.revisionOfQuoteId -ceq $lr.body.aggregateId) 'B4_ROOT_NUMBERING' 'Archived working version remains in root numbering'
 $ownWorkspace=$script:workspace;$script:workspace=$otherWorkspace
 Check (Api $emails[0] POST "/quotes/$($source.id)/revise" @{} "b4-foreign-revise-$run" $source.resourceVersion) 404 'RESOURCE_NOT_FOUND'
 Check (Api $emails[0] POST "/quotes/$($source.id)/expire" @{} "b4-foreign-expire-$run" $source.resourceVersion) 404 'RESOURCE_NOT_FOUND'
 Check (Api $emails[0] POST "/quotes/$($source.id)/archive" @{reason='Cross-workspace denial'} "b4-foreign-archive-$run" $source.resourceVersion) 404 'RESOURCE_NOT_FOUND';$script:workspace=$ownWorkspace
 $archiveTarget=FreshDraft 'fls-archive'
 $fls=Api $emails[0] POST '/access/roles' @{name="B4 readonly lifecycle fields $run";capabilities=$caps;dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='title';access='READ_ONLY'},@{resourceKey='quotes';fieldKey='expiredAt';access='READ_ONLY'},@{resourceKey='quotes';fieldKey='archivedAt';access='READ_ONLY'})} "b4-fls-role-$run";Check $fls 200
 $null=AssignThird $fls 'readonly'
 Check (Api $third POST "/quotes/$($source.id)/revise" @{} "b4-security-revise-$run" $source.resourceVersion) 403 'ACCESS_DENIED'
 Check (Api $third POST "/quotes/$($source.id)/expire" @{} "b4-fls-expiry-$run" $source.resourceVersion) 403 'ACCESS_DENIED'
 Check (Api $third POST "/quotes/$($archiveTarget.id)/archive" @{reason='Synthetic readonly-field denial'} "b4-fls-archive-$run" $archiveTarget.resourceVersion) 403 'ACCESS_DENIED'
 @{dealId=$deal.id;sourceQuoteId=$lr.body.aggregateId;revision3=$thirdRevision.body.aggregateId;dealHashUnchanged=(DealHash $deal.id) -ceq $dealHash;newCreator=$creator;inheritedOwner=$q.ownerId}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'linked-creator-proof.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});$unexpected=@($changes|Where-Object{if($_.table -ne 'access.MembershipRoleAssignments'){$true}else{$pk=$_.key|ConvertFrom-Json -AsHashtable;!$allowedB4Assignments.Contains([string]$pk.AssignmentId)}});@{unchanged=$unexpected.Count -eq 0;intentionalB4ThirdMembershipAssignmentChanges=$changes.Count-$unexpected.Count;changes=$unexpected}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($unexpected.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B4 security/linked/API/SQL: $($cases.Count) requests. $script:stateDirectory"
}
