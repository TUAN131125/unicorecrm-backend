#requires -Version 7.0
[CmdletBinding()]
param(
 [ValidatePattern('^UnicoreCRM_AcceptanceTest_[A-Za-z0-9_]+$')][string]$DatabaseName=('UnicoreCRM_AcceptanceTest_'+[guid]::NewGuid().ToString('N')),
 [string]$SqlServer='DESKTOP-ICH225R',[int]$Port=5357,[switch]$Browser,
 [string]$EvidenceDirectory='D:/Project_All/UnicoreCRM/task-runtime/customer-acceptance-order-20261030/connected'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot;$root=Split-Path $repo;$hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
[void](New-Item -ItemType Directory -Force $EvidenceDirectory);$EvidenceDirectory=(Resolve-Path $EvidenceDirectory).Path
$script:api="http://127.0.0.1:$Port";$script:token=$null;$script:ws=$null
$script:results=[Collections.Generic.List[object]]::new();$script:http=[Collections.Generic.List[object]]::new()
$script:connection="Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"
$script:hostProcess=$null;$script:vite=$null
$email='sales.acceptance.test@example.test';$password='Test-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
function SQL([string]$Query,[switch]$Master){
 $c=[System.Data.SqlClient.SqlConnection]::new($(if($Master){"Server=$SqlServer;Database=master;Integrated Security=True;TrustServerCertificate=True"}else{$script:connection}))
 $c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;$reader=$cmd.ExecuteReader();$rows=@();while($reader.Read()){$row=@{};for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=$reader.GetValue($i)};$rows+=[pscustomobject]$row};$reader.Dispose();return $rows}finally{$c.Dispose()}
}
function Check([string]$Name,$Expected,$Actual){
 $pass=([string]$Expected -ceq [string]$Actual);$script:results.Add(@{name=$Name;expected=$Expected;actual=$Actual;passed=$pass})
 if(!$pass){throw "$Name expected=$Expected actual=$Actual"}
}
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null,[switch]$Public){
 $headers=@{'X-Request-Id'=('acceptance-test-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'='acceptance-order-verification'}
 if(!$Public){if($script:token){$headers.Authorization='Bearer '+$script:token};if($script:ws){$headers['X-Workspace-Id']=$script:ws}}
 if($Key){$headers['Idempotency-Key']=$Key};if($null -ne $Version){$headers['If-Match']='"'+$Version+'"'}
 $args=@{Method=$Method;Uri=$script:api+$Path;Headers=$headers;SkipHttpErrorCheck=$true;NoProxy=$true;TimeoutSec=60}
 if($null -ne $Body){$args.Body=$Body|ConvertTo-Json -Depth 50 -Compress;$args.ContentType='application/json'}
 $r=Invoke-WebRequest @args;$bodyResult=$null;$raw=if($r.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($r.Content)}else{[string]$r.Content};try{$bodyResult=$raw|ConvertFrom-Json -AsHashtable}catch{}
 if($Path -notlike '/auth/*'){$script:http.Add(@{method=$Method;path=$Path;status=$r.StatusCode;code=$bodyResult.code})}
 return @{status=[int]$r.StatusCode;body=$bodyResult}
}
function Require([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
 $r=Api $Method $Path $Body $Key $Version
 if($r.status -notin 200,201){$r.body|ConvertTo-Json -Depth 10|Set-Content (Join-Path $EvidenceDirectory 'last-error.json');throw "$Method $Path returned $($r.status) $($r.body.code)"};return $r.body
}
function StartHost{
 $script:hostProcess=Start-Process -FilePath (Join-Path $hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory ('host-'+[guid]::NewGuid().ToString('N')+'.log')) -RedirectStandardError (Join-Path $EvidenceDirectory ('host-error-'+[guid]::NewGuid().ToString('N')+'.log'))
 for($i=0;$i -lt 90;$i++){if($script:hostProcess.HasExited){throw 'Isolated ApiHost exited'};try{$r=Api GET '/auth/session' -Public;if($r.status -eq 401){return}}catch{};Start-Sleep -Milliseconds 500};throw 'Isolated ApiHost did not start'
}
function StopHost{if($script:hostProcess -and !$script:hostProcess.HasExited){Stop-Process -Id $script:hostProcess.Id -Force;$script:hostProcess.WaitForExit(10000)|Out-Null}}
function Login{$script:token=$null;$script:token=(Require POST '/auth/sessions' @{email=$email;password=$password} ('login-'+[guid]::NewGuid().ToString('N'))).accessToken}
function Env([string]$Name,[string]$Value){[Environment]::SetEnvironmentVariable($Name,$Value)}
function Policy{
 foreach($entry in @{Version='ordinary-sales-test-v1';Enabled='true';AllowCustomerLink='true';AllowStaffRecorded='true';RequireVerifiedSignerAuthority='false';RequireStaffOperationalApproval='true';OrderDraftLimits__VND='100000000';InvitationHours='48'}.GetEnumerator()){Env ("QuoteSalesAcceptance__Workspaces__$($script:ws)__"+$entry.Key) $entry.Value}
 Env 'QuoteSalesAcceptance__PublicBaseUrl' 'http://127.0.0.1:3057'
 foreach($entry in @{Version='test-approval-v1';LineDiscountPercent='10';TotalDiscountPercent='10';PostpaidDays='30';AdvancedPaymentRequiresApproval='true';AlwaysRequireApproval='false';GrandTotalLimits__VND='100000000'}.GetEnumerator()){Env ("QuoteApprovalPolicies__Workspaces__$($script:ws)__"+$entry.Key) $entry.Value}
 Env "QuoteDocumentPolicies__Workspaces__$($script:ws)__Version" 'test-documents-v1';Env "QuoteDocumentPolicies__Workspaces__$($script:ws)__Enabled" 'true'
 Env "QuoteGmail__Workspaces__$($script:ws)__Enabled" 'false'
}
function DraftQuote([string]$Name){
 $body=@{buyerRef=@{type='CONTACT';id=$script:buyer};sourcePath='DIRECT_SALE';title=$Name;currency='VND';recipientEmail='customer@example.test';validUntil=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');lineItems=@(@{productNameSnapshot='Acceptance test service';productTypeSnapshot='SERVICE';quantity='2';unitPrice=@{amount='125000';currency='VND'};discountRate='10';taxMode='EXCLUSIVE';taxRate='10'});paymentAgreement=@{kind='FULL_PAYMENT';currency='VND';lines=@(@{sequence=1;label='Full payment';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=0;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');fulfillmentGate='NONE'})}}
 $q=(Require POST '/quotes' $body ('create-'+[guid]::NewGuid().ToString('N'))).result.quote
 $q=(Require POST "/quotes/$($q.id)/request-approval" @{} ('approve-assess-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion).result.quote
 $issued=Require POST "/quotes/$($q.id)/documents/issue" @{} ('issue-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion
 return @{quote=$issued.result.quote;document=$issued.result.document}
}
function SentQuote([string]$Name){
 $f=DraftQuote $Name;$q=$f.quote
 $q=(Require POST "/quotes/$($q.id)/send" @{deliveryId=('delivery_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} ('send-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion).result.quote
 return @{quote=$q;document=$f.document}
}
function StaffBody($f){return @{documentId=$f.document.documentId;channel='PHONE';customerAgreedAt=[DateTime]::UtcNow.AddMinutes(-1).ToString('yyyy-MM-ddTHH:mm:ssZ');evidence='Customer agreed by phone; synthetic connected verification.';responsibilityConfirmed=$true}}
function WaitOrder([string]$QuoteId){
 for($i=0;$i -lt 45;$i++){$q=Require GET "/quotes/$QuoteId";if($q.salesAcceptance.orderId){return $q};Start-Sleep -Seconds 1};throw 'Automatic Order creation did not finish'
}
function ConcurrentAcceptance($Fixture){
 $client=[Net.Http.HttpClient]::new();$requests=@();$tasks=@();$body=(StaffBody $Fixture)|ConvertTo-Json -Depth 20 -Compress
 $shared='concurrent-shared-'+[guid]::NewGuid().ToString('N')
 try{
  for($i=0;$i -lt 8;$i++){
   $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$script:api/quotes/$($Fixture.quote.id)/sales-acceptances")
   foreach($h in @{Authorization="Bearer $script:token";'X-Workspace-Id'=$script:ws;'X-Request-Id'="concurrent-acceptance-$i-0001";'Idempotency-Key'=$(if($i -lt 4){$shared}else{"$shared-$i"});'If-Match'=('"'+$Fixture.quote.resourceVersion+'"')}.GetEnumerator()){$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)|Out-Null}
   $request.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json');$requests+=,$request;$tasks+=,$client.SendAsync($request)
  }
  $statuses=@(foreach($task in $tasks){$response=$task.GetAwaiter().GetResult();try{[int]$response.StatusCode}finally{$response.Dispose()}})
  Check 'concurrent accepted/replayed commands return success' True ($statuses -contains 200)
  Check 'competing acceptance intents fail safely' True (@($statuses|Where-Object{$_ -notin 200,409,412}).Count -eq 0)
 }finally{foreach($request in $requests){$request.Dispose()};$client.Dispose()}
}
try{
 [void](SQL "IF DB_ID('$DatabaseName') IS NOT NULL THROW 51000,'A new isolated database is required',1; CREATE DATABASE [$DatabaseName];" -Master)
 Env 'ASPNETCORE_ENVIRONMENT' 'Development';Env 'DOTNET_ENVIRONMENT' 'Development';Env 'ASPNETCORE_URLS' $script:api
 Env 'ConnectionStrings__UnicoreCRM' $script:connection;Env 'UNICORE_DEV_SEED_ENABLED' 'false'
 Env 'IdentityAuth__DevelopmentBootstrap__Enabled' 'true';Env 'IdentityAuth__DevelopmentBootstrap__Email' $email;Env 'IdentityAuth__DevelopmentBootstrap__Password' $password
 Env 'Workspace__DevelopmentBootstrap__Enabled' 'false';Env 'AccessControl__DevelopmentBootstrap__Enabled' 'false';Env 'IdentityAuth__EmailVerification__Sender__Kind' 'DevelopmentLog'
 Env 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3057'
 Push-Location $hostDirectory
 try{& dotnet run --no-build --no-launch-profile -- --migrate *> (Join-Path $EvidenceDirectory 'migrate.log');if($LASTEXITCODE){throw 'Isolated migration failed'};& dotnet run --no-build --no-launch-profile -- --seed-demo *> (Join-Path $EvidenceDirectory 'seed.log');if($LASTEXITCODE){throw 'Isolated identity seed failed'}}finally{Pop-Location}
 StartHost;Login
 $provision=Require POST '/workspaces/initial-provisioning' @{name='Acceptance Order Test'} ('provision-'+[guid]::NewGuid().ToString('N'));$script:ws=$provision.workspaceId
 $workspaceKey=(SQL "SELECT [Key] FROM workspace.Workspaces WHERE WorkspaceId='$($script:ws)'")[0].Key
 # Explicitly configure the synthetic recorder's document permission; production defaults are unchanged.
 [void](SQL "INSERT INTO access.RoleCapabilities(RoleId,Capability) SELECT RoleId,'quotes.issueDocument' FROM access.Roles r WHERE WorkspaceId='$($script:ws)' AND Name='Workspace Owner' AND NOT EXISTS(SELECT 1 FROM access.RoleCapabilities c WHERE c.RoleId=r.RoleId AND c.Capability='quotes.issueDocument')")
 $enabledCurrencies='["VND","USD"]'
 $addresses=ConvertTo-Json -InputObject @(@{id='test_address';name='Registered';purposes=@('REGISTERED');addressLine1='Test Street';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='Test';contactPhone='';active=$true}) -Compress
 [void](SQL "UPDATE workspace.StudioConfigurations SET BusinessInformationJson=JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(BusinessInformationJson,'$.displayName','Acceptance Test Seller'),'$.legalName','Acceptance Test Seller'),'$.email','seller@example.test'), AddressesJson=N'$addresses', LocaleRegionJson=JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(LocaleRegionJson,'$.timezone','UTC'),'$.currencies.enabledCurrencies',JSON_QUERY('$enabledCurrencies')),'$.currencies.baseCurrency','VND'), FeaturesJson=JSON_MODIFY(FeaturesJson,'$.quotes',CAST(1 AS bit)) WHERE WorkspaceId='$($script:ws)'")
 StopHost;Policy;StartHost;Login
 $script:buyer=(Require POST '/contacts' @{fullName='Acceptance test buyer';personalEmail='customer@example.test'} ('contact-'+[guid]::NewGuid().ToString('N'))).result.contact.id
 $staff=SentQuote 'Staff accepted quote';$body=StaffBody $staff;$key='staff-'+[guid]::NewGuid().ToString('N')
 $r=Api POST "/quotes/$($staff.quote.id)/sales-acceptances" $body $key $staff.quote.resourceVersion;Check 'staff acceptance HTTP' 200 $r.status;Check 'staff assurance remains unverified' 'ORDINARY_SALES_UNVERIFIED' $r.body.assuranceLevel
 $replay=Api POST "/quotes/$($staff.quote.id)/sales-acceptances" $body $key $staff.quote.resourceVersion;Check 'same command replay' 200 $replay.status;Check 'same acceptance receipt' $r.body.acceptanceId $replay.body.acceptanceId
 $changed=$body.Clone();$changed.evidence='different';Check 'changed idempotent payload denied' 409 (Api POST "/quotes/$($staff.quote.id)/sales-acceptances" $changed $key $staff.quote.resourceVersion).status
 $q=WaitOrder $staff.quote.id;$order=Require GET "/orders/$($q.salesAcceptance.orderId)"
 Check 'staff accepted lifecycle' 'ACCEPTED' $q.status;Check 'automatic draft state' 'DRAFT' $order.state;Check 'automatic Order source' $q.id $order.sourceQuoteId
 Check 'authoritative total copied' $staff.quote.grandTotal.amount $order.grandTotal.amount;Check 'payment agreement copied' ($staff.quote.paymentAgreement|ConvertTo-Json -Depth 30 -Compress) ($order.paymentAgreementSnapshot|ConvertTo-Json -Depth 30 -Compress)
 Check 'operational approval retained' True $order.acceptance.operationalApprovalRequired
 Check 'source Quote version preserved' $staff.quote.resourceVersion $order.sourceQuoteResourceVersion
 Check 'immutable commercial fingerprint preserved' $q.salesAcceptance.commercialFingerprint $order.commercialSnapshotFingerprint
 $customer=SentQuote 'Customer link quote';$inv=Require POST "/quotes/$($customer.quote.id)/acceptance-invitations" @{documentId=$customer.document.documentId;sendGmail=$false} ('inv-'+[guid]::NewGuid().ToString('N')) $customer.quote.resourceVersion
 $invitationToken=$inv.acceptanceUrl.Split('/')[-1];$before=Require GET "/quotes/$($customer.quote.id)"
 $preview=Api POST '/public/quote-acceptance/preview' @{token=$invitationToken} -Public;Check 'public preview HTTP' 200 $preview.status
 Check 'preview does not accept quote' 'SENT' (Require GET "/quotes/$($customer.quote.id)").status
 Check 'invalid token denied' 410 (Api POST '/public/quote-acceptance/preview' @{token=('A'*64)} -Public).status
 if(!$Browser){$consent=Api POST '/public/quote-acceptance/consent' @{token=$invitationToken;agree=$true;termsVersion=$preview.body.termsVersion} ('consent-'+[guid]::NewGuid().ToString('N')) -Public;Check 'customer consent HTTP' 200 $consent.status}
 # Chromium phase is wired below; it uses the same real host and process-only invitation token.
 $context=@{database=$DatabaseName;workspaceId=$script:ws;workspaceKey=$workspaceKey;staffQuoteId=$staff.quote.id;staffOrderId=$order.id;customerQuoteId=$customer.quote.id;customerInvitationId=$inv.invitationId}
 $context|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'context.json')
 if($Browser){
  $browserStaff=SentQuote 'Chromium staff accepted quote'
  foreach($entry in @{ACCEPTANCE_TEST_API=$script:api;ACCEPTANCE_TEST_WORKSPACE=$workspaceKey;ACCEPTANCE_TEST_TOKEN=$invitationToken;ACCEPTANCE_TEST_EVIDENCE=(Join-Path $EvidenceDirectory 'browser');ACCEPTANCE_TEST_EMAIL=$email;ACCEPTANCE_TEST_PASSWORD=$password;ACCEPTANCE_TEST_QUOTE_NUMBER=$customer.quote.quoteNumber;ACCEPTANCE_TEST_STAFF_QUOTE=$browserStaff.quote.id;ACCEPTANCE_TEST_CUSTOMER_QUOTE=$customer.quote.id;VITE_RUNTIME_MODE='connected';VITE_API_BASE_URL=$script:api}.GetEnumerator()){Env $entry.Key $entry.Value}
  $frontend=Join-Path $root 'frontend/unicorecrm-web'
  $script:vite=Start-Process -FilePath node -WorkingDirectory $frontend -ArgumentList @('node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','3057','--strictPort') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'vite.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'vite-error.log')
  for($i=0;$i -lt 60;$i++){if($script:vite.HasExited){throw 'Isolated Vite exited'};try{if((Invoke-WebRequest 'http://127.0.0.1:3057' -NoProxy).StatusCode -eq 200){break}}catch{};Start-Sleep -Milliseconds 500}
  & node (Join-Path $PSScriptRoot 'verify-sales-acceptance-browser.mjs');Check 'real Chromium customer/staff acceptance to Order' 0 $LASTEXITCODE
 }
 $cq=WaitOrder $customer.quote.id;Check 'customer-link source' 'CUSTOMER_LINK' $cq.salesAcceptance.source
 Check 'customer audit retains actual operation' 1 (SQL "SELECT COUNT(*) C FROM quotes.CommandRecords WHERE WorkspaceId='$($script:ws)' AND QuoteId='$($customer.quote.id)' AND Operation='consentCustomerQuoteAcceptance' AND ActorId LIKE 'invitation-bearer:%'")[0].C
 Check 'staff audit retains session actor' 1 (SQL "SELECT COUNT(*) C FROM quotes.CommandRecords WHERE WorkspaceId='$($script:ws)' AND QuoteId='$($staff.quote.id)' AND Operation='recordQuoteSalesAcceptance' AND ActorId='$($q.salesAcceptance.recordedBy)'")[0].C
 Check 'exactly one Order per source Quote' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND SourceQuoteId='$($staff.quote.id)'")[0].C
 # Security negatives use a live, unchanged Quote and actual session/capability enforcement.
 $negative=SentQuote 'Negative acceptance checks';$nb=StaffBody $negative;$qid=$negative.quote.id;$version=$negative.quote.resourceVersion
 Check 'unauthenticated staff command denied' 401 (Api POST "/quotes/$qid/sales-acceptances" $nb ('unauth-'+[guid]::NewGuid().ToString('N')) $version -Public).status
 Check 'stale Quote version denied' 412 (Api POST "/quotes/$qid/sales-acceptances" $nb ('stale-'+[guid]::NewGuid().ToString('N')) ($version-1)).status
 $tampered=$nb.Clone();$tampered.unitPrice=@{amount='1';currency='VND'}
 Check 'client price override refused' 422 (Api POST "/quotes/$qid/sales-acceptances" $tampered ('tamper-'+[guid]::NewGuid().ToString('N')) $version).status
 $tampered=$nb.Clone();$tampered.acceptedBy='another_person'
 Check 'client actor claim refused' 422 (Api POST "/quotes/$qid/sales-acceptances" $tampered ('actor-'+[guid]::NewGuid().ToString('N')) $version).status
 $role=(SQL "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:ws)' AND Name='Workspace Owner'")[0].RoleId
 [void](SQL "DELETE FROM access.RoleCapabilities WHERE RoleId='$role' AND Capability='orders.create'")
 Check 'missing Order creation permission denied' 403 (Api POST "/quotes/$qid/sales-acceptances" $nb ('permission-'+[guid]::NewGuid().ToString('N')) $version).status
 [void](SQL "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES('$role','orders.create')")
 [void](SQL "INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES('sales_test_hidden_total','$($script:ws)','$role','quotes','grandTotal','Hidden')")
 Check 'hidden commercial field denied' 403 (Api POST "/quotes/$qid/sales-acceptances" $nb ('fls-'+[guid]::NewGuid().ToString('N')) $version).status
 [void](SQL "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='sales_test_hidden_total'")
 $originalWorkspace=$script:ws;$script:ws='ws_foreign_acceptance_test'
 Check 'foreign Workspace denied' 403 (Api POST "/quotes/$qid/sales-acceptances" $nb ('foreign-'+[guid]::NewGuid().ToString('N')) $version).status
 $script:ws=$originalWorkspace
 $ni=Require POST "/quotes/$qid/acceptance-invitations" @{documentId=$negative.document.documentId;sendGmail=$false} ('negative-link-'+[guid]::NewGuid().ToString('N')) $version
 $negativeToken=$ni.acceptanceUrl.Split('/')[-1]
 Check 'explicit consent required' 422 (Api POST '/public/quote-acceptance/consent' @{token=$negativeToken;agree=$false;termsVersion='ordinary-sales-consent-v1'} ('false-consent-'+[guid]::NewGuid().ToString('N')) -Public).status
 [void](SQL "UPDATE quotes.AcceptanceInvitations SET ExpiresAt=DATEADD(hour,-1,SYSUTCDATETIME()) WHERE InvitationId='$($ni.invitationId)'")
 Check 'expired token denied' 410 (Api POST '/public/quote-acceptance/preview' @{token=$negativeToken} -Public).status
 [void](SQL "UPDATE quotes.AcceptanceInvitations SET ExpiresAt=DATEADD(hour,24,SYSUTCDATETIME()) WHERE InvitationId='$($ni.invitationId)'; UPDATE quotes.Quotes SET ArchivedAt=SYSUTCDATETIME() WHERE WorkspaceId='$($script:ws)' AND QuoteId='$qid'")
 Check 'revoked archived Quote link denied' 410 (Api POST '/public/quote-acceptance/preview' @{token=$negativeToken} -Public).status
 [void](SQL "UPDATE quotes.Quotes SET ArchivedAt=NULL,ValidUntil=DATEADD(day,-1,CONVERT(date,SYSUTCDATETIME())) WHERE WorkspaceId='$($script:ws)' AND QuoteId='$qid'")
 Check 'expired Quote staff acceptance denied' 409 (Api POST "/quotes/$qid/sales-acceptances" $nb ('expired-quote-'+[guid]::NewGuid().ToString('N')) $version).status
 Check 'negative probes created no acceptance' 0 (SQL "SELECT COUNT(*) C FROM quotes.SalesAcceptances WHERE WorkspaceId='$($script:ws)' AND QuoteId='$qid'")[0].C
 $gmail=DraftQuote 'Gmail unavailable test'
 $gm=Require POST "/quotes/$($gmail.quote.id)/acceptance-invitations" @{documentId=$gmail.document.documentId;sendGmail=$true} ('gmail-'+[guid]::NewGuid().ToString('N')) $gmail.quote.resourceVersion
 Check 'missing Gmail credentials does not fake send' 'FAILED' $gm.deliveryStatus;Check 'Gmail external not configured' 'GMAIL_NOT_CONFIGURED' $gm.deliveryErrorCode
 Check 'failed Gmail does not mark Quote SENT' 'DRAFT' (Require GET "/quotes/$($gmail.quote.id)").status
 $history=@(Require GET "/quotes/$($gmail.quote.id)/acceptance-invitations");Check 'failed delivery history persists' 'FAILED' $history[0].deliveryStatus
 $race=SentQuote 'Concurrent acceptance and conversion'
 $secondHost=Start-Process -FilePath (Join-Path $hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $hostDirectory -ArgumentList @('--urls',"http://127.0.0.1:$($Port+1)") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'second-worker.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'second-worker-error.log')
 try{
  ConcurrentAcceptance $race;$raceQuote=WaitOrder $race.quote.id
  Check 'concurrent acceptance unique intent' 1 (SQL "SELECT COUNT(*) C FROM quotes.SalesAcceptances WHERE WorkspaceId='$($script:ws)' AND QuoteId='$($race.quote.id)'")[0].C
  Check 'two recovery workers create one Order' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND SourceQuoteId='$($race.quote.id)'")[0].C
 }finally{if(!$secondHost.HasExited){Stop-Process -Id $secondHost.Id -Force;$secondHost.WaitForExit(10000)|Out-Null}}
 # Force a real SQL transaction failure after Order insert; acceptance remains a durable request.
 $rollback=SentQuote 'Order rollback recovery'
 [void](SQL "CREATE TRIGGER orders.AcceptanceTestRollback ON orders.CreationEvidence AFTER INSERT AS BEGIN THROW 51023,'Isolated Order rollback test',1; END")
 $rb=Require POST "/quotes/$($rollback.quote.id)/sales-acceptances" (StaffBody $rollback) ('rollback-'+[guid]::NewGuid().ToString('N')) $rollback.quote.resourceVersion
 Start-Sleep -Seconds 4
 Check 'Order transaction rolls back business row' 0 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND SourceQuoteId='$($rollback.quote.id)'")[0].C
 Check 'Order transaction rolls back evidence' 0 (SQL "SELECT COUNT(*) C FROM orders.CreationEvidence WHERE WorkspaceId='$($script:ws)' AND QuoteId='$($rollback.quote.id)'")[0].C
 Check 'Order transaction rolls back outbox' 0 (SQL "SELECT COUNT(*) C FROM orders.OutboxMessages WHERE WorkspaceId='$($script:ws)' AND JSON_VALUE(PayloadJson,'$.quoteId')='$($rollback.quote.id)'")[0].C
 Check 'acceptance remains durable after Order failure' 'PENDING' (Require GET "/quotes/$($rollback.quote.id)").salesAcceptance.conversionStatus
 [void](SQL 'DROP TRIGGER orders.AcceptanceTestRollback');StopHost;StartHost;Login
 $rq=WaitOrder $rollback.quote.id;Check 'restart retries rolled-back creation' 'COMPLETED' $rq.salesAcceptance.conversionStatus
 # Fail the acknowledgment after Order commit. The same immutable source must replay that Order.
 $ack=SentQuote 'Order acknowledgment recovery'
 [void](SQL "CREATE TRIGGER quotes.AcceptanceTestAcknowledgment ON quotes.SalesAcceptances AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE OrderId IS NOT NULL AND QuoteId='$($ack.quote.id)') THROW 51024,'Isolated acknowledgment failure',1; END")
 [void](Require POST "/quotes/$($ack.quote.id)/sales-acceptances" (StaffBody $ack) ('ack-'+[guid]::NewGuid().ToString('N')) $ack.quote.resourceVersion)
 Start-Sleep -Seconds 4
 Check 'Order committed before lost acknowledgment' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND SourceQuoteId='$($ack.quote.id)'")[0].C
 [void](SQL 'DROP TRIGGER quotes.AcceptanceTestAcknowledgment');StopHost;StartHost;Login
 $aq=WaitOrder $ack.quote.id;Check 'acknowledgment recovered without duplicate Order' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND SourceQuoteId='$($ack.quote.id)'")[0].C
 $authority=SentQuote 'Higher assurance required';StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'true';StartHost;Login
 $denied=Api POST "/quotes/$($authority.quote.id)/sales-acceptances" (StaffBody $authority) ('authority-'+[guid]::NewGuid().ToString('N')) $authority.quote.resourceVersion
 Check 'higher signer assurance still enforced' 'VERIFIED_SIGNER_AUTHORITY_REQUIRED' $denied.body.code
 StopHost;Policy;StartHost;Login
 $beforeRestart=Require GET "/orders/$($order.id)";StopHost;StartHost;Login
 Check 'Order survives actual host restart and relogin' ($beforeRestart|ConvertTo-Json -Depth 50 -Compress) ((Require GET "/orders/$($order.id)")|ConvertTo-Json -Depth 50 -Compress)
 if($Browser){Env 'ACCEPTANCE_TEST_PHASE' 'restart'; & node (Join-Path $PSScriptRoot 'verify-sales-acceptance-browser.mjs');Check 'real Chromium after host restart and relogin' 0 $LASTEXITCODE}
}
finally{
 StopHost;if($script:vite -and !$script:vite.HasExited){Stop-Process -Id $script:vite.Id -Force}
 $script:results|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'assertions.json')
 $script:http|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'http.json')
}
