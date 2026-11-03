#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^UnicoreCRM_OrderConfirmationTest_[A-Za-z0-9_]+$')][string]$DatabaseName=('UnicoreCRM_OrderConfirmationTest_'+[guid]::NewGuid().ToString('N')),[switch]$Browser,[switch]$BrowserOnly,[int]$Port=5367,[string]$SqlServer='DESKTOP-ICH225R')
$ErrorActionPreference='Stop';if($BrowserOnly){$Browser=$true}
$root=Split-Path (Split-Path $PSScriptRoot);$repo=Join-Path $root 'backend';$hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$EvidenceDirectory=Join-Path $root ('task-runtime/quote-detail-unified/'+$DatabaseName);[void](New-Item -ItemType Directory -Force $EvidenceDirectory)
$script:api="http://127.0.0.1:$Port";$script:connection="Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"
$script:token=$null;$script:ws=$null;$script:hostProcess=$null;$script:vite=$null
$script:results=[Collections.Generic.List[object]]::new();$script:http=[Collections.Generic.List[object]]::new()
$email='o2.confirmer@example.test';$password='O2-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$readerEmail='o2.reader@example.test';$readerPassword='O2-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-sales-acceptance-order.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'O1 helper syntax error'}
foreach($fn in $ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($fn.Name -in @('SQL','Check','Api','Require','StartHost','StopHost','Login','Env','Policy','StaffBody','WaitOrder','DraftQuote','SentQuote','ConcurrentAcceptance')){. ([scriptblock]::Create($fn.Extent.Text))}}
function Seed([string]$Identity,[string]$Password,[string[]]$Capabilities){
 Env 'IdentityAuth__DevelopmentBootstrap__Enabled' 'true';Env 'IdentityAuth__DevelopmentBootstrap__Email' $Identity;Env 'IdentityAuth__DevelopmentBootstrap__Password' $Password
 Env 'Workspace__DevelopmentBootstrap__Enabled' 'true';Env 'Workspace__DevelopmentBootstrap__IdentityEmail' $Identity;Env 'Workspace__DevelopmentBootstrap__EnsureStudioConfiguration' 'true'
 foreach($target in @(@{Section='MemberWorkspace';Key=$workspaceKey},@{Section='NonMemberWorkspace';Key=$foreignKey})){
  $prefix='Workspace__DevelopmentBootstrap__'+$target.Section+'__'
  foreach($e in @{Key=$target.Key;Name='Order Confirmation Test';LogoText='O2';Locale='vi';TimeZone='UTC';BaseCurrency='VND'}.GetEnumerator()){Env ($prefix+$e.Key) $e.Value}
  $i=0;foreach($value in $allCapabilities){Env ($prefix+'Capabilities__'+$i) $value;$i++}
  $i=0;foreach($value in @('contacts','quotes','orders','payments')){Env ($prefix+'EnabledModuleKeys__'+$i) $value;$i++}
  $i=0;foreach($value in @('crm','studio','people')){Env ($prefix+'AvailableProductSpaces__'+$i) $value;$i++}
 }
 Env 'AccessControl__DevelopmentBootstrap__Enabled' 'true';Env 'AccessControl__DevelopmentBootstrap__IdentityEmail' $Identity;Env 'AccessControl__DevelopmentBootstrap__WorkspaceKey' $workspaceKey
 Env 'AccessControl__DevelopmentBootstrap__RoleName' ('Predeclared O2 fixture '+$Identity)
 # Fixture identities and roles are established once before business commands; never elevated after failure.
 $i=0;foreach($value in $Capabilities){Env ('AccessControl__DevelopmentBootstrap__Capabilities__'+$i) $value;$i++}
 while($i -lt ($allCapabilities.Count+1)){Env ('AccessControl__DevelopmentBootstrap__Capabilities__'+$i) $Capabilities[0];$i++}
 $p=Start-Process (Join-Path $hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $hostDirectory -ArgumentList '--seed-demo' -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $EvidenceDirectory ('seed-'+$Identity+'.log')) -RedirectStandardError (Join-Path $EvidenceDirectory ('seed-error-'+$Identity+'.log'))
 if($p.ExitCode){throw 'Official isolated test fixture provisioning failed'}
 foreach($name in @('IdentityAuth','Workspace','AccessControl')){Env ($name+'__DevelopmentBootstrap__Enabled') 'false'}
}
function DraftQuote([string]$Name,[string]$ProductType='SERVICE',[int]$DueDays=0,[switch]$Unissued,[string]$Currency='VND',[string]$Price='125000'){
 $body=@{buyerRef=@{type='CONTACT';id=$script:buyer};sourcePath='DIRECT_SALE';title=$Name;currency=$Currency;recipientEmail='customer@example.test';validUntil=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');lineItems=@(@{productNameSnapshot='Acceptance test service';productTypeSnapshot=$ProductType;quantity='2';unitPrice=@{amount=$Price;currency=$Currency};discountRate='10';taxMode='EXCLUSIVE';taxRate='10'});paymentAgreement=@{kind='FULL_PAYMENT';currency=$Currency;lines=@(@{sequence=1;label='Full payment';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=$DueDays;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');fulfillmentGate='NONE'})}}
 $q=(Require POST '/quotes' $body ('create-'+[guid]::NewGuid().ToString('N'))).result.quote
 if($Unissued){return @{quote=$q;document=$null}}
 $issued=Require POST "/quotes/$($q.id)/documents/issue" @{} ('issue-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion
 return @{quote=$issued.result.quote;document=$issued.result.document}
}
function SentQuote([string]$Name,[string]$ProductType='SERVICE',[int]$DueDays=0){
 $f=DraftQuote $Name $ProductType $DueDays;$q=$f.quote
 $q=(Require POST "/quotes/$($q.id)/send" @{deliveryId=('delivery_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} ('send-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion).result.quote
 return @{quote=$q;document=$f.document}
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
 [void](SQL "IF DB_ID('$DatabaseName') IS NOT NULL THROW 51000,'Only a fresh isolated database is allowed',1;CREATE DATABASE [$DatabaseName];" -Master)
 Env 'ConnectionStrings__UnicoreCRM' $script:connection;Env 'DOTNET_ENVIRONMENT' 'Development';Env 'ASPNETCORE_ENVIRONMENT' 'Development';Env 'ASPNETCORE_URLS' $script:api;Env 'UNICORE_DEV_SEED_ENABLED' 'false';Env 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3057'
 Env 'IdentityAuth__DevelopmentBootstrap__Enabled' 'false';Env 'Workspace__DevelopmentBootstrap__Enabled' 'false';Env 'AccessControl__DevelopmentBootstrap__Enabled' 'false';Env 'IdentityAuth__EmailVerification__Sender__Kind' 'DevelopmentLog'
 Push-Location $hostDirectory;try{& dotnet run --no-build --no-launch-profile -- --migrate *> (Join-Path $EvidenceDirectory 'migrate.log');if($LASTEXITCODE){throw 'Isolated migration failed'}}finally{Pop-Location}
 $workspaceKey='o2-confirmation-'+[guid]::NewGuid().ToString('N').Substring(0,8);$foreignKey=$workspaceKey+'-foreign'
 $policyText=Get-Content (Join-Path $repo 'src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs') -Raw
 $ownerText=$policyText.Substring(0,$policyText.IndexOf('internal static IReadOnlyList<string> CustomRole'))
 $allCapabilities=@([regex]::Matches($ownerText,'"([a-z][a-zA-Z.]+)"')|ForEach-Object{$_.Groups[1].Value}|Where-Object{$_ -match '\.'}|Sort-Object -Unique)
 Seed $email $password (@($allCapabilities)+@('quotes.issueDocument'))
 foreach($key in @([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -like 'AccessControl__DevelopmentBootstrap__Capabilities__*'})){Env $key $null}
 Seed $readerEmail $readerPassword @('workspace.context.resolve','contacts.read','orders.read','payments.plan.read','quotes.read')
 $workspaceAKey=$workspaceKey;$workspaceBKey=$workspaceKey+'-currency';$workspaceKey=$workspaceBKey
 Seed $email $password (@($allCapabilities)+@('quotes.issueDocument'))
 $workspaceKey=$workspaceAKey
 $script:ws=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$workspaceKey'")[0].WorkspaceId
 $foreign=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$foreignKey'")[0].WorkspaceId
 $workspaceBId=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$workspaceBKey'")[0].WorkspaceId
 Policy
 foreach($entry in @{Version='customer-response-test-v1';AllowReportedAcceptance='false';AllowRejection='true'}.GetEnumerator()){Env ("QuoteCustomerDecisionPolicies__Workspaces__$($script:ws)__"+$entry.Key) $entry.Value}
 foreach($e in @{OrdinarySalesWithoutInternalApproval='false';AutoConfirmOrders='true';AutoConfirmLimits__VND='100000000';AutoConfirmLimits__USD='100000';OrderDraftLimits__USD='100000';AutoConfirmProductTypes__0='SERVICE';AutoConfirmProductTypes__1='DIGITAL';RequireStaffOperationalApproval='false'}.GetEnumerator()){Env ("QuoteSalesAcceptance__Workspaces__$($script:ws)__"+$e.Key) $e.Value}
 StartHost;Login
 $config=Require GET '/workspace-configuration';$business=$config.businessInformation;$business.displayName='O2 Test Seller';$business.legalName='O2 Test Seller';$business.email='seller@example.test'
 $addresses=@(@{id='test_registered';name='Registered';purposes=@('REGISTERED');addressLine1='Test Street';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='Test';contactPhone='';active=$true})
 [void](Require PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=$addresses} ('business-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $config=Require GET '/workspace-configuration';$features=$config.features;$features.quotes=$true;$features.orders=$true;$features.payments=$true
 [void](Require PATCH '/workspace-configuration/features' @{features=$features} ('features-'+[guid]::NewGuid().ToString('N')) $config.revision)
 # Real Studio projection is the authority for enabled/default currency.
 $config=Require GET '/workspace-configuration';$locale=$config.localeRegion;$locale.currencies.enabledCurrencies=@('VND','USD');$locale.currencies.baseCurrency='VND'
 [void](Require PATCH '/workspace-configuration/locale-region' @{localeRegion=$locale} ('currency-config-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $workspaceAId=$script:ws;$script:ws=$workspaceBId
 $config=Require GET '/workspace-configuration';$locale=$config.localeRegion;$locale.currencies.enabledCurrencies=@('VND','USD');$locale.currencies.baseCurrency='USD'
 [void](Require PATCH '/workspace-configuration/locale-region' @{localeRegion=$locale} ('workspace-b-currency-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $config=Require GET '/workspace-configuration';$features=$config.features;$features.quotes=$true
 [void](Require PATCH '/workspace-configuration/features' @{features=$features} ('workspace-b-features-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $script:ws=$workspaceAId
 $script:buyer=(Require POST '/contacts' @{fullName='O2 test buyer';personalEmail='customer@example.test'} ('buyer-'+[guid]::NewGuid().ToString('N'))).result.contact.id

 $member=(Require GET '/auth/session').principal.memberId
 $sourceDeal=(Require POST '/deals' @{name='VND source for Studio USD Quote';buyerRef=@{type='CONTACT';id=$script:buyer};amount=@{amount='125000';currency='VND'};opportunityScore='20';ownerId=$member;expectedCloseDate='2027-06-01';interestedProductIds=@();lineItems=@()} ('currency-source-deal-'+[guid]::NewGuid().ToString('N'))).result.deal

 function AssertConfirmed($q){
  $a=WaitOrder $q.id;$o=Require GET "/orders/$($a.salesAcceptance.orderId)";$plans=@(Require GET "/payment-plans?orderId=$($o.id)")
  Check 'Order actually CONFIRMED' 'CONFIRMED' $o.state;Check 'one current Plan' 1 $plans.Count;Check 'Plan actually ACTIVE' 'ACTIVE' $plans[0].state
  Check 'system policy origin' 'WORKSPACE_POLICY' $o.confirmationOrigin
  Check 'unchanged authoritative commercial total' $q.grandTotal.amount $o.grandTotal.amount
  Check 'fingerprint bound' $a.salesAcceptance.commercialFingerprint $o.commercialSnapshotFingerprint
  Check 'no fake internal approval' True ($null -eq $a.approvalStatus)
  Check 'no duplicate Order' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE SourceQuoteId='$($q.id)'")[0].C
  return @{quote=$a;order=$o;plan=$plans[0]}
 }
 function AssertBlocked($f,[string]$Code){
  [void](Require POST "/quotes/$($f.quote.id)/sales-acceptances" (StaffBody $f) ('blocked-'+[guid]::NewGuid().ToString('N')) $f.quote.resourceVersion)
  for($j=0;$j -lt 35;$j++){$q=Require GET "/quotes/$($f.quote.id)";if($q.salesAcceptance.conversionStatus -eq 'BLOCKED'){break};Start-Sleep -Seconds 1}
  Check ('conversion blocked '+$Code) $Code $q.salesAcceptance.lastErrorCode
  Check 'blocked acceptance immutable' 'ACCEPTED' $q.status
  Check 'blocked conversion leaves no durable draft' 0 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE SourceQuoteId='$($f.quote.id)'")[0].C
 }
 $staff=SentQuote 'Automatic confirmed staff quote';$body=StaffBody $staff;$key='auto-staff-'+[guid]::NewGuid().ToString('N')
 Check 'staff acceptance HTTP' 200 (Api POST "/quotes/$($staff.quote.id)/sales-acceptances" $body $key $staff.quote.resourceVersion).status
 Check 'same-key replay' 200 (Api POST "/quotes/$($staff.quote.id)/sales-acceptances" $body $key $staff.quote.resourceVersion).status
 $f=AssertConfirmed $staff.quote
 $customer=SentQuote 'Automatic confirmed customer quote';$inv=Require POST "/quotes/$($customer.quote.id)/acceptance-invitations" @{documentId=$customer.document.documentId;sendGmail=$false} ('invite-'+[guid]::NewGuid().ToString('N')) $customer.quote.resourceVersion
 $invitationToken=$inv.acceptanceUrl.Split('/')[-1];$preview=Api POST '/public/quote-acceptance/preview' @{token=$invitationToken} -Public
 Check 'preview not consent' 'SENT' (Require GET "/quotes/$($customer.quote.id)").status
 Check 'real public consent' 200 (Api POST '/public/quote-acceptance/consent' @{token=$invitationToken;agree=$true;termsVersion=$preview.body.termsVersion} ('consent-'+[guid]::NewGuid().ToString('N')) -Public).status
 $c=AssertConfirmed $customer.quote
 if(!$BrowserOnly){
 $credit=SentQuote 'Real credit authority required' 'SERVICE' 7;AssertBlocked $credit 'CREDIT_APPROVAL_REQUIRED'
 $physical=SentQuote 'Physical fulfillment policy blocked' 'PHYSICAL';AssertBlocked $physical 'AUTO_CONFIRM_PRODUCT_TYPE_BLOCKED'
 $concurrent=SentQuote 'Eight concurrent acceptance requests';ConcurrentAcceptance $concurrent;$cc=AssertConfirmed $concurrent.quote
 $negative=SentQuote 'Security negative quote';$nb=StaffBody $negative;$path="/quotes/$($negative.quote.id)/sales-acceptances";$v=$negative.quote.resourceVersion
 Check '401 anonymous' 401 (Api POST $path $nb 'negative-anonymous-0001' $v -Public).status
 Check 'stale Quote denied' 412 (Api POST $path $nb 'negative-stale-0001' ($v-1)).status
 $tamper=$nb.Clone();$tamper.unitPrice=@{amount='1';currency='VND'};Check 'client price refused' 422 (Api POST $path $tamper 'negative-price-0001' $v).status
 $tamper=$nb.Clone();$tamper.acceptedBy='forged';Check 'actor spoof refused' 422 (Api POST $path $tamper 'negative-actor-0001' $v).status
 $owner=$script:token;$script:token=(Require POST '/auth/sessions' @{email=$readerEmail;password=$readerPassword} 'reader-auto-login-0001').accessToken
 Check '403 no acceptance capability' 403 (Api POST $path $nb 'negative-capability-0001' $v).status;$script:token=$owner
 $saved=$script:ws;$script:ws=$foreign;Check 'cross Workspace denied' 403 (Api POST $path $nb 'negative-workspace-0001' $v).status;$script:ws=$saved
 $role=(SQL "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:ws)' AND Name LIKE 'Predeclared O2 fixture o2.confirmer%'")[0].RoleId
 [void](SQL "INSERT INTO access.RoleFieldSecurity(PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES('auto_confirm_hidden_total','$($script:ws)','$role','quotes','grandTotal','Hidden')")
 Check 'FLS hidden commercial field denies acceptance' 403 (Api POST $path $nb 'auto-fls-denied-0001' $v).status
 [void](SQL "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='auto_confirm_hidden_total'")
 $tamper=$nb.Clone();$tamper.documentId=$staff.document.documentId
 Check 'foreign Quote document binding denied' 409 (Api POST $path $tamper 'auto-document-mismatch-0001' $v).status
 $negativeLink=Require POST "/quotes/$($negative.quote.id)/acceptance-invitations" @{documentId=$negative.document.documentId;sendGmail=$false} 'negative-expiry-link-0001' $v
 $negativeToken=$negativeLink.acceptanceUrl.Split('/')[-1]
 [void](SQL "UPDATE quotes.AcceptanceInvitations SET ExpiresAt=DATEADD(hour,-1,SYSUTCDATETIME()) WHERE InvitationId='$($negativeLink.invitationId)'")
 Check 'expired acceptance invitation denied' 410 (Api POST '/public/quote-acceptance/preview' @{token=$negativeToken} -Public).status
 [void](SQL "UPDATE quotes.Quotes SET ValidUntil=DATEADD(day,-1,CONVERT(date,SYSUTCDATETIME())) WHERE QuoteId='$($negative.quote.id)'")
 Check 'expired Quote acceptance denied' 409 (Api POST $path $nb 'auto-quote-expired-0001' $v).status
 Check 'negative probes created no acceptance' 0 (SQL "SELECT COUNT(*) C FROM quotes.SalesAcceptances WHERE QuoteId='$($negative.quote.id)'")[0].C
 [void](SQL "CREATE TRIGGER payments.AutoActivationFault ON payments.PlanEvents AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE EventType='PAYMENT_PLAN_ACTIVATED') THROW 51031,'Isolated activation failure',1;END")
 $fault=SentQuote 'Rollback and restart recovery';[void](Require POST "/quotes/$($fault.quote.id)/sales-acceptances" (StaffBody $fault) 'auto-fault-acceptance-0001' $fault.quote.resourceVersion)
 Start-Sleep -Seconds 8
 Check 'acceptance survives payment fault' 'ACCEPTED' (Require GET "/quotes/$($fault.quote.id)").status
 Check 'atomic Order rollback' 0 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE SourceQuoteId='$($fault.quote.id)'")[0].C
 Check 'atomic Plan rollback' 0 (SQL "SELECT COUNT(*) C FROM payments.PaymentPlans WHERE OrderId NOT IN(SELECT OrderId FROM orders.Orders)")[0].C
 Check 'no orphan receipt' 0 (SQL "SELECT COUNT(*) C FROM workflow.OrderConfirmationReceipts WHERE OrderId NOT IN(SELECT OrderId FROM orders.Orders)")[0].C
 StopHost;[void](SQL 'DROP TRIGGER payments.AutoActivationFault');StartHost;Login;$recovered=AssertConfirmed $fault.quote
 # Lose only the Quote acknowledgement after the multi-owner commit. Recovery must replay the durable receipt.
 [void](SQL "CREATE TRIGGER quotes.AutoAcknowledgementFault ON quotes.SalesAcceptances AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE OrderId IS NOT NULL) THROW 51032,'Isolated acknowledgement failure',1;END")
 $lost=SentQuote 'Lost acknowledgement recovery';[void](Require POST "/quotes/$($lost.quote.id)/sales-acceptances" (StaffBody $lost) 'auto-lost-acceptance-0001' $lost.quote.resourceVersion)
 for($j=0;$j -lt 30;$j++){$n=(SQL "SELECT COUNT(*) C FROM orders.Orders WHERE SourceQuoteId='$($lost.quote.id)' AND State='CONFIRMED'")[0].C;if($n -eq 1){break};Start-Sleep -Seconds 1}
 Check 'lost acknowledgement still committed once' 1 $n
 Check 'quote ack really lost' True ($null -eq (Require GET "/quotes/$($lost.quote.id)").salesAcceptance.orderId)
 StopHost;[void](SQL 'DROP TRIGGER quotes.AutoAcknowledgementFault');StartHost;Login;$lostRecovered=AssertConfirmed $lost.quote
 # Current higher-assurance policy must refuse ordinary acceptance.
 $special=DraftQuote 'Explicit approval policy' -Unissued
 StopHost;Env "QuoteApprovalPolicies__Workspaces__$($script:ws)__AlwaysRequireApproval" 'true';StartHost;Login
 Check 'D1 direct issue with historical AlwaysRequireApproval enabled' 201 (Api POST "/quotes/$($special.quote.id)/documents/issue" @{} 'special-approval-issue-0001' $special.quote.resourceVersion).status
 Check 'D1 does not synthesize approval state' True ($null -eq (Require GET "/quotes/$($special.quote.id)").approvalStatus)
 StopHost;Env "QuoteApprovalPolicies__Workspaces__$($script:ws)__AlwaysRequireApproval" 'false';StartHost;Login
 $fulfillment=SentQuote 'Missing physical fulfillment' 'PHYSICAL'
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__AutoConfirmProductTypes__2" 'PHYSICAL';StartHost;Login
 AssertBlocked $fulfillment 'FULFILLMENT_INFORMATION_REQUIRED'
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__AutoConfirmProductTypes__2" $null
 Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequirePaymentInstructions" 'true';StartHost;Login
 $instructions=SentQuote 'Required payment instructions';AssertBlocked $instructions 'PAYMENT_ACCOUNT_AUTHORITY_REQUIRED'
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequirePaymentInstructions" 'false';StartHost;Login
 $high=SentQuote 'Higher assurance policy refusal';StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'true';StartHost;Login
 Check 'higher signer guard' 'VERIFIED_SIGNER_AUTHORITY_REQUIRED' (Api POST "/quotes/$($high.quote.id)/sales-acceptances" (StaffBody $high) 'higher-signer-0001' $high.quote.resourceVersion).body.code
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'false';StartHost;Login
 StopHost;StartHost;Login;Check 'restart durable confirmed Order' 'CONFIRMED' (Require GET "/orders/$($f.order.id)").state
 }
 # D1 is global: even high values and the old AlwaysRequireApproval flag cannot restore B1.
 $directHigh=DraftQuote 'Direct issuance above retired approval limit' -Unissued -Price '125000000'
 StopHost;Env "QuoteApprovalPolicies__Workspaces__$($script:ws)__AlwaysRequireApproval" 'true';StartHost;Login
 $directHighIssued=Require POST "/quotes/$($directHigh.quote.id)/documents/issue" @{} ('direct-high-'+[guid]::NewGuid().ToString('N')) $directHigh.quote.resourceVersion
 Check 'high-value direct PDF has no synthetic approval' True ($null -eq $directHighIssued.result.quote.approvalStatus)
 Check 'high-value direct PDF is persisted' True ($directHighIssued.result.document.fileSize -gt 0)
 Check 'acceptance risk guard independent of retired B1' 'ACCEPTANCE_RISK_LIMIT_EXCEEDED' (Api POST "/quotes/$($directHigh.quote.id)/sales-acceptances" (StaffBody @{quote=$directHighIssued.result.quote;document=$directHighIssued.result.document}) 'direct-high-risk-0001' $directHighIssued.result.quote.resourceVersion).body.code
 StopHost;Env "QuoteApprovalPolicies__Workspaces__$($script:ws)__AlwaysRequireApproval" 'false';StartHost;Login
 # USD is explicit in request, persisted document and confirmed Order; no FX/relabeling.
 $usd=DraftQuote 'USD exact commercial snapshot' -Currency 'USD' -Price '123.45'
 $usdSent=(Require POST "/quotes/$($usd.quote.id)/send" @{deliveryId=('delivery_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} ('usd-send-'+[guid]::NewGuid().ToString('N')) $usd.quote.resourceVersion).result.quote
 $usd.quote=$usdSent
 [void](Require POST "/quotes/$($usd.quote.id)/sales-acceptances" (StaffBody $usd) ('usd-accept-'+[guid]::NewGuid().ToString('N')) $usd.quote.resourceVersion)
 $usdConfirmed=AssertConfirmed $usd.quote
 Check 'USD currency reaches Order' 'USD' $usdConfirmed.order.currency
 Check 'USD authoritative decimal total' '244.43' $usdConfirmed.order.grandTotal.amount
 $config=Require GET '/workspace-configuration';$locale=$config.localeRegion;$locale.currencies.baseCurrency='USD'
 [void](Require PATCH '/workspace-configuration/locale-region' @{localeRegion=$locale} ('studio-usd-'+[guid]::NewGuid().ToString('N')) $config.revision)
 Check 'Studio current default changed' 'USD' (Require GET '/workspace-configuration').localeRegion.currencies.baseCurrency
 Check 'historical VND Quote currency immutable' 'VND' (Require GET "/quotes/$($staff.quote.id)").currency
 Check 'historical VND amount immutable' $staff.quote.grandTotal.amount (Require GET "/quotes/$($staff.quote.id)").grandTotal.amount
 Check 'historical Order currency immutable' 'VND' (Require GET "/orders/$($f.order.id)").currency
 @{workspaceKey=$workspaceKey;workspaceId=$script:ws;staffQuoteId=$staff.quote.id;staffOrderId=$f.order.id;customerQuoteId=$customer.quote.id;database=$DatabaseName}|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'context.json')
 if($Browser){
  $legacyUnissued=DraftQuote 'Legacy sent quote needs current PDF' -Unissued
  $legacyUnissued.quote=(Require POST "/quotes/$($legacyUnissued.quote.id)/send" @{deliveryId=('delivery_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} ('legacy-sent-'+[guid]::NewGuid().ToString('N')) $legacyUnissued.quote.resourceVersion).result.quote
  $browserDraft=DraftQuote 'Chromium ordinary quote' -Unissued
  $browserRejection=SentQuote 'Chromium customer rejection quote'
  $browserCustomer=SentQuote 'Chromium customer consent quote'
  $browserInvitation=Require POST "/quotes/$($browserCustomer.quote.id)/acceptance-invitations" @{documentId=$browserCustomer.document.documentId;sendGmail=$false} ('browser-invite-'+[guid]::NewGuid().ToString('N')) $browserCustomer.quote.resourceVersion
  foreach($entry in @{ACCEPTANCE_TEST_REJECTION_QUOTE=$browserRejection.quote.id;ACCEPTANCE_TEST_LEGACY_SENT_QUOTE=$legacyUnissued.quote.id;ACCEPTANCE_TEST_DEAL=$sourceDeal.id;ACCEPTANCE_TEST_USD_QUOTE=$usd.quote.id;ACCEPTANCE_TEST_WORKSPACE_B=$workspaceBKey;ACCEPTANCE_TEST_API=$script:api;ACCEPTANCE_TEST_WORKSPACE=$workspaceKey;ACCEPTANCE_TEST_WORKSPACE_ID=$script:ws;ACCEPTANCE_TEST_TOKEN=$browserInvitation.acceptanceUrl.Split('/')[-1];ACCEPTANCE_TEST_EVIDENCE=(Join-Path $EvidenceDirectory 'browser');ACCEPTANCE_TEST_EMAIL=$email;ACCEPTANCE_TEST_PASSWORD=$password;ACCEPTANCE_TEST_STAFF_QUOTE=$browserDraft.quote.id;ACCEPTANCE_TEST_CUSTOMER_QUOTE=$browserCustomer.quote.id;VITE_RUNTIME_MODE='connected';VITE_API_BASE_URL=$script:api}.GetEnumerator()){Env $entry.Key $entry.Value}
  $frontend=Join-Path $root 'frontend/unicorecrm-web'
  $script:vite=Start-Process -FilePath node -WorkingDirectory $frontend -ArgumentList @('node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','3057','--strictPort') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'vite.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'vite-error.log')
  for($i=0;$i -lt 60;$i++){if($script:vite.HasExited){throw 'Isolated Vite exited'};try{if((Invoke-WebRequest 'http://127.0.0.1:3057' -NoProxy).StatusCode -eq 200){break}}catch{};Start-Sleep -Milliseconds 500}
  & node (Join-Path $PSScriptRoot 'verify-quote-unified-workflow-browser.mjs');Check 'connected Chromium automatic confirmation' 0 $LASTEXITCODE
  [void](AssertConfirmed $browserDraft.quote);[void](AssertConfirmed $browserCustomer.quote)
 }
 StopHost;StartHost;Login
 foreach($fixture in @($staff,$usd)){
  $pdf=Invoke-WebRequest -Uri "$script:api/quotes/$($fixture.quote.id)/documents/$($fixture.document.documentId)/content" -Headers @{Authorization="Bearer $script:token";'X-Workspace-Id'=$script:ws;'X-Request-Id'=('pdf-restart-'+[guid]::NewGuid().ToString('N'))} -NoProxy
  Check 'stored PDF HTTP after restart' 200 $pdf.StatusCode
  Check 'stored PDF SHA256 after restart' $fixture.document.pdfSha256 ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$pdf.Content)))
  [IO.File]::WriteAllBytes((Join-Path $EvidenceDirectory ('issued-'+$fixture.quote.currency+'.pdf')),[byte[]]$pdf.Content)
 }
 & (Join-Path $PSScriptRoot 'verify-quote-automatic-confirmation-sql.ps1') -DatabaseName $DatabaseName -SqlServer $SqlServer -EvidenceDirectory $EvidenceDirectory
 @(SQL "SELECT @@SERVERNAME ServerName, DB_NAME() DatabaseName;" )|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-target.json')
 @(SQL "SELECT OrderId,State,SourceQuoteId,ConfirmedBy,ConfirmationOrigin,ConfirmationPolicyVersion,GrandTotalAmount FROM orders.Orders")|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-orders.json')
 @(SQL "SELECT PaymentPlanId,OrderId,State,ResourceVersion FROM payments.PaymentPlans")|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-plans.json')
 $script:results|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'results.json');$script:http|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'http.json')
}finally{
 $script:results|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'results.json');$script:http|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'http.json');StopHost;if($script:vite -and !$script:vite.HasExited){Stop-Process -Id $script:vite.Id -Force}
}
