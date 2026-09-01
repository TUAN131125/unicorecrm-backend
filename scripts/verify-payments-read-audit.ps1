<# .SYNOPSIS Verifies Payments-owned READ_ACCESS_LOG read-audit conformance against an isolated database and real ApiHost. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_]{1,128}$')][string]$DatabaseName,
    [string]$SqlServer='(localdb)\MSSQLLocalDB',[int]$Port=5363,[int]$ReadyTimeoutSeconds=420,[switch]$KeepDatabase)
$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Net.Http
$script:Passed=0;$script:Failed=0;$script:Results=New-Object System.Collections.ArrayList;$script:Counter=0
$script:BaseUrl="http://127.0.0.1:$Port";$script:Token=$null;$script:WorkspaceId=$null;$script:MemberId=$null;$script:LastRequestId=$null
function Add-Result([string]$Name,[string]$Expected,[string]$Actual){if($Expected-eq$Actual){$script:Passed++;[void]$script:Results.Add("PASS | $Name | $Actual")}else{$script:Failed++;[void]$script:Results.Add("FAIL | $Name | expected=$Expected actual=$Actual")}}
function New-ConnectionString([string]$Database){"Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"}
function Invoke-SqlNonQuery([string]$Query,[string]$Database='master'){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;[void]$cmd.ExecuteNonQuery()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function Get-Scalar([string]$Query,[string]$Database){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;return $cmd.ExecuteScalar()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function New-RequestId{$script:Counter++;$script:LastRequestId='req-pay-audit-{0:d6}'-f$script:Counter;$script:LastRequestId}
function Invoke-Api{param([string]$Method,[string]$Path,[string]$Body,[string]$Token,[string]$WorkspaceId,[string]$IdempotencyKey)
    $req=New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method),"$script:BaseUrl$Path");[void]$req.Headers.TryAddWithoutValidation('X-Request-Id',(New-RequestId));[void]$req.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-payments-read-audit-0001')
    if($Token){[void]$req.Headers.TryAddWithoutValidation('Authorization',"Bearer $Token")};if($WorkspaceId){[void]$req.Headers.TryAddWithoutValidation('X-Workspace-Id',$WorkspaceId)};if($IdempotencyKey){[void]$req.Headers.TryAddWithoutValidation('Idempotency-Key',$IdempotencyKey)};if($Body){$req.Content=New-Object System.Net.Http.StringContent($Body,[Text.Encoding]::UTF8,'application/json')}
    $h=New-Object System.Net.Http.HttpClientHandler;$h.UseProxy=$false;$h.AllowAutoRedirect=$false;$client=New-Object System.Net.Http.HttpClient($h,$true);$client.Timeout=[TimeSpan]::FromSeconds(60);$resp=$null
    try{$resp=$client.SendAsync($req).GetAwaiter().GetResult();$raw=$resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();$status=[int]$resp.StatusCode}finally{if($resp){$resp.Dispose()};$client.Dispose();$req.Dispose()};$payload=$null;if($raw){try{$payload=$raw|ConvertFrom-Json}catch{}};[pscustomobject]@{Status=$status;Body=$payload;Raw=$raw}}
function Invoke-Read([string]$Path){Invoke-Api -Method GET -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId}
function Get-AuditCount{[int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords")}
function Get-RecordDecisionCount{[int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey='payments'")}
# Issues one read and reports "<httpStatus>|<ownerAuditDelta>".
function Measure-Read([string]$Path){$b=Get-AuditCount;$r=Invoke-Read $Path;$d=(Get-AuditCount)-$b;[pscustomobject]@{Status=$r.Status;Delta=$d;Body=$r.Body;Raw=$r.Raw;Probe="$($r.Status)|$d"}}
function Set-Scope([string]$RoleId,[string]$Scope){Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_payments_audit'; INSERT INTO access.RoleDataScopes(PolicyId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES('scope_payments_audit','$RoleId','payments','$Scope','[]');"}
$root=(Resolve-Path(Join-Path $PSScriptRoot '..')).Path;$hostProject=Join-Path $root 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj';$paymentsRoot=Join-Path $root 'src/UnicoreCRM.Billing/Payments'
$email='payments.read.audit@example.test';$password='Payments-Read-Audit!2026';$hostProcess=$null;$logPath=Join-Path([IO.Path]::GetTempPath())("unicore-payments-audit-$([Guid]::NewGuid().ToString('N')).log")
$planA='plan_audit_a';$planB='plan_audit_b';$lineA='line_audit_a';$intentA='intent_audit_a';$intentB='intent_audit_b';$recordA='record_audit_a';$recordB='record_audit_b'
$intentForeign='intent_audit_foreign';$recordForeign='record_audit_foreign';$foreignWorkspace='ws_payments_audit_foreign'
try{
    Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];"
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_URLS=$script:BaseUrl;$env:ConnectionStrings__UnicoreCRM=New-ConnectionString $DatabaseName;$env:Development__ApplyMigrations='true';$env:UNICORE_DEV_SEED_ENABLED='false';$env:IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog';$env:IdentityAuth__DevelopmentBootstrap__Enabled='true';$env:IdentityAuth__DevelopmentBootstrap__Email=$email;$env:IdentityAuth__DevelopmentBootstrap__Password=$password;$env:IdentityAuth__DevelopmentBootstrap__DisplayName='Payments Audit Fixture';$env:Workspace__DevelopmentBootstrap__Enabled='false';$env:AccessControl__DevelopmentBootstrap__Enabled='false';$env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false';$env:AI__Provider__Kind='DevelopmentDeterministic'
    $hostProcess=Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$hostProject)-PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err";$ready=$false
    for($i=0;$i-lt$ReadyTimeoutSeconds;$i++){Start-Sleep -Seconds 1;if($hostProcess.HasExited){throw "ApiHost exited $($hostProcess.ExitCode). See $logPath"};try{if((Invoke-Api -Method GET -Path '/auth/session').Status-gt 0){$ready=$true;break}}catch{}};if(-not$ready){throw "ApiHost not ready. See $logPath"}
    $signIn=Invoke-Api -Method POST -Path '/auth/sessions' -IdempotencyKey 'idem-pay-audit-signin-0001' -Body(@{email=$email;password=$password}|ConvertTo-Json -Compress);if($signIn.Status-ne 200){throw "Sign-in failed: $($signIn.Raw)"};$script:Token=$signIn.Body.accessToken
    $prov=Invoke-Api -Method POST -Path '/workspaces/initial-provisioning' -Token $script:Token -IdempotencyKey 'idem-pay-audit-prov-0001' -Body '{"name":"Payments Audit Workspace"}';Add-Result 'Workspace provisioning succeeds' '201' ([string]$prov.Status);$script:WorkspaceId=$prov.Body.workspaceId
    $roleId=Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:WorkspaceId)' AND Name='Workspace Owner'"
    $script:MemberId=Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 MemberId FROM workspace.Memberships WHERE WorkspaceId='$($script:WorkspaceId)'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','payments.read'),('$roleId','payments.plan.read')"

    Add-Result 'payments.ReadAuditRecords table exists' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='ReadAuditRecords'"))
    Add-Result 'read-audit columns exact' 'ActorId,AuditId,CorrelationId,OccurredAt,Operation,Outcome,RecordId,RequestId,ResourceVersion,WorkspaceId' ((Get-Scalar -Database $DatabaseName -Query "SELECT STRING_AGG(COLUMN_NAME,',') WITHIN GROUP (ORDER BY COLUMN_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='payments' AND TABLE_NAME='ReadAuditRecords'"))
    Add-Result 'read-audit Workspace-leading index' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('payments.ReadAuditRecords') AND name='IX_ReadAuditRecords_WorkspaceId_OccurredAt'"))
    Add-Result 'read-audit has no foreign key' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('payments.ReadAuditRecords')"))

    $agreementA='{"version":3,"kind":"FULL_PAYMENT","currency":"USD","lines":[{"id":"agreement_line_a","sequence":1,"label":"Deposit","purpose":"DEPOSIT","amountRule":{"type":"FIXED","amount":{"amount":"100","currency":"USD"}},"previewAmount":{"amount":"100","currency":"USD"},"dueRule":{"type":"FIXED_DATE","date":"2026-09-30"},"allowedMethodCodes":["CARD"],"fulfillmentGate":"NONE"}]}'
    $agreementB='{"version":1,"kind":"INSTALLMENT","currency":"USD","lines":[{"id":"agreement_line_b","sequence":1,"label":"Installment","purpose":"INSTALLMENT","amountRule":{"type":"REMAINDER"},"previewAmount":{"amount":"50","currency":"USD"},"dueRule":{"type":"MILESTONE","milestoneCode":"M1"},"allowedMethodCodes":["CARD"],"fulfillmentGate":"NONE"}]}'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO payments.PaymentPlans(WorkspaceId,PaymentPlanId,OrderId,BuyerType,BuyerId,Kind,State,Currency,AgreementSnapshotJson,ScheduleLineIdsJson,SupersedesPlanId,SupersededByPlanId,EvidenceCount,ResourceVersion,CreatedAt,UpdatedAt,ActivatedAt,CompletedAt,CancelledAt) VALUES
('$($script:WorkspaceId)','$planA','order_audit_a','CONTACT','contact_audit_a','FULL_PAYMENT','ACTIVE','USD',N'$agreementA',N'["$lineA"]',NULL,NULL,0,3,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00',NULL,NULL,NULL),
('$($script:WorkspaceId)','$planB','order_audit_b','CONTACT','contact_audit_b','INSTALLMENT','DRAFT','USD',N'$agreementB',N'[]',NULL,NULL,0,1,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00',NULL,NULL,NULL);
INSERT INTO payments.PaymentScheduleLines(WorkspaceId,PaymentScheduleLineId,PaymentPlanId,PaymentPlanVersion,OrderId,BuyerType,BuyerId,Sequence,Label,Purpose,AmountRuleJson,Amount,AmountCurrency,DueRuleJson,ResolvedDueDate,AllowedMethodCodesJson,PreferredMethodCode,Channel,FulfillmentGate,InvoicePolicyCode,State,SatisfiedAmount,SatisfiedCurrency,OutstandingAmount,OutstandingCurrency,ResourceVersion,CreatedAt,UpdatedAt) VALUES
('$($script:WorkspaceId)','$lineA','$planA',3,'order_audit_a','CONTACT','contact_audit_a',1,'Deposit','DEPOSIT',N'{"type":"FIXED","amount":{"amount":"100","currency":"USD"}}',100.000000,'USD',N'{"type":"FIXED_DATE","date":"2026-09-30"}','2026-09-30',N'["CARD"]',NULL,NULL,'NONE',NULL,'DUE',0.000000,'USD',100.000000,'USD',2,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00');
INSERT INTO payments.PaymentIntents(WorkspaceId,PaymentIntentId,BuyerType,BuyerId,OrderId,InvoiceIdsJson,ScheduleLineIdsJson,Amount,Currency,MethodCode,ProviderCode,State,CheckoutUrl,ExpiresAt,FailureCode,Purpose,ResourceVersion,CreatedAt,UpdatedAt) VALUES
('$($script:WorkspaceId)','$intentA','CONTACT','contact_audit_a','order_audit_a',N'[]',N'["$lineA"]',100.000000,'USD','CARD','provider_alpha','CREATED',NULL,'2026-09-30T00:00:00+00:00',NULL,NULL,5,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00'),
('$($script:WorkspaceId)','$intentB','CONTACT','contact_audit_b',NULL,N'[]',N'[]',50.000000,'EUR','BANK','provider_beta','PROCESSING',NULL,'2026-09-30T00:00:00+00:00',NULL,NULL,2,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00'),
('$foreignWorkspace','$intentForeign','CONTACT','contact_audit_a',NULL,N'[]',N'[]',999.000000,'USD','SECRET','secret_provider','FAILED',NULL,'2026-09-30T00:00:00+00:00',NULL,NULL,1,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00');
INSERT INTO payments.PaymentRecords(WorkspaceId,PaymentRecordId,BuyerType,BuyerId,OrderId,PaymentIntentId,Kind,State,Amount,Currency,MethodCode,Channel,ProviderCode,RefundOfPaymentRecordId,RefundOfCustomerCreditId,RefundIntentId,OccurredAt,ExternalReference,EvidenceJson,ReconciliationState,CodCustomerCollectionState,CodMerchantRemittanceState,EffectiveForReceivables,ResourceVersion,CreatedAt,UpdatedAt,AllocationsJson,RefundsJson,CustomerCreditsJson,UnallocatedAmount,UnallocatedCurrency,RefundableAmount,RefundableCurrency) VALUES
('$($script:WorkspaceId)','$recordA','CONTACT','contact_audit_a',NULL,NULL,'PAYMENT','SUCCEEDED',100.000000,'USD','CARD','ONLINE_GATEWAY',NULL,NULL,NULL,NULL,'2026-08-30T10:00:00+00:00',NULL,NULL,'MATCHED',NULL,NULL,1,9,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00',N'[]',N'[]',N'[]',0.000000,'USD',100.000000,'USD'),
('$($script:WorkspaceId)','$recordB','CONTACT','contact_audit_b',NULL,NULL,'PAYMENT','PENDING',50.000000,'EUR','BANK','BANK',NULL,NULL,NULL,NULL,'2026-08-30T10:00:00+00:00',NULL,NULL,'UNRECONCILED',NULL,NULL,0,1,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00',N'[]',N'[]',N'[]',50.000000,'EUR',50.000000,'EUR'),
('$foreignWorkspace','$recordForeign','CONTACT','contact_audit_a',NULL,NULL,'PAYMENT','FAILED',999.000000,'USD','SECRET','EXTERNAL',NULL,NULL,NULL,NULL,'2026-08-30T10:00:00+00:00',NULL,NULL,'MISMATCH',NULL,NULL,0,1,'2026-08-30T10:00:00+00:00','2026-08-30T10:00:00+00:00',N'[]',N'[]',N'[]',999.000000,'USD',999.000000,'USD');
"@
    Set-Scope $roleId 'Workspace'

    # 1-5. Successful list reads write exactly one owner row each.
    Add-Result 'listPaymentPlans        => 200 and +1' '200|1' (Measure-Read '/payment-plans').Probe
    Add-Result 'listPaymentScheduleLines=> 200 and +1' '200|1' (Measure-Read '/payment-schedule-lines').Probe
    Add-Result 'listPaymentIntents      => 200 and +1' '200|1' (Measure-Read '/payment-intents').Probe
    Add-Result 'listPaymentRecords      => 200 and +1' '200|1' (Measure-Read '/payments').Probe

    # 2. Empty successful result still writes exactly one row.
    $empty=Measure-Read '/payment-plans?orderId=order_never_used'
    Add-Result 'empty listPaymentPlans  => 200 and +1' '200|1' $empty.Probe
    Add-Result 'empty list really empty' '0' ([string]$empty.Body.Count)

    # 9. Multi-row list writes one row, not one per entity.
    $multi=Measure-Read '/payments'
    Add-Result 'multi-row list returns 2 rows' '2' ([string]$multi.Body.Count)
    Add-Result 'multi-row list writes exactly 1 audit' '1' ([string]$multi.Delta)

    # 6-8. Successful resource reads write one row with recordId and resourceVersion.
    Add-Result 'getPaymentIntent        => 200 and +1' '200|1' (Measure-Read "/payment-intents/$intentA").Probe
    Add-Result 'getPaymentIntent recordId+version' "$intentA|5" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(RecordId,'|',ResourceVersion) FROM payments.ReadAuditRecords WHERE Operation='getPaymentIntent' ORDER BY OccurredAt DESC"))
    Add-Result 'getPaymentIntentStatus  => 200 and +1' '200|1' (Measure-Read "/payment-intents/$intentA/status").Probe
    Add-Result 'getPaymentIntentStatus recordId+version' "$intentA|5" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(RecordId,'|',ResourceVersion) FROM payments.ReadAuditRecords WHERE Operation='getPaymentIntentStatus' ORDER BY OccurredAt DESC"))
    Add-Result 'getPaymentRecordDetail  => 200 and +1' '200|1' (Measure-Read "/payments/$recordA/detail").Probe
    Add-Result 'getPaymentRecordDetail recordId+version' "$recordA|9" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(RecordId,'|',ResourceVersion) FROM payments.ReadAuditRecords WHERE Operation='getPaymentRecordDetail' ORDER BY OccurredAt DESC"))

    # 17-22. Provenance of the most recent row, taken from trusted context and request metadata.
    $lastRequestId=$script:LastRequestId
    Add-Result 'audit provenance exact' "getPaymentRecordDetail|$($script:WorkspaceId)|$($script:MemberId)|$lastRequestId|corr-payments-read-audit-0001|READ" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(Operation,'|',WorkspaceId,'|',ActorId,'|',RequestId,'|',CorrelationId,'|',Outcome) FROM payments.ReadAuditRecords ORDER BY OccurredAt DESC, AuditId DESC"))
    Add-Result 'every audit row outcome is READ' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE Outcome <> 'READ'"))
    Add-Result 'list rows carry null recordId and version' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE Operation LIKE 'list%' AND (RecordId IS NOT NULL OR ResourceVersion IS NOT NULL)"))
    Add-Result 'resource rows always carry recordId' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE Operation LIKE 'get%' AND RecordId IS NULL"))
    Add-Result 'only admitted operationIds stored' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE Operation NOT IN ('listPaymentPlans','listPaymentScheduleLines','listPaymentIntents','listPaymentRecords','getPaymentIntent','getPaymentIntentStatus','getPaymentRecordDetail')"))
    Add-Result 'trusted Workspace provenance only' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE WorkspaceId <> '$($script:WorkspaceId)'"))

    # 23. No business value copied into evidence.
    $auditDump=Get-Scalar -Database $DatabaseName -Query "SELECT STRING_AGG(CONCAT(AuditId,'|',Operation,'|',WorkspaceId,'|',ActorId,'|',ISNULL(RecordId,''),'|',RequestId,'|',CorrelationId,'|',Outcome,'|',ISNULL(CAST(ResourceVersion AS varchar(32)),'')),' ') FROM payments.ReadAuditRecords"
    Add-Result 'no business values in audit evidence' 'True' ([string]($auditDump -notmatch 'provider_alpha|provider_beta|secret_provider|contact_audit|order_audit|CARD|BANK|USD|EUR|SUCCEEDED|CREATED|agreedAt|100|999'))

    # 24. RESOURCE reads produce AccessControl record decisions; 25. list reads produce none.
    $b=Get-RecordDecisionCount;[void](Invoke-Read "/payment-intents/$intentA/status");$statusDecisions=(Get-RecordDecisionCount)-$b
    Add-Result 'getPaymentIntentStatus writes 1 record decision' '1' ([string]$statusDecisions)
    Add-Result 'getPaymentIntentStatus record decision content' "payments|$intentA|getPaymentIntentStatus|1|RECORD_SCOPE_WORKSPACE" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(ResourceKey,'|',RecordId,'|',EnforcementPoint,'|',CAST(Allowed AS int),'|',DecisionCode) FROM access.RecordAccessDecisions WHERE EnforcementPoint='getPaymentIntentStatus' ORDER BY EvaluatedAt DESC"))
    $b=Get-RecordDecisionCount;[void](Invoke-Read "/payment-intents/$intentA");Add-Result 'getPaymentIntent writes 1 record decision' '1' ([string]((Get-RecordDecisionCount)-$b))
    $b=Get-RecordDecisionCount;[void](Invoke-Read "/payments/$recordA/detail");Add-Result 'getPaymentRecordDetail writes 1 record decision' '1' ([string]((Get-RecordDecisionCount)-$b))
    $b=Get-RecordDecisionCount;[void](Invoke-Read '/payments');Add-Result 'list writes zero per-row record decisions' '0' ([string]((Get-RecordDecisionCount)-$b))
    $b=Get-RecordDecisionCount;[void](Invoke-Read '/payment-intents');Add-Result 'intent list writes zero record decisions' '0' ([string]((Get-RecordDecisionCount)-$b))

    # 13-14. Unknown and foreign records disclose nothing and write no owner evidence.
    Add-Result 'unknown intent      => 404 and +0' '404|0' (Measure-Read '/payment-intents/intent_unknown').Probe
    Add-Result 'unknown record      => 404 and +0' '404|0' (Measure-Read '/payments/record_unknown/detail').Probe
    Add-Result 'foreign intent      => 404 and +0' '404|0' (Measure-Read "/payment-intents/$intentForeign").Probe
    Add-Result 'foreign intent stat => 404 and +0' '404|0' (Measure-Read "/payment-intents/$intentForeign/status").Probe
    Add-Result 'foreign record      => 404 and +0' '404|0' (Measure-Read "/payments/$recordForeign/detail").Probe
    Add-Result 'malformed path      => 404 and +0' '404|0' (Measure-Read '/payment-intents/%20bad').Probe

    # 11. Malformed but authorized query writes no owner evidence.
    Add-Result 'malformed authorized query => 422 and +0' '422|0' (Measure-Read '/payment-plans?orderId=%20bad').Probe
    Add-Result 'empty authorized query     => 422 and +0' '422|0' (Measure-Read '/payments?buyerId=').Probe

    # 10,12. Capability denial writes no owner evidence and still precedes malformed input.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability IN ('payments.read','payments.plan.read')"
    Add-Result 'denied listPaymentPlans   => 403 and +0' '403|0' (Measure-Read '/payment-plans').Probe
    Add-Result 'denied listPaymentRecords => 403 and +0' '403|0' (Measure-Read '/payments').Probe
    Add-Result 'denied getPaymentIntent   => 403 and +0' '403|0' (Measure-Read "/payment-intents/$intentA").Probe
    Add-Result 'denied getIntentStatus    => 403 and +0' '403|0' (Measure-Read "/payment-intents/$intentA/status").Probe
    Add-Result 'denied detail             => 403 and +0' '403|0' (Measure-Read "/payments/$recordA/detail").Probe
    Add-Result 'denied malformed query    => 403 and +0' '403|0' (Measure-Read '/payment-plans?orderId=%20bad').Probe
    Add-Result 'denied malformed path     => 403 and +0' '403|0' (Measure-Read '/payment-intents/%20bad').Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','payments.read'),('$roleId','payments.plan.read')"

    # 15. Record-access denial writes no owner evidence; lists fail closed to an empty result that is still an access.
    foreach($scope in @('Own','Team','Custom')){
        Set-Scope $roleId $scope
        Add-Result "$($scope.ToUpperInvariant()) detail denied => 404 and +0" '404|0' (Measure-Read "/payment-intents/$intentA").Probe
        $scoped=Measure-Read '/payments'
        Add-Result "$($scope.ToUpperInvariant()) list fails closed to empty" '200|1' $scoped.Probe
        Add-Result "$($scope.ToUpperInvariant()) list discloses nothing" '0' ([string]$scoped.Body.Count)
    }
    Set-Scope $roleId 'Workspace'

    # 26-28. Field security unchanged by this correction; a required hidden field still fails closed and writes no evidence.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_pay_audit_%'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity(PolicyId,RoleId,ResourceKey,FieldKey,Access)VALUES('field_pay_audit_state','$roleId','payments','state','Hidden')"
    Add-Result 'required hidden field  => 403 and +0' '403|0' (Measure-Read '/payments').Probe
    Add-Result 'required hidden intent => 403 and +0' '403|0' (Measure-Read "/payment-intents/$intentA").Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_pay_audit_%'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity(PolicyId,RoleId,ResourceKey,FieldKey,Access)VALUES('field_pay_audit_provider','$roleId','payments','providerCode','Hidden')"
    $optional=Measure-Read '/payments'
    Add-Result 'optional hidden field  => 200 and +1' '200|1' $optional.Probe
    Add-Result 'optional hidden field omitted' 'True' ([string]($optional.Raw -notmatch 'providerCode'))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_pay_audit_%'"

    # 16. Contract-invalid persisted state fails before disclosure and writes no owner evidence.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE payments.PaymentRecords SET EvidenceJson=N'[{`"id`":`"broken`"}]' WHERE WorkspaceId='$($script:WorkspaceId)' AND PaymentRecordId='$recordA'"
    Add-Result 'corrupt state list   => 500 and +0' '500|0' (Measure-Read '/payments').Probe
    Add-Result 'corrupt state detail => 500 and +0' '500|0' (Measure-Read "/payments/$recordA/detail").Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE payments.PaymentRecords SET EvidenceJson=NULL WHERE WorkspaceId='$($script:WorkspaceId)' AND PaymentRecordId='$recordA'"
    Add-Result 'restored fixture reads cleanly' '200|1' (Measure-Read '/payments').Probe

    # 29. Workspace isolation preserved.
    Add-Result 'foreign Workspace rows never disclosed' 'True' ([string]((Invoke-Read '/payments').Raw -notmatch 'secret_provider' -and (Invoke-Read '/payment-intents').Raw -notmatch 'secret_provider'))
    Add-Result 'no audit rows for foreign Workspace' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM payments.ReadAuditRecords WHERE WorkspaceId='$foreignWorkspace'"))

    # 31-34. Owner-boundary source proof.
    $files=Get-ChildItem $paymentsRoot -Recurse -File|Where-Object Extension -eq '.cs';$source=($files|ForEach-Object{Get-Content $_.FullName -Raw})-join"`n"
    Add-Result 'no foreign DbContext' 'True' ([string]($source-notmatch'OrdersDbContext|InvoicesDbContext|QuotesDbContext|CustomersDbContext|ContactsDbContext|ProductsDbContext|FulfillmentDbContext'))
    Add-Result 'no provider runtime call' 'True' ([string]($source-notmatch'HttpClient|ProviderClient|IPaymentProvider|Webhook'))
    Add-Result 'no mutation route' 'True' ([string]($source-notmatch'MapPost|MapPatch|MapDelete|MapPut'))
    Add-Result 'no workflow outbox idempotency' 'True' ([string]($source-notmatch'IWorkflow|Outbox|Idempotenc'))
    Add-Result 'no generic cross-module audit framework' 'True' ([string]($source-notmatch'IAuditFramework|IReadAuditService|GenericAudit'))
    foreach($case in @(@('POST','/payments'),@('PATCH',"/payment-intents/$intentA"),@('DELETE',"/payments/$recordA"),@('POST',"/payment-intents/$intentA/capture"))){Add-Result "no mutation route $($case[0]) $($case[1])" 'True' ([string]((Invoke-Api -Method $case[0] -Path $case[1] -Token $script:Token -WorkspaceId $script:WorkspaceId -Body '{}').Status-in 404,405))}
}catch{$script:Failed++;[void]$script:Results.Add("FAIL | verifier execution | $($_.Exception.Message)");if(Test-Path $logPath){[void]$script:Results.Add((Get-Content $logPath -Tail 40)-join"`n")}}
finally{if($hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id -Force;$hostProcess.WaitForExit()};if(-not $KeepDatabase){try{Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;"}catch{}};Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue}
$script:Results|ForEach-Object{Write-Host $_};Write-Host "PAYMENTS READ AUDIT RESULT: PASS=$script:Passed FAIL=$script:Failed";if($script:Failed -gt 0){exit 1}
