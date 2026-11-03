#requires -Version 7.0
[CmdletBinding()]
param([ValidatePattern('^UnicoreCRM_OrderConfirmationTest_[A-Za-z0-9_]+$')][string]$DatabaseName=('UnicoreCRM_OrderConfirmationTest_'+[guid]::NewGuid().ToString('N')),[switch]$Browser,[int]$Port=5367,[string]$SqlServer='DESKTOP-ICH225R')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot);$repo=Join-Path $root 'backend';$hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$EvidenceDirectory=Join-Path $root ('task-runtime/order-confirmation-o2/'+$DatabaseName);[void](New-Item -ItemType Directory -Force $EvidenceDirectory)
$script:api="http://127.0.0.1:$Port";$script:connection="Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"
$script:token=$null;$script:ws=$null;$script:hostProcess=$null;$script:vite=$null
$script:results=[Collections.Generic.List[object]]::new();$script:http=[Collections.Generic.List[object]]::new()
$email='o2.confirmer@example.test';$password='O2-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$readerEmail='o2.reader@example.test';$readerPassword='O2-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verify-sales-acceptance-order.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'O1 helper syntax error'}
foreach($fn in $ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($fn.Name -in @('SQL','Check','Api','Require','StartHost','StopHost','Login','Env','Policy','StaffBody','WaitOrder')){. ([scriptblock]::Create($fn.Extent.Text))}}
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
function NewOrder([string]$Name,[string]$ProductType='SERVICE',[string]$AcceptanceSource='STAFF_RECORDED'){
 $body=@{buyerRef=@{type='CONTACT';id=$script:buyer};sourcePath='DIRECT_SALE';title=$Name;currency='VND';recipientEmail='customer@example.test';validUntil=[DateTime]::UtcNow.AddDays(30).ToString('yyyy-MM-dd');lineItems=@(@{productNameSnapshot='O2 test product';productTypeSnapshot=$ProductType;quantity='2';unitPrice=@{amount='125000';currency='VND'};discountRate='10';taxMode='EXCLUSIVE';taxRate='10'});paymentAgreement=@{kind='FULL_PAYMENT';currency='VND';lines=@(@{sequence=1;label='Full payment';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=0;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');fulfillmentGate='NONE'})}}
 $q=(Require POST '/quotes' $body ('create-'+[guid]::NewGuid().ToString('N'))).result.quote
 $q=(Require POST "/quotes/$($q.id)/request-approval" @{} ('assess-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion).result.quote
 $issued=Require POST "/quotes/$($q.id)/documents/issue" @{} ('issue-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion;$q=$issued.result.quote
 $q=(Require POST "/quotes/$($q.id)/send" @{deliveryId=('delivery_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} ('send-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion).result.quote
 if($AcceptanceSource -eq 'CUSTOMER_LINK'){
  $inv=Require POST "/quotes/$($q.id)/acceptance-invitations" @{documentId=$issued.result.document.documentId;sendGmail=$false} ('invite-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion
  $invitationToken=$inv.acceptanceUrl.Split('/')[-1];$preview=Api POST '/public/quote-acceptance/preview' @{token=$invitationToken} -Public
  $consent=Api POST '/public/quote-acceptance/consent' @{token=$invitationToken;agree=$true;termsVersion=$preview.body.termsVersion} ('consent-'+[guid]::NewGuid().ToString('N')) -Public
  Check 'fresh O2 customer link acceptance' 200 $consent.status
 }else{[void](Require POST "/quotes/$($q.id)/sales-acceptances" (StaffBody @{quote=$q;document=$issued.result.document}) ('accept-'+[guid]::NewGuid().ToString('N')) $q.resourceVersion)}
 $accepted=WaitOrder $q.id;$o=Require GET "/orders/$($accepted.salesAcceptance.orderId)";$plans=@(Require GET "/payment-plans?orderId=$($o.id)")
 if($plans.Count -ne 1){throw 'Expected exactly one owner-created plan'}
 return @{order=$o;plan=$plans[0];quote=$accepted}
}
function ConfirmBody($f){return @{paymentPlanId=$f.plan.id;expectedPaymentPlanVersion=$f.plan.resourceVersion;operationalReviewConfirmed=$true;operationalReviewNotes='Reviewed source acceptance, buyer, totals, payment agreement and operational readiness.'}}
function Confirm($f,$Body=$null,[string]$Key=''){
 if(!$Body){$Body=ConfirmBody $f};if(!$Key){$Key='confirm-'+[guid]::NewGuid().ToString('N')}
 return Api POST "/workflows/order-confirmation/$($f.order.id)/confirm-with-payment-plan" $Body $Key $f.order.resourceVersion
}
function Concurrent($f){
 $client=[Net.Http.HttpClient]::new();$requests=@();$tasks=@();$body=(ConfirmBody $f)|ConvertTo-Json -Compress;$shared='o2-concurrent-'+[guid]::NewGuid().ToString('N')
 try{
  for($i=0;$i -lt 8;$i++){$r=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$script:api/workflows/order-confirmation/$($f.order.id)/confirm-with-payment-plan");foreach($h in @{Authorization="Bearer $script:token";'X-Workspace-Id'=$script:ws;'X-Request-Id'="o2-concurrent-$i-0001";'Idempotency-Key'=$(if($i -lt 4){$shared}else{"$shared-$i"});'If-Match'=('"'+$f.order.resourceVersion+'"')}.GetEnumerator()){$r.Headers.TryAddWithoutValidation($h.Key,$h.Value)|Out-Null};$r.Content=[Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json');$requests+=,$r;$tasks+=,$client.SendAsync($r)}
  $statuses=@(foreach($task in $tasks){$response=$task.GetAwaiter().GetResult();try{[int]$response.StatusCode}finally{$response.Dispose()}})
  Check '8 concurrent requests accept/replay one intent' True ($statuses -contains 200);Check '8 concurrent requests fail safely' True (@($statuses|Where-Object{$_ -notin 200,409,412}).Count -eq 0)
 }finally{foreach($r in $requests){$r.Dispose()};$client.Dispose()}
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
 $script:ws=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$workspaceKey'")[0].WorkspaceId
 $foreign=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$foreignKey'")[0].WorkspaceId
 Policy;StartHost;Login
 $config=Require GET '/workspace-configuration';$business=$config.businessInformation;$business.displayName='O2 Test Seller';$business.legalName='O2 Test Seller';$business.email='seller@example.test'
 $addresses=@(@{id='test_registered';name='Registered';purposes=@('REGISTERED');addressLine1='Test Street';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='Test';contactPhone='';active=$true})
 [void](Require PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=$addresses} ('business-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $config=Require GET '/workspace-configuration';$features=$config.features;$features.quotes=$true;$features.orders=$true;$features.payments=$true
 [void](Require PATCH '/workspace-configuration/features' @{features=$features} ('features-'+[guid]::NewGuid().ToString('N')) $config.revision)
 $script:buyer=(Require POST '/contacts' @{fullName='O2 test buyer';personalEmail='customer@example.test'} ('buyer-'+[guid]::NewGuid().ToString('N'))).result.contact.id
 $f=NewOrder 'O2 confirmation positive';$path="/workflows/order-confirmation/$($f.order.id)/confirm-with-payment-plan";$body=ConfirmBody $f
 Check 'DRAFT Order generated by current O1 flow' 'DRAFT' $f.order.state;Check 'Payment Plan DRAFT owner-created' 'DRAFT' $f.plan.state
 Check 'anonymous denied' 401 (Api POST $path $body 'o2-anonymous-0001' $f.order.resourceVersion -Public).status
 $savedToken=$script:token;$script:token=(Require POST '/auth/sessions' @{email=$readerEmail;password=$readerPassword} 'reader-login-0001').accessToken
 Check 'insufficient confirmation capability denied' 403 (Confirm $f).status;$script:token=$savedToken
 $savedWs=$script:ws;$script:ws=$foreign;Check 'cross Workspace denied' 403 (Confirm $f).status;$script:ws=$savedWs
 Check 'stale Order version denied' 412 (Api POST $path $body 'o2-stale-order-0001' ($f.order.resourceVersion+1)).status
 $bad=$body.Clone();$bad.expectedPaymentPlanVersion=$f.plan.resourceVersion+1;Check 'stale Plan version denied' 412 (Confirm $f $bad).status
 $bad=$body.Clone();$bad.operationalReviewConfirmed=$false;$bad.operationalReviewNotes='';Check 'missing operational approval denied' 'OPERATIONAL_APPROVAL_REQUIRED' (Confirm $f $bad).body.code
 $bad=$body.Clone();$bad.grandTotal=@{amount='1';currency='VND'};Check 'client price override refused' 422 (Confirm $f $bad).status
 $bad=$body.Clone();$bad.confirmedBy='fake_actor';Check 'client actor override refused' 422 (Confirm $f $bad).status
 $bad=$body.Clone();$bad.paymentPlanId='plan_does_not_exist';Check 'missing matching Payment Plan denied' 'PAYMENT_PLAN_DRAFT_REQUIRED' (Confirm $f $bad).body.code
 [void](SQL "UPDATE payments.PaymentPlans SET Currency='USD' WHERE WorkspaceId='$($script:ws)' AND PaymentPlanId='$($f.plan.id)'")
 Check 'Payment Plan currency binding mismatch denied' 'PAYMENT_PLAN_AGREEMENT_MISMATCH' (Confirm $f).body.code
 [void](SQL "UPDATE payments.PaymentPlans SET Currency='VND' WHERE WorkspaceId='$($script:ws)' AND PaymentPlanId='$($f.plan.id)'")
 # Negative tampering only: no SQL writes of successful confirmations, approval evidence or RBAC.
 [void](SQL "UPDATE orders.Orders SET GrandTotalAmount=GrandTotalAmount+1 WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")
 Check 'commercial snapshot mismatch denied' 'COMMERCIAL_SNAPSHOT_MISMATCH' (Confirm $f).body.code
 [void](SQL "UPDATE orders.Orders SET GrandTotalAmount=GrandTotalAmount-1 WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")
 $creditJson='{"status":"APPROVAL_REQUIRED","blockerCodes":["CREDIT_APPROVAL_REQUIRED"]}'
 [void](SQL "UPDATE orders.Orders SET CreditPolicyEvaluationJson=N'$creditJson' WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")
 Check 'credit approval required denied' 'CREDIT_APPROVAL_REQUIRED' (Confirm $f).body.code
 [void](SQL "UPDATE orders.Orders SET CreditPolicyEvaluationJson=NULL WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")
 [void](SQL "CREATE TRIGGER payments.O2ActivationFailure ON payments.PlanEvents AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE EventType='PAYMENT_PLAN_ACTIVATED') THROW 51031,'O2 isolated activation failure',1;END")
 $faultKey='o2-rollback-'+[guid]::NewGuid().ToString('N')
 Check 'Payment activation failure returns no success' 500 (Confirm $f $body $faultKey).status
 Check 'Order rollback after activation failure' 'DRAFT' (Require GET "/orders/$($f.order.id)").state
 Check 'Plan rollback after activation failure' 'DRAFT' (@(Require GET "/payment-plans?orderId=$($f.order.id)")[0]).state
 Check 'confirmation receipt rollback' 0 (SQL "SELECT COUNT(*) C FROM workflow.OrderConfirmationReceipts WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")[0].C
 Check 'schedule version and amounts rollback' 0 (SQL "SELECT COUNT(*) C FROM payments.PaymentScheduleLines WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)' AND (PaymentPlanVersion<>1 OR SatisfiedAmount<>0 OR ResolvedDueDate IS NOT NULL)")[0].C
 Check 'Payment Plan outbox rollback' 0 (SQL "SELECT COUNT(*) C FROM payments.PlanEvents WHERE WorkspaceId='$($script:ws)' AND PlanId='$($f.plan.id)' AND EventType='PAYMENT_PLAN_ACTIVATED'")[0].C
 Check 'Order outbox rollback' 0 (SQL "SELECT COUNT(*) C FROM orders.OutboxMessages WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)' AND EventType='ORDER_CONFIRMED'")[0].C
 [void](SQL 'DROP TRIGGER payments.O2ActivationFailure')
 $key=$faultKey;$r=Confirm $f $body $key;Check 'confirmation HTTP committed after rollback with same key' 200 $r.status;Check 'authoritative mutation evidence' 'COMMITTED' $r.body.outcome
 $replay=Confirm $f $body $key;Check 'same idempotency intent replays' 'REPLAYED' $replay.body.outcome;Check 'replay returns original command' $r.body.commandId $replay.body.commandId
 $bad=$body.Clone();$bad.operationalReviewNotes='different';Check 'same key different payload denied' 'IDEMPOTENCY_KEY_REUSED' (Confirm $f $bad $key).body.code
 $current=Require GET "/orders/$($f.order.id)";Check 'confirmed state persists' 'CONFIRMED' $current.state;Check 'ordinary assurance not upgraded' 'ORDINARY_SALES_UNVERIFIED' $current.acceptance.assuranceLevel
 Check 'confirmedBy uses authenticated recorder' $f.quote.salesAcceptance.recordedBy (SQL "SELECT ConfirmedBy FROM orders.Orders WHERE WorkspaceId='$($script:ws)' AND OrderId='$($f.order.id)'")[0].ConfirmedBy
 Check 'actual emitted Order event retained' 1 (SQL "SELECT COUNT(*) C FROM orders.OutboxMessages WHERE EventId='$($r.body.commandId)-order' AND EventType='ORDER_CONFIRMED'")[0].C
 Check 'actual emitted Payment Plan event retained' 1 (SQL "SELECT COUNT(*) C FROM payments.PlanEvents WHERE EventId='$($r.body.commandId)-plan' AND EventType='PAYMENT_PLAN_ACTIVATED'")[0].C
 Check 'double confirmation denied' 409 (Api POST $path $body ('double-'+[guid]::NewGuid().ToString('N')) $current.resourceVersion).status
 $race=NewOrder 'Eight concurrent confirmations';Concurrent $race
 Check 'one concurrent confirmation receipt' 1 (SQL "SELECT COUNT(*) C FROM workflow.OrderConfirmationReceipts WHERE WorkspaceId='$($script:ws)' AND OrderId='$($race.order.id)'")[0].C
 Check 'one plan per Order' 1 (SQL "SELECT COUNT(*) C FROM payments.PaymentPlans WHERE WorkspaceId='$($script:ws)' AND OrderId='$($race.order.id)'")[0].C
 $physical=NewOrder 'Physical fulfillment required' 'PHYSICAL';Check 'physical fulfillment missing denied' 'FULFILLMENT_INFORMATION_REQUIRED' (Confirm $physical).body.code
 $physicalBody=ConfirmBody $physical;$physicalBody.fulfillment=@{recipientName='Test recipient';recipientPhone='0900000000';shippingAddress=@{line1='Test delivery street';city='Test City'}}
 Check 'physical Order confirms with required fulfillment' 200 (Confirm $physical $physicalBody).status
 Check 'physical recipient is durable' 'Test recipient' (Require GET "/orders/$($physical.order.id)").recipientName
 $customer=NewOrder 'Customer link confirmation' 'SERVICE' 'CUSTOMER_LINK';$customerBody=ConfirmBody $customer;$customerBody.operationalReviewConfirmed=$false;$customerBody.operationalReviewNotes=''
 Check 'customer link follows source-specific operational policy' 200 (Confirm $customer $customerBody).status
 Check 'customer link remains ordinary after confirmation' 'ORDINARY_SALES_UNVERIFIED' (Require GET "/orders/$($customer.order.id)").acceptance.assuranceLevel
 $higher=NewOrder 'Higher signer assurance required';StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'true';StartHost;Login
 Check 'higher assurance remains enforced' 'VERIFIED_SIGNER_AUTHORITY_REQUIRED' (Confirm $higher).body.code
 StopHost;Policy;StartHost;Login
 # Reproduce an acknowledged O1 draft with a lost/missing plan. Only the owner recovery may rebuild it.
 $legacy=NewOrder 'Existing O1 draft plan recovery';StopHost
 [void](SQL "DELETE FROM payments.PaymentScheduleLines WHERE WorkspaceId='$($script:ws)' AND PaymentPlanId='$($legacy.plan.id)'; DELETE FROM payments.PaymentPlans WHERE WorkspaceId='$($script:ws)' AND PaymentPlanId='$($legacy.plan.id)';")
 [void](SQL "CREATE TRIGGER payments.O2LegacyPlanPreparationFailure ON payments.PaymentPlans AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE OrderId='$($legacy.order.id)') THROW 51032,'Isolated missing plan recovery fault',1;END")
 StartHost;Login
 Check 'actually missing Payment Plan blocks confirmation' 'PAYMENT_PLAN_DRAFT_REQUIRED' (Confirm $legacy).body.code
 [void](SQL 'DROP TRIGGER payments.O2LegacyPlanPreparationFailure')
 $recovered=@();for($i=0;$i -lt 45;$i++){$recovered=@(Require GET "/payment-plans?orderId=$($legacy.order.id)");if($recovered.Count){break};Start-Sleep -Seconds 1}
 Check 'existing O1 draft receives one recovered plan' 1 $recovered.Count
 Check 'recovery does not confirm Order' 'DRAFT' (Require GET "/orders/$($legacy.order.id)").state
 $legacy.plan=$recovered[0];Check 'recovered draft can confirm through real operation' 200 (Confirm $legacy).status
 $ui=NewOrder 'Chromium confirmation';@{database=$DatabaseName;workspaceId=$script:ws;workspaceKey=$workspaceKey;orderId=$ui.order.id;confirmedHttpOrderId=$f.order.id}|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'context.json')
 if($Browser){
  foreach($entry in @{O2_API=$script:api;O2_WORKSPACE=$workspaceKey;O2_ORDER=$ui.order.id;O2_EMAIL=$email;O2_PASSWORD=$password;O2_EVIDENCE=$EvidenceDirectory;VITE_RUNTIME_MODE='connected';VITE_API_BASE_URL=$script:api}.GetEnumerator()){Env $entry.Key $entry.Value}
  $frontend=Join-Path $root 'frontend/unicorecrm-web';$script:vite=Start-Process node -WorkingDirectory $frontend -ArgumentList @('node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','3057','--strictPort') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'vite.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'vite-error.log')
  & node (Join-Path $PSScriptRoot 'verify-order-confirmation-browser.mjs');Check 'real Chromium confirmation' 0 $LASTEXITCODE
 }
 StopHost;StartHost;Login
 Check 'restart and relogin preserve confirmation' 'CONFIRMED' (Require GET "/orders/$($f.order.id)").state
 Check 'restart preserves ACTIVE Plan' 'ACTIVE' (@(Require GET "/payment-plans?orderId=$($f.order.id)")[0]).state
 Check 'no payment collection fabricated' 0 (SQL 'SELECT COUNT(*) C FROM payments.PaymentRecords')[0].C
 if($Browser){Env 'O2_PHASE' 'restart';& node (Join-Path $PSScriptRoot 'verify-order-confirmation-browser.mjs');Check 'real Chromium restart readback' 0 $LASTEXITCODE}
}finally{StopHost;if($script:vite -and !$script:vite.HasExited){Stop-Process -Id $script:vite.Id -Force};$script:results|ConvertTo-Json -Depth 15|Set-Content (Join-Path $EvidenceDirectory 'assertions.json');$script:http|ConvertTo-Json -Depth 10|Set-Content (Join-Path $EvidenceDirectory 'http.json')}
