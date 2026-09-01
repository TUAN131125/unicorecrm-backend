<#
.SYNOPSIS
    Verifies the Payments-owned Payment Intent read core against an isolated database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5358,
    [int] $ReadyTimeoutSeconds = 420,
    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$script:Passed = 0
$script:Failed = 0
$script:Results = New-Object System.Collections.ArrayList
$script:RequestCounter = 0
$script:BaseUrl = "http://127.0.0.1:$Port"
$script:Token = $null
$script:WorkspaceId = $null

function Add-Result([string] $Name, [string] $Expected, [string] $Actual) {
    if ($Expected -eq $Actual) { $script:Passed++; [void]$script:Results.Add("PASS | $Name | $Actual") }
    else { $script:Failed++; [void]$script:Results.Add("FAIL | $Name | expected=$Expected actual=$Actual") }
}
function New-ConnectionString([string] $Database) { "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" }
function Invoke-SqlNonQuery([string] $Query, [string] $Database = 'master') {
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database)
    $command = $null; $connection.Open()
    try { $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120; [void]$command.ExecuteNonQuery() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose() }
}
function Get-Scalar([string] $Query, [string] $Database) {
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database)
    $command = $null; $connection.Open()
    try { $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120; return $command.ExecuteScalar() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose() }
}
function New-RequestId { $script:RequestCounter++; 'req-payment-intent-read-{0:d6}' -f $script:RequestCounter }
function Invoke-Api {
    param([string] $Method, [string] $Path, [string] $Body, [string] $Token, [string] $WorkspaceId, [string] $IdempotencyKey)
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-payment-intent-read-0001')
    if ($Token) { [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if ($WorkspaceId) { [void]$request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if ($IdempotencyKey) { [void]$request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if ($Body) { $request.Content = New-Object System.Net.Http.StringContent ($Body, [Text.Encoding]::UTF8, 'application/json') }
    $handler = New-Object System.Net.Http.HttpClientHandler; $handler.UseProxy = $false; $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient ($handler, $true); $client.Timeout = [TimeSpan]::FromSeconds(60)
    $response = $null
    try { $response = $client.SendAsync($request).GetAwaiter().GetResult(); $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); $status = [int]$response.StatusCode }
    finally { if ($null -ne $response) { $response.Dispose() }; $client.Dispose(); $request.Dispose() }
    $payload = $null; if ($raw) { try { $payload = $raw | ConvertFrom-Json } catch { } }
    [pscustomobject]@{ Status = $status; Body = $payload; Raw = $raw }
}
function Invoke-Intent([string] $Path) { Invoke-Api -Method GET -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId }
function Set-PaymentScope([string] $RoleId, [string] $Scope) {
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_payment_intent_read_core';
INSERT INTO access.RoleDataScopes (PolicyId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson)
VALUES ('scope_payment_intent_read_core','$RoleId','payments','$Scope','[]');
"@
}
function Clear-IntentFields { Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_intent_read_%'" }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$paymentsRoot = Join-Path $repositoryRoot 'src/UnicoreCRM.Billing/Payments'
$email = 'payment.intent.read@example.test'; $password = 'Payment-Intent-Read!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-payment-intent-read-$([Guid]::NewGuid().ToString('N')).log")
$intentA = 'payment_intent_read_a'; $intentB = 'payment_intent_read_b'; $intentForeign = 'payment_intent_read_foreign'
$orderA = 'order_intent_read_a'; $orderB = 'order_intent_read_b'; $foreignWorkspaceId = 'ws_payment_intent_foreign'

try {
    Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;
CREATE DATABASE [$DatabaseName];
"@
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = New-ConnectionString $DatabaseName; $env:Development__ApplyMigrations = 'true'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'; $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password; $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Payment Intent Read Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'; $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'; $env:AI__Provider__Kind = 'DevelopmentDeterministic'
    $hostProcess = Start-Process -FilePath dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$hostProject) -PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"
    $ready = $false
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        Start-Sleep -Seconds 1
        if ($hostProcess.HasExited) { throw "ApiHost exited with $($hostProcess.ExitCode). See $logPath" }
        try { if ((Invoke-Api -Method GET -Path '/auth/session').Status -gt 0) { $ready = $true; break } } catch { }
    }
    if (-not $ready) { throw "ApiHost not ready. See $logPath" }

    Add-Result 'unauthenticated intent list rejected' '401' ([string](Invoke-Api -Method GET -Path '/payment-intents' -WorkspaceId 'ws_unknown').Status)
    Add-Result 'unauthenticated intent detail rejected' '401' ([string](Invoke-Api -Method GET -Path "/payment-intents/$intentA" -WorkspaceId 'ws_unknown').Status)
    Add-Result 'unauthenticated intent status rejected' '401' ([string](Invoke-Api -Method GET -Path "/payment-intents/$intentA/status" -WorkspaceId 'ws_unknown').Status)
    $signIn = Invoke-Api -Method POST -Path '/auth/sessions' -IdempotencyKey 'idem-payment-intent-signin-0001' -Body (@{email=$email;password=$password}|ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed: $($signIn.Raw)" }; $script:Token = $signIn.Body.accessToken
    $provision = Invoke-Api -Method POST -Path '/workspaces/initial-provisioning' -Token $script:Token -IdempotencyKey 'idem-payment-intent-provision-0001' -Body '{"name":"Payment Intent Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' ([string]$provision.Status); $script:WorkspaceId = $provision.Body.workspaceId
    $roleId = Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:WorkspaceId)' AND Name='Workspace Owner'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','payments.read')"
    Add-Result 'fresh migration creates PaymentIntents' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='PaymentIntents'"))
    Add-Result 'order filter index exists' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.PaymentIntents') AND name='IX_PaymentIntents_WorkspaceId_OrderId'"))
    Add-Result 'no Payment Intent read-audit table' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='PaymentIntentReadAuditRecords'"))

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO payments.PaymentIntents
(WorkspaceId,PaymentIntentId,BuyerType,BuyerId,OrderId,InvoiceIdsJson,ScheduleLineIdsJson,Amount,Currency,MethodCode,ProviderCode,State,CheckoutUrl,ExpiresAt,FailureCode,Purpose,ResourceVersion,CreatedAt,UpdatedAt)
VALUES
('$($script:WorkspaceId)','$intentA','CONTACT','contact_intent_a','$orderA',N'["invoice_a","invoice_b"]',N'["schedule_line_a"]',150.250000,'USD','CARD','provider_alpha','REQUIRES_ACTION','https://payments.example.test/checkout/a','2026-09-30T12:00:00+00:00','ACTION_REQUIRED','DEPOSIT',7,'2026-08-29T10:00:00+00:00','2026-08-30T11:00:00+00:00'),
('$($script:WorkspaceId)','$intentB','ORGANIZATION_ACCOUNT','organization_intent_b',NULL,N'[]',N'[]',99.000000,'EUR','BANK_TRANSFER','provider_beta','SUCCEEDED',NULL,'2026-10-01T00:00:00+00:00',NULL,NULL,0,'2026-08-28T00:00:00+00:00','2026-08-28T01:00:00+00:00'),
('$foreignWorkspaceId','$intentForeign','CONTACT','contact_foreign','$orderA',N'["invoice_foreign"]',N'["schedule_foreign"]',999.000000,'USD','SECRET_METHOD','secret_provider','FAILED',NULL,'2026-10-01T00:00:00+00:00','FOREIGN_SECRET','OTHER',1,'2026-08-28T00:00:00+00:00','2026-08-28T01:00:00+00:00');
"@
    Set-PaymentScope $roleId 'Workspace'
    $list = Invoke-Intent '/payment-intents'; $detail = Invoke-Intent "/payment-intents/$intentA"; $status = Invoke-Intent "/payment-intents/$intentA/status"
    Add-Result 'WORKSPACE intent list succeeds' '200' ([string]$list.Status); Add-Result 'Workspace intent count' '2' ([string]$list.Body.Count)
    Add-Result 'intent detail succeeds' '200' ([string]$detail.Status); Add-Result 'intent status succeeds' '200' ([string]$status.Status)
    Add-Result 'foreign intent excluded' 'False' ([string]($list.Body.id -contains $intentForeign))
    Add-Result 'foreign persisted values never emitted' 'True' ([string](($list.Raw -notmatch 'FOREIGN_SECRET|secret_provider') -and ($detail.Raw -notmatch 'FOREIGN_SECRET|secret_provider')))
    $filtered = Invoke-Intent "/payment-intents?orderId=$orderA"
    Add-Result 'orderId exact filter returns one' '1' ([string]$filtered.Body.Count); Add-Result 'orderId exact filter identity' $intentA $filtered.Body.id
    Add-Result 'unknown order filter is empty' '0' ([string](Invoke-Intent '/payment-intents?orderId=order_unknown').Body.Count)
    Add-Result 'authorized malformed orderId rejected' '422' ([string](Invoke-Intent '/payment-intents?orderId=%20bad').Status)
    Add-Result 'authorized malformed detail identifier is nondisclosing' '404' ([string](Invoke-Intent '/payment-intents/%20bad').Status)
    Add-Result 'unknown detail is nondisclosing' '404' ([string](Invoke-Intent '/payment-intents/payment_intent_unknown').Status)
    Add-Result 'unknown status is nondisclosing' '404' ([string](Invoke-Intent '/payment-intents/payment_intent_unknown/status').Status)
    Add-Result 'foreign detail is nondisclosing' '404' ([string](Invoke-Intent "/payment-intents/$intentForeign").Status)
    Add-Result 'foreign status is nondisclosing' '404' ([string](Invoke-Intent "/payment-intents/$intentForeign/status").Status)

    Add-Result 'full intent exact shape' 'amount,buyerRef,checkoutUrl,createdAt,expiresAt,failureCode,id,invoiceIds,methodCode,orderId,providerCode,purpose,resourceVersion,scheduleLineIds,state,updatedAt,workspaceId' (($detail.Body.PSObject.Properties.Name|Sort-Object)-join ',')
    Add-Result 'status exact shape' 'failureCode,id,resourceVersion,state,updatedAt' (($status.Body.PSObject.Properties.Name|Sort-Object)-join ',')
    $minimal = @($list.Body|Where-Object id -eq $intentB)[0]
    Add-Result 'optional null fields omitted' 'amount,buyerRef,createdAt,expiresAt,id,invoiceIds,methodCode,providerCode,resourceVersion,scheduleLineIds,state,updatedAt,workspaceId' (($minimal.PSObject.Properties.Name|Sort-Object)-join ',')
    Add-Result 'buyer reference exact' 'CONTACT|contact_intent_a' "$($detail.Body.buyerRef.type)|$($detail.Body.buyerRef.id)"
    Add-Result 'money decimal string and currency exact' '150.25|USD' "$($detail.Body.amount.amount)|$($detail.Body.amount.currency)"
    Add-Result 'required ID arrays exact' 'invoice_a,invoice_b|schedule_line_a' "$(($detail.Body.invoiceIds)-join ',')|$(($detail.Body.scheduleLineIds)-join ',')"
    Add-Result 'state and purpose vocabularies exact' 'REQUIRES_ACTION|DEPOSIT' "$($detail.Body.state)|$($detail.Body.purpose)"
    Add-Result 'resourceVersion preserved' '7' ([string]$detail.Body.resourceVersion)
    Add-Result 'UTC timestamps emitted' 'True' ([string](($detail.Raw -match '"expiresAt":"[^"]+Z"') -and ($detail.Raw -match '"createdAt":"[^"]+Z"') -and ($detail.Raw -match '"updatedAt":"[^"]+Z"')))

    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='payments.read'"
    Add-Result 'missing capability denies list' '403' ([string](Invoke-Intent '/payment-intents').Status)
    Add-Result 'missing capability denies detail' '403' ([string](Invoke-Intent "/payment-intents/$intentA").Status)
    Add-Result 'missing capability denies status' '403' ([string](Invoke-Intent "/payment-intents/$intentA/status").Status)
    Add-Result 'capability denial precedes malformed query validation' '403' ([string](Invoke-Intent '/payment-intents?orderId=%20bad').Status)
    Add-Result 'capability denial precedes malformed path validation' '403' ([string](Invoke-Intent '/payment-intents/%20bad').Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','payments.read')"
    foreach($scope in @('Own','Team','Custom')) {
        Set-PaymentScope $roleId $scope
        Add-Result "$($scope.ToUpperInvariant()) intent list fails closed" '0' ([string](Invoke-Intent '/payment-intents').Body.Count)
        Add-Result "$($scope.ToUpperInvariant()) intent detail fails closed" '404' ([string](Invoke-Intent "/payment-intents/$intentA").Status)
        Add-Result "$($scope.ToUpperInvariant()) intent status fails closed" '404' ([string](Invoke-Intent "/payment-intents/$intentA/status").Status)
    }
    Set-PaymentScope $roleId 'Workspace'; Clear-IntentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_intent_read_invoice_ids','$roleId','payments','invoiceIds','Hidden')"
    Add-Result 'required full field fails list closed' '403' ([string](Invoke-Intent '/payment-intents').Status)
    Add-Result 'required full field fails detail closed' '403' ([string](Invoke-Intent "/payment-intents/$intentA").Status)
    Add-Result 'full-only field policy does not deny status' '200' ([string](Invoke-Intent "/payment-intents/$intentA/status").Status); Clear-IntentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_intent_read_checkout','$roleId','payments','checkoutUrl','Hidden')"
    $optionalHidden = Invoke-Intent "/payment-intents/$intentA"
    Add-Result 'optional full field omitted' 'True' ([string](($optionalHidden.Status -eq 200) -and ($optionalHidden.Raw -notmatch 'checkoutUrl')))
    Add-Result 'optional full field policy does not affect status' '200' ([string](Invoke-Intent "/payment-intents/$intentA/status").Status); Clear-IntentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_intent_read_failure','$roleId','payments','failureCode','Hidden')"
    Add-Result 'shared optional field omitted from full representation' 'True' ([string]((Invoke-Intent "/payment-intents/$intentA").Raw -notmatch 'failureCode'))
    Add-Result 'shared optional field omitted from status representation' 'True' ([string]((Invoke-Intent "/payment-intents/$intentA/status").Raw -notmatch 'failureCode')); Clear-IntentFields

    Add-Result 'missing Trusted Workspace rejected' '403' ([string](Invoke-Api -Method GET -Path '/payment-intents' -Token $script:Token).Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE payments.PaymentIntents SET InvoiceIdsJson=N'{`"not`":`"an-array`"}' WHERE WorkspaceId='$($script:WorkspaceId)' AND PaymentIntentId='$intentA'"
    $corruptDetail = Invoke-Intent "/payment-intents/$intentA"; $corruptList = Invoke-Intent '/payment-intents'
    Add-Result 'contract-invalid nested document fails detail closed' '500' ([string]$corruptDetail.Status)
    Add-Result 'contract-invalid nested document fails list closed' '500' ([string]$corruptList.Status)
    Add-Result 'corrupt detail emits no partial intent' 'True' ([string]($corruptDetail.Raw -notmatch $intentA))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE payments.PaymentIntents SET InvoiceIdsJson=N'[`"invoice_a`",`"invoice_b`"]' WHERE WorkspaceId='$($script:WorkspaceId)' AND PaymentIntentId='$intentA'"
    Add-Result 'healthy detail remains exact after corrupt fixture' '200' ([string](Invoke-Intent "/payment-intents/$intentA").Status)

    foreach($case in @(@('POST','/payment-intents'),@('PATCH',"/payment-intents/$intentA"),@('DELETE',"/payment-intents/$intentA"),@('POST',"/payment-intents/$intentA/capture"))) {
        Add-Result "no Payment Intent mutation $($case[0]) $($case[1])" 'True' ([string]((Invoke-Api -Method $case[0] -Path $case[1] -Token $script:Token -WorkspaceId $script:WorkspaceId -Body '{}').Status -in 404,405))
    }
    $paymentFiles = Get-ChildItem $paymentsRoot -Recurse -File | Where-Object Extension -in '.cs','.csproj'; $source = ($paymentFiles|ForEach-Object{Get-Content $_.FullName -Raw})-join "`n"
    Add-Result 'no foreign DbContext reference' 'True' ([string]($source -notmatch 'OrdersDbContext|InvoicesDbContext|QuotesDbContext|ProductsDbContext|CustomersDbContext|ShippingDbContext|FulfillmentDbContext'))
    Add-Result 'no provider runtime client' 'True' ([string]($source -notmatch 'HttpClient|ProviderClient|IPaymentProvider|Webhook'))
    Add-Result 'no workflow outbox idempotency or mutation implementation' 'True' ([string]($source -notmatch 'IWorkflow|Outbox|Idempotenc|MapPost|MapPatch|MapDelete'))
    $routeSource = Get-Content (Join-Path $paymentsRoot 'Contracts/PaymentIntentEndpoints.cs') -Raw
    Add-Result 'exactly three admitted Payment Intent GET routes' '3' ([string]([regex]::Matches($routeSource,'\.MapGet\(').Count))
    $persistenceSource = Get-Content (Join-Path $paymentsRoot 'Infrastructure/Persistence/EfPaymentsPersistence.cs') -Raw
    Add-Result 'no invented Payment Intent ordering or pagination' 'True' ([string]($persistenceSource -notmatch 'PaymentIntents[\s\S]{0,600}(OrderBy|Skip|Take)'))
}
catch { $script:Failed++; [void]$script:Results.Add("FAIL | verifier execution | $($_.Exception.Message)"); if(Test-Path $logPath){[void]$script:Results.Add((Get-Content $logPath -Tail 40)-join "`n")} }
finally {
    if($null -ne $hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id -Force; $hostProcess.WaitForExit()}
    if(-not $KeepDatabase){try{Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;"}catch{}}
    Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
}
$script:Results|ForEach-Object{Write-Host $_}
Write-Host "PAYMENT INTENT READ CORE RESULT: PASS=$script:Passed FAIL=$script:Failed"
if($script:Failed -gt 0){exit 1}
