#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Fixture)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-document-b5/api-'+[guid]::NewGuid().ToString('N'))
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
$script:workspace='ws_096302f33ec94b3fb6ab9a41422d5531'
$emails=@('quote.creator@example.test','quote.approver@example.test','quote.approver2@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$securityKey='unicore-quote-document-b5-'+$run.Substring(0,8)
$securityParseTokens=$null;$securityParseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b5-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','deals')").Replace("@('crm')","@('crm','studio','people')").Replace('B2','B5')))
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}

function DocumentPolicy{
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Version" 'b5-adjusted-package-a-v1'
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Enabled" 'true'
}
function DraftQuote([string]$Name,[string]$Amount='1000',[string]$Currency='VND'){
 $body=Draft "B5 synthetic $Name $run" $Amount;$body.currency=$Currency;$body.lineItems[0].unitPrice.currency=$Currency
 $body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $r=Api $emails[0] POST '/quotes' $body "b5-create-$Name-$run";Check $r 201;return $r.body.result.quote
}
function Assessed($Quote,[string]$Name){
 $q=RequestApproval $Quote "b5-$Name-$run"
 if($q.approvalStatus -eq 'PENDING'){$r=Api $emails[1] POST "/quotes/$($q.id)/approve" @{} "b5-approve-$Name-$run" $q.resourceVersion;Check $r 200;$q=$r.body.result.quote}
 return $q
}
function Issue($Quote,[string]$Name,[string]$Actor=$emails[0],[string]$Reason=''){
 $body=@{};if($Reason){$body.reason=$Reason}
 return Api $Actor POST "/quotes/$($Quote.id)/documents/issue" $body "b5-issue-$Name-$run" $Quote.resourceVersion
}
function Download([string]$QuoteId,[string]$DocumentId,[string]$Name,[string]$Actor=$emails[0]){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098/quotes/$QuoteId/documents/$DocumentId/content")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b5-download-$Name-$run";'X-Correlation-Id'="b5-download-$Name-$run"}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{
   $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();$status=[int]$response.StatusCode
   if($status -eq 200){
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));$path=Join-Path $script:stateDirectory "$Name.pdf";[IO.File]::WriteAllBytes($path,$bytes)
    $value=@{status=$status;sha256=$hash;bytes=$bytes.Length;mediaType=[string]$response.Content.Headers.ContentType;cacheControl=[string]$response.Headers.CacheControl;fileName=[string]$response.Content.Headers.ContentDisposition.FileNameStar;path=$path}
   }else{$value=@{status=$status;body=([Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable)}}
   $cases.Add(@{actor=$Actor;method='GET';path="/quotes/$QuoteId/documents/$DocumentId/content";binary=$status -eq 200;response=$value});return $value
  }finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
$fixtureEvidence=Get-Content -LiteralPath $Fixture -Raw|ConvertFrom-Json -AsHashtable
Assert-Demo ($fixtureEvidence.status -ceq 'PASS' -and $fixtureEvidence.workspaceKey.StartsWith('unicore-quote-document-b5-')) 'B5_FIXTURE' 'A successful isolated B5 fixture is required'
$script:workspace=$fixtureEvidence.workspaceId;$script:buyer=$fixtureEvidence.buyerId;$securityKey=$fixtureEvidence.workspaceKey
$allowedAssignments=[Collections.Generic.HashSet[string]]::new()
function RowHash([string]$Id){return [string](Invoke-DemoSql 'SELECT CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT d.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)),2) Hash FROM deals.Deals d WHERE WorkspaceId=@ws AND DealId=@d' @{'@ws'=$script:workspace;'@d'=$Id}).Rows[0].Hash}
function ParallelRequests($Quote,[bool]$SameKey=$false,[bool]$WithEdit=$false){
 $client=[Net.Http.HttpClient]::new();$pending=@()
 try{
  foreach($name in 'one','two'){
   $path="/quotes/$($Quote.id)/documents/issue";$body=@{};$key="b5-race-$name-$run-$($Quote.id)"
   if($SameKey){$key="b5-race-same-$run-$($Quote.id)"}
   if($WithEdit -and $name -eq 'two'){$path="/quotes/$($Quote.id)/update-draft";$body=Draft "B5 race edited $run" '1000';$body.validUntil=$Quote.validUntil}
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$path")
   foreach($h in @{Authorization="Bearer $($script:tokens[$emails[0]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b5-race-$name-$run";'X-Correlation-Id'="b5-race-$name-$run";'Idempotency-Key'=$key;'If-Match'=('"'+$Quote.resourceVersion+'"')}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
   $request.Content=[Net.Http.StringContent]::new(($body|ConvertTo-Json -Depth 20 -Compress),[Text.Encoding]::UTF8,'application/json');$pending+=@{path=$path;request=$request;task=$client.SendAsync($request)}
  }
  return ,@(foreach($entry in $pending){$response=$entry.task.GetAwaiter().GetResult();try{$r=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{method='POST';path=$entry.path;concurrent=$true;response=$r});$r}finally{$response.Dispose()}})
 }finally{foreach($entry in $pending){$entry.request.Dispose()};$client.Dispose()}
}
function AssignViewer($Role,[string]$Name){
 $profile=$Role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$Role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 foreach($a in $Role.body.result.assignments|Where-Object membershipId -ceq $member.membershipId){[void]$allowedAssignments.Add([string]$a.assignmentId)}
 $v=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 $r=Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($Role.body.aggregateId);teamIds=@()} "b5-viewer-$Name-$run" $v;Check $r 200
}
try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 SetPolicy 'b5-approval-v1';LifecyclePolicy;DocumentPolicy;StartHost;LoginAll
 $body=Draft "B5 multipage Unicode $run" '12345';$body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $body.lineItems=@(1..24|ForEach-Object{@{productNameSnapshot="SẢN PHẨM SYNTHETIC SỐ $_";descriptionSnapshot=("Mô tả tiếng Việt rất dài: báo giá, phê duyệt, thanh toán, địa chỉ và điều kiện giao dịch. "*9)+"KẾT THÚC DÒNG $_";quantity='2';unitPrice=@{amount='12345';currency='VND'};discountRate='5';taxMode='EXCLUSIVE';taxRate='10';billingCycleSnapshot='monthly'}})
 $body.adjustments=@(@{id="fee_$run";label='Phí vận chuyển synthetic';type='SHIPPING_FEE';calculation='FIXED_AMOUNT';value='1000'},@{id="discount_$run";label='Giảm giá tổng synthetic';type='DISCOUNT';calculation='FIXED_AMOUNT';value='2000'})
 $q=Api $emails[0] POST '/quotes' $body "b5-layout-create-$run";Check $q 201;$quote=Assessed $q.body.result.quote 'layout'
 $issued=Issue $quote 'layout';Check $issued 201;$doc=$issued.body.result.document;$file=Download $quote.id $doc.documentId 'multipage-issued';Check $file 200
 Assert-Demo ($doc.pageCount -gt 1 -and $file.sha256 -ceq $doc.pdfSha256) 'B5_MULTIPAGE' 'Actual Backend issuance must store a complete multi-page PDF'
 @{quote=$issued.body.result.quote;document=$doc;download=$file}|ConvertTo-Json -Depth 40|Set-Content (Join-Path $script:stateDirectory 'multipage-proof.json')
 $profile=Api $emails[0] GET '/access/context';Check $profile 200
 $di=@{name="B5 linked synthetic Deal $run";buyerRef=@{type='CONTACT';id=$script:buyer};amount=@{amount='1000';currency='VND'};opportunityScore='70';ownerId=$profile.body.memberId;expectedCloseDate=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');interestedProductIds=@();lineItems=@();contactId=$script:buyer}
 $dr=Api $emails[0] POST '/deals' $di "b5-linked-deal-$run";Check $dr 201;$deal=$dr.body.result.deal;$dealHash=RowHash $deal.id
 $draft=Draft "B5 linked Quote $run" '1000';$draft.Remove('buyerRef');$draft.Remove('sourcePath');$draft.validUntil=$body.validUntil
 $linked=Api $emails[0] POST "/deals/$($deal.id)/quotes" $draft "b5-linked-create-$run" $deal.resourceVersion;Check $linked 201
 $lq=Assessed $linked.body.result.quote 'linked';$li=Issue $lq 'linked';Check $li 201
 Check (Download $lq.id $li.body.result.document.documentId 'linked-issued') 200
 Assert-Demo ((RowHash $deal.id) -ceq $dealHash) 'B5_DEAL_UNCHANGED' 'Issue and download must not alter the linked Deal, Pipeline, Stage or Won evidence'
 @{dealId=$deal.id;beforeHash=$dealHash;afterHash=(RowHash $deal.id);quoteId=$lq.id;documentId=$li.body.result.document.documentId}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'linked-deal-proof.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 if($before){$after=Snapshot;$changes=@(foreach($table in $before.Keys){foreach($key in $before[$table].Keys){if($before[$table][$key] -cne $after[$table][$key]){@{table=$table;key=$key}}}});@{unchanged=$changes.Count -eq 0;changes=$changes}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'preservation.json');if($changes.Count){$status='FAIL'}}
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$fixtureEvidence.workspaceId;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B5 multipage/precision/linked Deal API/SQL. $script:stateDirectory"
}
