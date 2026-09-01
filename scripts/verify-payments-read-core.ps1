<#
.SYNOPSIS
    Verifies the owner-local PaymentPlan read core against an isolated database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5357,
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
    $command = $null
    $connection.Open()
    try { $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120; [void]$command.ExecuteNonQuery() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose() }
}
function Get-Scalar([string] $Query, [string] $Database) {
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database)
    $command = $null
    $connection.Open()
    try { $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120; return $command.ExecuteScalar() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose() }
}
function New-RequestId { $script:RequestCounter++; 'req-payments-read-{0:d6}' -f $script:RequestCounter }
function Invoke-Api {
    param([string] $Method, [string] $Path, [string] $Body, [string] $Token, [string] $WorkspaceId, [string] $IdempotencyKey, [string] $RequestId)
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ([string]::IsNullOrWhiteSpace($RequestId)) { [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId)) }
    elseif ($RequestId -ne 'omit') { [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId) }
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-payments-read-core-0001')
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
function Invoke-Payments([string] $Path) { Invoke-Api -Method GET -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId }
function Set-PaymentScope([string] $RoleId, [string] $Scope) {
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
DELETE FROM access.RoleDataScopes WHERE PolicyId = 'scope_payments_read_core';
INSERT INTO access.RoleDataScopes (PolicyId, RoleId, ResourceKey, Scope, AllowedOwnerIdsJson)
VALUES ('scope_payments_read_core', '$RoleId', 'payments', '$Scope', '[]');
"@
}
function Clear-PaymentFields { Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_payments_read_%'" }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$billingRoot = Join-Path $repositoryRoot 'src/UnicoreCRM.Billing/Payments'
$email = 'payments.read.provisioned@example.test'
$password = 'Payments-Read-Core!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-payments-read-$([Guid]::NewGuid().ToString('N')).log")
$planA = 'payment_plan_read_a'; $planB = 'payment_plan_read_b'; $planForeign = 'payment_plan_read_foreign'
$lineA = 'payment_schedule_line_a'; $lineB = 'payment_schedule_line_b'; $lineForeign = 'payment_schedule_line_foreign'
$orderA = 'order_payment_read_a'; $orderB = 'order_payment_read_b'; $foreignWorkspaceId = 'ws_payments_read_foreign'

try {
    Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;
CREATE DATABASE [$DatabaseName];
"@
    $env:ASPNETCORE_ENVIRONMENT = 'Development'; $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = New-ConnectionString $DatabaseName; $env:Development__ApplyMigrations = 'true'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'; $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'; $env:IdentityAuth__DevelopmentBootstrap__Email = $email
    $env:IdentityAuth__DevelopmentBootstrap__Password = $password; $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Payments Read Fixture'
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

    Add-Result 'unauthenticated payment plans rejected' '401' ([string](Invoke-Api -Method GET -Path '/payment-plans' -WorkspaceId 'ws_unknown').Status)
    Add-Result 'unauthenticated schedule lines rejected' '401' ([string](Invoke-Api -Method GET -Path '/payment-schedule-lines' -WorkspaceId 'ws_unknown').Status)
    $signIn = Invoke-Api -Method POST -Path '/auth/sessions' -IdempotencyKey 'idem-payments-read-signin-0001' -Body (@{ email=$email; password=$password } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed: $($signIn.Raw)" }; $script:Token = $signIn.Body.accessToken
    $provision = Invoke-Api -Method POST -Path '/workspaces/initial-provisioning' -Token $script:Token -IdempotencyKey 'idem-payments-read-provision-0001' -Body '{"name":"Payments Read Workspace"}'
    Add-Result 'initial Workspace provisioning succeeds' '201' ([string]$provision.Status); $script:WorkspaceId = $provision.Body.workspaceId
    $roleId = Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId = '$($script:WorkspaceId)' AND Name = 'Workspace Owner'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId', 'payments.plan.read')"
    Add-Result 'fresh migration creates PaymentPlans' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='PaymentPlans'"))
    Add-Result 'fresh migration creates PaymentScheduleLines' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='PaymentScheduleLines'"))
    Add-Result 'frozen READ_ACCESS_LOG read audit table present' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='ReadAuditRecords'"))
    Add-Result 'PaymentPlan order filter index exists' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.PaymentPlans') AND name='IX_PaymentPlans_WorkspaceId_OrderId'"))
    Add-Result 'ScheduleLine plan filter index exists' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.PaymentScheduleLines') AND name='IX_PaymentScheduleLines_WorkspaceId_PaymentPlanId'"))
    Add-Result 'no speculative PaymentPlan state index' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.PaymentPlans') AND name='IX_PaymentPlans_WorkspaceId_State'"))
    Add-Result 'no speculative ScheduleLine order index' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.PaymentScheduleLines') AND name='IX_PaymentScheduleLines_WorkspaceId_OrderId'"))

    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO payments.PaymentPlans
(WorkspaceId,PaymentPlanId,OrderId,BuyerType,BuyerId,Kind,State,Currency,AgreementSnapshotJson,ScheduleLineIdsJson,SupersedesPlanId,SupersededByPlanId,EvidenceCount,ResourceVersion,CreatedAt,UpdatedAt,ActivatedAt,CompletedAt,CancelledAt)
VALUES
('$($script:WorkspaceId)','$planA','$orderA','CONTACT','contact_payment_a','DEPOSIT_AND_BALANCE','ACTIVE','USD',N'{"version":3,"kind":"DEPOSIT_AND_BALANCE","currency":"USD","lines":[{"id":"agreement_line_a","sequence":1,"label":"Deposit","purpose":"DEPOSIT","amountRule":{"type":"PERCENTAGE","percentage":"25"},"previewAmount":{"amount":"25.5","currency":"USD"},"dueRule":{"type":"FIXED_DATE","date":"2026-09-01"},"allowedMethodCodes":["BANK_TRANSFER"],"preferredMethodCode":"BANK_TRANSFER","channel":"BANK","fulfillmentGate":"BEFORE_BOOKING","invoicePolicyCode":"STANDARD"}],"acceptedAt":"2026-08-30T00:00:00Z","sourceQuoteId":"quote_scalar_a","policyVersion":"policy-v1"}',N'["$lineA"]',NULL,NULL,2,3,'2026-08-29T00:00:00+00:00','2026-08-30T00:00:00+00:00','2026-08-30T00:00:00+00:00',NULL,NULL),
('$($script:WorkspaceId)','$planB','$orderB','ORGANIZATION_ACCOUNT','organization_payment_b','FULL_PAYMENT','DRAFT','EUR',N'{"version":0,"kind":"FULL_PAYMENT","currency":"EUR","lines":[{"id":"agreement_line_b","sequence":1,"label":"Full payment","purpose":"FULL","amountRule":{"type":"FIXED","amount":{"amount":"120","currency":"EUR"}},"previewAmount":{"amount":"120","currency":"EUR"},"dueRule":{"type":"EVENT_RELATIVE","event":"ORDER_CONFIRMED","offsetDays":0,"dayBasis":"CALENDAR"},"allowedMethodCodes":["CARD"],"fulfillmentGate":"NONE"}]}',N'["$lineB"]',NULL,NULL,0,0,'2026-08-28T00:00:00+00:00','2026-08-28T00:00:00+00:00',NULL,NULL,NULL),
('$foreignWorkspaceId','$planForeign','$orderA','CONTACT','contact_foreign','CUSTOM','DRAFT','USD',N'{"version":0,"kind":"CUSTOM","currency":"USD","lines":[{"id":"agreement_foreign","sequence":1,"label":"Foreign secret","purpose":"OTHER","amountRule":{"type":"REMAINDER"},"previewAmount":{"amount":"1","currency":"USD"},"dueRule":{"type":"MILESTONE","milestoneCode":"FOREIGN"},"allowedMethodCodes":["FOREIGN"],"fulfillmentGate":"NONE"}]}',N'["$lineForeign"]',NULL,NULL,0,0,'2026-08-28T00:00:00+00:00','2026-08-28T00:00:00+00:00',NULL,NULL,NULL);
INSERT INTO payments.PaymentScheduleLines
(WorkspaceId,PaymentScheduleLineId,PaymentPlanId,PaymentPlanVersion,OrderId,BuyerType,BuyerId,Sequence,Label,Purpose,AmountRuleJson,Amount,AmountCurrency,DueRuleJson,ResolvedDueDate,AllowedMethodCodesJson,PreferredMethodCode,Channel,FulfillmentGate,InvoicePolicyCode,State,SatisfiedAmount,SatisfiedCurrency,OutstandingAmount,OutstandingCurrency,ResourceVersion,CreatedAt,UpdatedAt)
VALUES
('$($script:WorkspaceId)','$lineA','$planA',3,'$orderA','CONTACT','contact_payment_a',1,'Deposit','DEPOSIT',N'{"type":"PERCENTAGE","percentage":"25"}',25.500000,'USD',N'{"type":"FIXED_DATE","date":"2026-09-01"}','2026-09-01',N'["BANK_TRANSFER"]','BANK_TRANSFER','BANK','BEFORE_BOOKING','STANDARD','DUE',5.250000,'USD',20.250000,'USD',4,'2026-08-29T00:00:00+00:00','2026-08-30T00:00:00+00:00'),
('$($script:WorkspaceId)','$lineB','$planB',0,'$orderB','ORGANIZATION_ACCOUNT','organization_payment_b',1,'Full payment','FULL',N'{"type":"FIXED","amount":{"amount":"120","currency":"EUR"}}',120.000000,'EUR',N'{"type":"EVENT_RELATIVE","event":"ORDER_CONFIRMED","offsetDays":0,"dayBasis":"CALENDAR"}',NULL,N'["CARD"]',NULL,NULL,'NONE',NULL,'SCHEDULED',0.000000,'EUR',120.000000,'EUR',0,'2026-08-28T00:00:00+00:00','2026-08-28T00:00:00+00:00'),
('$foreignWorkspaceId','$lineForeign','$planForeign',0,'$orderA','CONTACT','contact_foreign',1,'Foreign secret','OTHER',N'{"type":"REMAINDER"}',1.000000,'USD',N'{"type":"MILESTONE","milestoneCode":"FOREIGN"}',NULL,N'["FOREIGN"]',NULL,NULL,'NONE',NULL,'SCHEDULED',0.000000,'USD',1.000000,'USD',0,'2026-08-28T00:00:00+00:00','2026-08-28T00:00:00+00:00');
"@
    Set-PaymentScope $roleId 'Workspace'
    $plans = Invoke-Payments '/payment-plans'; $lines = Invoke-Payments '/payment-schedule-lines'
    Add-Result 'WORKSPACE payment plans succeeds' '200' ([string]$plans.Status); Add-Result 'Workspace plan count' '2' ([string]$plans.Body.Count)
    Add-Result 'WORKSPACE schedule lines succeeds' '200' ([string]$lines.Status); Add-Result 'Workspace schedule count' '2' ([string]$lines.Body.Count)
    Add-Result 'foreign plan excluded' 'False' ([string]($plans.Body.id -contains $planForeign)); Add-Result 'foreign line excluded' 'False' ([string]($lines.Body.id -contains $lineForeign))
    Add-Result 'foreign persisted value never emitted' 'True' ([string](($plans.Raw -notmatch 'Foreign secret') -and ($lines.Raw -notmatch 'Foreign secret')))
    $filteredPlan = Invoke-Payments "/payment-plans?orderId=$orderA"; $filteredLine = Invoke-Payments "/payment-schedule-lines?planId=$planA"
    Add-Result 'orderId exact filter' $planA $filteredPlan.Body.id; Add-Result 'planId exact filter' $lineA $filteredLine.Body.id
    Add-Result 'unknown order filter is empty' '0' ([string](Invoke-Payments '/payment-plans?orderId=order_unknown').Body.Count)
    Add-Result 'foreign plan filter is empty' '0' ([string](Invoke-Payments "/payment-schedule-lines?planId=$planForeign").Body.Count)
    Add-Result 'authorized invalid orderId rejected' '422' ([string](Invoke-Payments '/payment-plans?orderId=%20bad').Status)
    Add-Result 'authorized invalid planId rejected' '422' ([string](Invoke-Payments '/payment-schedule-lines?planId=%20bad').Status)
    $planDoc = @($plans.Body | Where-Object id -eq $planA)[0]; $lineDoc = @($lines.Body | Where-Object id -eq $lineA)[0]
    Add-Result 'plan exact top-level shape' 'activatedAt,agreementSnapshot,buyerRef,createdAt,currency,evidenceCount,id,kind,orderId,resourceVersion,scheduleLineIds,state,updatedAt,workspaceId' (($planDoc.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'schedule exact top-level shape' 'allowedMethodCodes,amount,amountRule,buyerRef,channel,createdAt,dueRule,fulfillmentGate,id,invoicePolicyCode,label,orderId,outstandingAmount,planId,planVersion,preferredMethodCode,purpose,resolvedDueDate,resourceVersion,satisfiedAmount,sequence,state,updatedAt,workspaceId' (($lineDoc.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'money decimal string preserved' '25.5' $lineDoc.amount.amount; Add-Result 'currency preserved' 'USD' $lineDoc.amount.currency
    Add-Result 'resource version preserved' '4' ([string]$lineDoc.resourceVersion); Add-Result 'UTC timestamp emitted' 'True' ([string]($lines.Raw -match '"updatedAt":"[^"]+Z"'))
    Add-Result 'agreement enum and nested rules exact' 'DEPOSIT_AND_BALANCE|PERCENTAGE|FIXED_DATE' "$($planDoc.agreementSnapshot.kind)|$($planDoc.agreementSnapshot.lines[0].amountRule.type)|$($planDoc.agreementSnapshot.lines[0].dueRule.type)"

    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='payments.plan.read'"
    Add-Result 'missing capability denies plans' '403' ([string](Invoke-Payments '/payment-plans').Status); Add-Result 'missing capability denies lines' '403' ([string](Invoke-Payments '/payment-schedule-lines').Status)
    Add-Result 'missing capability precedes malformed orderId validation' '403' ([string](Invoke-Payments '/payment-plans?orderId=%20bad').Status)
    Add-Result 'missing capability precedes malformed planId validation' '403' ([string](Invoke-Payments '/payment-schedule-lines?planId=%20bad').Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('$roleId','payments.plan.read')"
    foreach ($scope in @('Own','Team','Custom')) { Set-PaymentScope $roleId $scope; Add-Result "$($scope.ToUpperInvariant()) plans fail closed" '0' ([string](Invoke-Payments '/payment-plans').Body.Count); Add-Result "$($scope.ToUpperInvariant()) lines fail closed" '0' ([string](Invoke-Payments '/payment-schedule-lines').Body.Count) }
    Set-PaymentScope $roleId 'Workspace'; Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_optional','$roleId','payments','workspaceId','Hidden')"
    Add-Result 'optional hidden plan field omitted' 'True' ([string]((Invoke-Payments '/payment-plans').Raw -notmatch 'workspaceId')); Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_required','$roleId','payments','state','Hidden')"
    Add-Result 'required hidden field fails plans closed' '403' ([string](Invoke-Payments '/payment-plans').Status); Add-Result 'required hidden field fails lines closed' '403' ([string](Invoke-Payments '/payment-schedule-lines').Status); Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_plan_id','$roleId','payments','planId','Hidden')"
    Add-Result 'schedule-only required policy does not deny plans' '200' ([string](Invoke-Payments '/payment-plans').Status)
    Add-Result 'schedule-only required policy fails schedule lines closed' '403' ([string](Invoke-Payments '/payment-schedule-lines').Status); Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_kind','$roleId','payments','kind','Hidden')"
    Add-Result 'plan-only required policy fails plans closed' '403' ([string](Invoke-Payments '/payment-plans').Status)
    Add-Result 'plan-only required policy does not deny schedule lines' '200' ([string](Invoke-Payments '/payment-schedule-lines').Status); Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_activated','$roleId','payments','activatedAt','Hidden')"
    $planOptionalHidden = Invoke-Payments '/payment-plans'; $scheduleUnaffectedByPlanOptional = Invoke-Payments '/payment-schedule-lines'
    Add-Result 'plan-only optional field is omitted from plans' 'True' ([string](($planOptionalHidden.Status -eq 200) -and ($planOptionalHidden.Raw -notmatch 'activatedAt')))
    Add-Result 'plan-only optional policy does not affect schedule lines' '200' ([string]$scheduleUnaffectedByPlanOptional.Status); Clear-PaymentFields
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity (PolicyId,RoleId,ResourceKey,FieldKey,Access) VALUES ('field_payments_read_channel','$roleId','payments','channel','Hidden')"
    $scheduleOptionalHidden = Invoke-Payments '/payment-schedule-lines'; $plansUnaffectedByScheduleOptional = Invoke-Payments '/payment-plans'
    Add-Result 'schedule-only optional field is omitted from schedule lines' 'True' ([string](($scheduleOptionalHidden.Status -eq 200) -and ($scheduleOptionalHidden.Raw -notmatch '"channel"')))
    Add-Result 'schedule-only optional policy does not affect plans' '200' ([string]$plansUnaffectedByScheduleOptional.Status); Clear-PaymentFields
    Add-Result 'missing trusted Workspace rejected' '403' ([string](Invoke-Api -Method GET -Path '/payment-plans' -Token $script:Token).Status)
    foreach ($case in @(@('POST','/payment-plans'),@('PATCH',"/payment-plans/$planA"),@('DELETE',"/payment-plans/$planA"),@('POST','/payment-schedule-lines'))) {
        $mutationStatus = (Invoke-Api -Method $case[0] -Path $case[1] -Token $script:Token -WorkspaceId $script:WorkspaceId -Body '{}').Status
        Add-Result "no Payment mutation $($case[0]) $($case[1])" 'True' ([string]($mutationStatus -in 404,405))
    }
    $paymentFiles = Get-ChildItem $billingRoot -Recurse -File | Where-Object Extension -in '.cs','.csproj'; $source = ($paymentFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    Add-Result 'no foreign DbContext reference' 'True' ([string]($source -notmatch 'OrdersDbContext|QuotesDbContext|ProductsDbContext|CustomersDbContext|ShippingDbContext|FulfillmentDbContext'))
    Add-Result 'no workflow or mutation implementation' 'True' ([string]($source -notmatch 'IWorkflow|Outbox|MapPost|MapPatch|MapDelete'))
    # SaveChanges is admitted only for the frozen read-evidence append, nowhere else in Payments.
    $saveChangesFiles=(Get-ChildItem $billingRoot -Recurse -File -Filter *.cs|Where-Object{(Get-Content $_.FullName -Raw)-match'SaveChangesAsync'}|ForEach-Object Name|Sort-Object)-join','
    Add-Result 'SaveChanges confined to read-audit append' 'EfPaymentsPersistence.cs,PaymentReadAudit.cs,PaymentsApplication.cs' $saveChangesFiles
    $routeSource = Get-Content (Join-Path $billingRoot 'Contracts/PaymentsEndpoints.cs') -Raw
    Add-Result 'exactly two admitted Payment GET routes' '2' ([string]([regex]::Matches($routeSource,'\.MapGet\(').Count))
}
catch { $script:Failed++; [void]$script:Results.Add("FAIL | verifier execution | $($_.Exception.Message)"); if (Test-Path $logPath) { [void]$script:Results.Add((Get-Content $logPath -Tail 40) -join "`n") } }
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force; $hostProcess.WaitForExit() }
    if (-not $KeepDatabase) { try { Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;" } catch { } }
    Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "PAYMENTS READ CORE RESULT: PASS=$script:Passed FAIL=$script:Failed"
if ($script:Failed -gt 0) { exit 1 }
