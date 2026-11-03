#requires -Version 7.0
[CmdletBinding()]
param(
 [ValidatePattern('^UnicoreCRM_OrderConfirmationTest_[A-Za-z0-9_]+$')][string]$DatabaseName=('UnicoreCRM_OrderConfirmationTest_'+[guid]::NewGuid().ToString('N')),
 [string]$SqlServer='DESKTOP-ICH225R',[int]$Port=5377,[switch]$Browser
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot;$root=Split-Path $repo
$hostDirectory=Join-Path $repo 'artifacts/shared-smtp-runtime'
$EvidenceDirectory=Join-Path $root ('task-runtime/shared-smtp-quote/connected/'+$DatabaseName)
[void](New-Item -ItemType Directory -Force $EvidenceDirectory)
$script:api="http://127.0.0.1:$Port";$script:connection="Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"
$script:token=$null;$script:ws=$null;$script:hostProcess=$null;$script:vite=$null
$script:results=[Collections.Generic.List[object]]::new();$script:http=[Collections.Generic.List[object]]::new()
$email='smtp.fixture@example.test';$password='Fixture-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$readerEmail='smtp.reader@example.test';$readerPassword='Fixture-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
$senderEmail='smtp.sender@example.test';$senderPassword='Fixture-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))+'!a9'
# Import definitions only. Existing scripts' main bodies are never evaluated.
foreach($source in @('verify-sales-acceptance-order.ps1','verify-quote-automatic-confirmation.ps1')){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $source),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'Fixture helper syntax error'}
 foreach($fn in $ast.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
  if($fn.Name -in @('SQL','Check','Api','Require','StartHost','StopHost','Login','Env','Policy','StaffBody','WaitOrder','Seed','DraftQuote','SentQuote','ConcurrentAcceptance')){. ([scriptblock]::Create($fn.Extent.Text))}
 }
}
function AssertConfirmed($fixture){
 $q=WaitOrder $fixture.quote.id;$o=Require GET "/orders/$($q.salesAcceptance.orderId)"
 $plans=@(Require GET "/payment-plans?orderId=$($o.id)")
 Check 'Order SQL-backed state' 'CONFIRMED' $o.state;Check 'one Payment Plan' 1 $plans.Count;Check 'Payment Plan active' 'ACTIVE' $plans[0].state
 Check 'commercial currency retained' $fixture.quote.currency $o.currency
 Check 'commercial total retained' $fixture.quote.grandTotal.amount $o.grandTotal.amount
 Check 'one Order per Quote' 1 (SQL "SELECT COUNT(*) C FROM orders.Orders WHERE SourceQuoteId='$($q.id)'")[0].C
 return @{quote=$q;order=$o}
}
try{
 # Name validation plus an existing-database refusal protect Development and prior evidence databases.
 [void](SQL "IF DB_ID('$DatabaseName') IS NOT NULL THROW 51000,'A fresh isolated database is required',1;CREATE DATABASE [$DatabaseName];" -Master)
 foreach($entry in @{ConnectionStrings__UnicoreCRM=$script:connection;DOTNET_ENVIRONMENT='Development';ASPNETCORE_ENVIRONMENT='Development';ASPNETCORE_URLS=$script:api;UNICORE_DEV_SEED_ENABLED='false';Frontend__AllowedOrigins__0='http://127.0.0.1:3057';Frontend__AllowedOrigins__1='http://127.0.0.1:3058';IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog'}.GetEnumerator()){Env $entry.Key $entry.Value}
 foreach($name in @('IdentityAuth','Workspace','AccessControl')){Env ($name+'__DevelopmentBootstrap__Enabled') 'false'}
 Push-Location $hostDirectory
 try{& (Join-Path $hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') --migrate *> (Join-Path $EvidenceDirectory 'migrate.log');if($LASTEXITCODE){throw 'Isolated migration failed'}}finally{Pop-Location}
 $workspaceKey='smtp-fixture-'+[guid]::NewGuid().ToString('N').Substring(0,8);$foreignKey=$workspaceKey+'-foreign'
 $policyText=Get-Content (Join-Path $repo 'src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs') -Raw
 $ownerText=$policyText.Substring(0,$policyText.IndexOf('internal static IReadOnlyList<string> CustomRole'))
 $allCapabilities=@([regex]::Matches($ownerText,'"([a-z][a-zA-Z.]+)"')|ForEach-Object{$_.Groups[1].Value}|Where-Object{$_ -match '\.'}|Sort-Object -Unique)
 Seed $email $password (@($allCapabilities)+@('quotes.issueDocument'))
 foreach($key in @([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -like 'AccessControl__DevelopmentBootstrap__Capabilities__*'})){Env $key $null}
 Seed $readerEmail $readerPassword @('workspace.context.resolve','contacts.read','orders.read','payments.plan.read','quotes.read')
 foreach($key in @([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -like 'AccessControl__DevelopmentBootstrap__Capabilities__*'})){Env $key $null}
 Seed $senderEmail $senderPassword @('workspace.context.resolve','contacts.read','quotes.read','quotes.create','quotes.update','quotes.issueDocument')
 $script:ws=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$workspaceKey'")[0].WorkspaceId
 $foreign=(SQL "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]='$foreignKey'")[0].WorkspaceId
 Policy
 foreach($e in @{OrdinarySalesWithoutInternalApproval='true';AutoConfirmOrders='true';AutoConfirmLimits__VND='100000000';AutoConfirmProductTypes__0='SERVICE';AutoConfirmProductTypes__1='DIGITAL';RequireStaffOperationalApproval='false'}.GetEnumerator()){Env ("QuoteSalesAcceptance__Workspaces__$($script:ws)__"+$e.Key) $e.Value}
 StartHost;Login
 $config=Require GET '/workspace-configuration';$business=$config.businessInformation;$business.displayName='SMTP fixture seller';$business.legalName='SMTP fixture seller';$business.email='seller@example.test'
 $addresses=@(@{id='smtp_registered';name='Registered';purposes=@('REGISTERED');addressLine1='Test Street';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='Test';contactPhone='';active=$true})
 [void](Require PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=$addresses} 'smtp-business-0001' $config.revision)
 $config=Require GET '/workspace-configuration';$features=$config.features;$features.quotes=$true;$features.orders=$true;$features.payments=$true
 [void](Require PATCH '/workspace-configuration/features' @{features=$features} 'smtp-features-0001' $config.revision)
 $script:buyer=(Require POST '/contacts' @{fullName='SMTP fixture buyer';personalEmail='customer@example.test'} 'smtp-buyer-0001').result.contact.id
 $issued=DraftQuote 'SMTP failure must not mark SENT';$q=$issued.quote;$path="/quotes/$($q.id)/acceptance-invitations"
 Check 'issued Quote starts DRAFT' 'DRAFT' $q.status
 Check 'Workspace Gmail allowlist denied' 'GMAIL_WORKSPACE_NOT_ALLOWED' (Require GET "/quotes/$($q.id)").actions.sendGmail.blockerCodes[0]
 $request=@{documentId=$issued.document.documentId;sendGmail=$true}
 $override=$request.Clone();$override.recipientEmail='unauthorized@example.test'
 Check 'client cannot override recipient' 422 (Api POST $path $override 'smtp-recipient-tamper-0001' $q.resourceVersion).status
 $saved=$script:ws;$script:ws=$foreign
 Check 'cross Workspace Gmail denied' 403 (Api POST $path $request 'smtp-foreign-workspace-0001' $q.resourceVersion).status
 $script:ws=$saved
 $failed=Require POST $path $request 'smtp-denied-0001' $q.resourceVersion
 Check 'Gmail denied outcome' 'FAILED' $failed.deliveryStatus;Check 'Gmail denied reason' 'GMAIL_WORKSPACE_NOT_ALLOWED' $failed.deliveryErrorCode
 Check 'failed SMTP leaves Quote DRAFT' 'DRAFT' (Require GET "/quotes/$($q.id)").status
 Check 'same-key replay retains invitation' $failed.invitationId (Require POST $path $request 'smtp-denied-0001' $q.resourceVersion).invitationId
 Check 'one invitation after replay' 1 (SQL "SELECT COUNT(*) C FROM quotes.AcceptanceInvitations WHERE QuoteId='$($q.id)'")[0].C
 Check 'changed invitation payload conflicts' 409 (Api POST $path @{documentId=$issued.document.documentId;sendGmail=$false} 'smtp-denied-0001' $q.resourceVersion).status
 # This permission applies only to the synthetic fixture Workspace and this isolated process.
 StopHost;Env "QuoteGmail__Workspaces__$($script:ws)__Enabled" 'true';StartHost;Login
 Check 'DevelopmentLog cannot fake Gmail' 'GMAIL_NOT_CONFIGURED' (Require POST $path $request 'smtp-unavailable-0001' $q.resourceVersion).deliveryErrorCode
 Check 'no false provider evidence in SQL' 0 (SQL "SELECT COUNT(*) C FROM quotes.Quotes WHERE QuoteId='$($q.id)' AND DeliveryHistoryJson IS NOT NULL")[0].C
 $staff=SentQuote 'Manual acceptance confirmed';$body=StaffBody $staff;$staffPath="/quotes/$($staff.quote.id)/sales-acceptances"
 $sent=Require GET "/quotes/$($staff.quote.id)"
 Check 'manual send evidence is distinct' 'USER_CONFIRMED_SENT' $sent.deliveryHistory[0].evidenceType
 Check 'manual action allowed by server' True $sent.actions.recordSalesAcceptance.allowed
 $invalid=$body.Clone();$invalid.responsibilityConfirmed=$false
 Check 'attestation required' 422 (Api POST $staffPath $invalid 'staff-invalid-0001' $staff.quote.resourceVersion).status
 Check 'staff acceptance commits' 200 (Api POST $staffPath $body 'staff-valid-0001' $staff.quote.resourceVersion).status
 Check 'staff acceptance replay' 200 (Api POST $staffPath $body 'staff-valid-0001' $staff.quote.resourceVersion).status
 $tamper=$body.Clone();$tamper.evidence='Changed'
 Check 'staff payload conflict' 409 (Api POST $staffPath $tamper 'staff-valid-0001' $staff.quote.resourceVersion).status
 $confirmed=AssertConfirmed $staff
 Check 'ordinary attestation stays unverified' 'ORDINARY_SALES_UNVERIFIED' $confirmed.quote.salesAcceptance.assuranceLevel
 $customer=SentQuote 'Customer link confirmed'
 $inv=Require POST "/quotes/$($customer.quote.id)/acceptance-invitations" @{documentId=$customer.document.documentId;sendGmail=$false} 'customer-link-0001' $customer.quote.resourceVersion
 $consentToken=$inv.acceptanceUrl.Split('/')[-1];$preview=Api POST '/public/quote-acceptance/preview' @{token=$consentToken} -Public
 Check 'customer preview succeeds' 200 $preview.status
 $consent=@{token=$consentToken;agree=$true;termsVersion=$preview.body.termsVersion}
 Check 'explicit consent commits' 200 (Api POST '/public/quote-acceptance/consent' $consent 'customer-consent-0001' -Public).status
 Check 'consent replay' 200 (Api POST '/public/quote-acceptance/consent' $consent 'customer-consent-0001' -Public).status
 [void](AssertConfirmed $customer)
 $concurrent=SentQuote 'Concurrent staff requests';ConcurrentAcceptance $concurrent;[void](AssertConfirmed $concurrent)
 $negative=SentQuote 'Security guards';$nb=StaffBody $negative;$path="/quotes/$($negative.quote.id)/sales-acceptances";$v=$negative.quote.resourceVersion
 Check 'anonymous denied' 401 (Api POST $path $nb 'security-anon-0001' $v -Public).status
 Check 'stale version denied' 412 (Api POST $path $nb 'security-stale-0001' ($v-1)).status
 $owner=$script:token;$script:token=(Require POST '/auth/sessions' @{email=$readerEmail;password=$readerPassword} 'reader-login-0001').accessToken
 Check 'insufficient capability denied' 403 (Api POST $path $nb 'security-capability-0001' $v).status;$script:token=$owner
 $saved=$script:ws;$script:ws=$foreign;Check 'cross Workspace denied' 403 (Api POST $path $nb 'security-workspace-0001' $v).status;$script:ws=$saved
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'true';StartHost;Login
 Check 'B6 not bypassed' 'VERIFIED_SIGNER_AUTHORITY_REQUIRED' (Api POST $path $nb 'security-assurance-0001' $v).body.code
 $owner=$script:token;$script:token=(Require POST '/auth/sessions' @{email=$senderEmail;password=$senderPassword} 'sender-login-0001').accessToken
 $pdfOnly=DraftQuote 'Send PDF without Orders capability'
 $pdfResult=Require POST "/quotes/$($pdfOnly.quote.id)/acceptance-invitations" @{documentId=$pdfOnly.document.documentId;sendGmail=$true} 'pdf-only-0001' $pdfOnly.quote.resourceVersion
 Check 'sender without orders.create can submit delivery' 'GMAIL_NOT_CONFIGURED' $pdfResult.deliveryErrorCode
 Check 'high assurance delivery contains no ordinary link' False $pdfResult.hasAcceptanceLink
 Check 'PDF-only stored envelope contains no link' 0 (SQL "SELECT LEN(ProtectedDeliveryPayload) C FROM quotes.AcceptanceInvitations WHERE InvitationId='$($pdfResult.invitationId)'")[0].C
 Check 'same sender cannot accept without Orders capability' 403 (Api POST "/quotes/$($negative.quote.id)/sales-acceptances" $nb 'sender-cannot-accept-0001' $v).status
 $script:token=$owner
 StopHost;Env "QuoteSalesAcceptance__Workspaces__$($script:ws)__RequireVerifiedSignerAuthority" 'false';StartHost;Login
 Check 'restart retains confirmed Order' 'CONFIRMED' (Require GET "/orders/$($confirmed.order.id)").state
 foreach($entry in @{QUOTE_DELIVERY_TEST_WORKSPACE=$script:ws;QUOTE_DELIVERY_TEST_EMAIL=$email;QUOTE_DELIVERY_TEST_PASSWORD=$password;QUOTE_DELIVERY_TEST_BUYER=$script:buyer}.GetEnumerator()){Env $entry.Key $entry.Value}
 & dotnet test (Join-Path $repo 'tests/UnicoreCRM.ArchitectureTests/UnicoreCRM.ArchitectureTests.csproj') --no-restore "-p:OutputPath=$repo/artifacts/shared-smtp-tests/bin/" --filter 'FullyQualifiedName~QuoteSmtpRecoveryTests' --logger 'trx;LogFileName=smtp-recovery.trx' --results-directory $EvidenceDirectory *> (Join-Path $EvidenceDirectory 'smtp-recovery.log')
 Check 'HTTP SQL simulated provider recovery' 0 $LASTEXITCODE
 if($Browser){
  # A closed loopback port exercises real connection failure, never Gmail or external recipients.
  StopHost
  foreach($entry in @{Kind='GmailSmtp';Host='127.0.0.1';Port='65432';Username='smtp.fixture@example.test';AppPassword='isolated-dummy-password';FromAddress='smtp.fixture@example.test';TimeoutSeconds='3'}.GetEnumerator()){Env ('IdentityAuth__EmailVerification__Sender__'+$entry.Key) $entry.Value}
  StartHost;Login
  $draft=DraftQuote 'Connected browser issuance' -Unissued;$public=SentQuote 'Connected browser consent'
  $missing=DraftQuote 'Manual SENT without an issued PDF' -Unissued
  [void](Require POST "/quotes/$($missing.quote.id)/send" @{deliveryId=('manual_'+[guid]::NewGuid().ToString('N'));channel='EMAIL';evidenceType='USER_CONFIRMED_SENT';recipientEmail='customer@example.test'} 'browser-missing-document-0001' $missing.quote.resourceVersion)
  Env 'ACCEPTANCE_TEST_MISSING_DOCUMENT_QUOTE' $missing.quote.id
  $link=Require POST "/quotes/$($public.quote.id)/acceptance-invitations" @{documentId=$public.document.documentId;sendGmail=$false} 'browser-link-0001' $public.quote.resourceVersion
  foreach($entry in @{ACCEPTANCE_TEST_API=$script:api;ACCEPTANCE_TEST_WORKSPACE=$workspaceKey;ACCEPTANCE_TEST_WORKSPACE_ID=$script:ws;ACCEPTANCE_TEST_TOKEN=$link.acceptanceUrl.Split('/')[-1];ACCEPTANCE_TEST_EVIDENCE=(Join-Path $EvidenceDirectory 'browser');ACCEPTANCE_TEST_EMAIL=$email;ACCEPTANCE_TEST_PASSWORD=$password;ACCEPTANCE_TEST_STAFF_QUOTE=$draft.quote.id;ACCEPTANCE_TEST_CUSTOMER_QUOTE=$public.quote.id;VITE_RUNTIME_MODE='connected';VITE_API_BASE_URL=$script:api}.GetEnumerator()){Env $entry.Key $entry.Value}
  $script:vite=Start-Process node -WorkingDirectory (Join-Path $root 'frontend/unicorecrm-web') -ArgumentList @('node_modules/vite/bin/vite.js','--host','127.0.0.1','--port','3057','--strictPort') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $EvidenceDirectory 'vite.log') -RedirectStandardError (Join-Path $EvidenceDirectory 'vite-error.log')
  for($i=0;$i -lt 60;$i++){if($script:vite.HasExited){throw 'Isolated Vite exited'};try{if((Invoke-WebRequest 'http://127.0.0.1:3057' -NoProxy).StatusCode -eq 200){break}}catch{};Start-Sleep -Milliseconds 500}
  & node (Join-Path $PSScriptRoot 'verify-quote-shared-smtp-browser.mjs');Check 'connected Chromium' 0 $LASTEXITCODE
  [void](AssertConfirmed $draft);[void](AssertConfirmed $public)
 }
 Check 'no duplicate Orders SQL' 0 (SQL "SELECT COUNT(*) C FROM (SELECT SourceQuoteId FROM orders.Orders WHERE SourceQuoteId IS NOT NULL GROUP BY SourceQuoteId HAVING COUNT(*)>1) d")[0].C
 Check 'no orphan Payment Plans SQL' 0 (SQL "SELECT COUNT(*) C FROM payments.PaymentPlans p LEFT JOIN orders.Orders o ON o.OrderId=p.OrderId WHERE o.OrderId IS NULL")[0].C
 @{database=$DatabaseName;workspaceId=$script:ws;workspaceKey=$workspaceKey;realGmail='GMAIL_REAL_DELIVERY_NOT_RUN'}|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'context.json')
 @(SQL 'SELECT OrderId,SourceQuoteId,State,GrandTotalAmount FROM orders.Orders')|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-orders.json')
 @(SQL 'SELECT PaymentPlanId,OrderId,State FROM payments.PaymentPlans')|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-plans.json')
}finally{
 $script:results|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'results.json');$script:http|ConvertTo-Json -Depth 12|Set-Content (Join-Path $EvidenceDirectory 'http.json')
 StopHost;if($script:vite -and !$script:vite.HasExited){Stop-Process -Id $script:vite.Id -Force}
}
