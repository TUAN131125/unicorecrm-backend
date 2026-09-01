<# .SYNOPSIS Verifies Invoices-owned READ_ACCESS_LOG read-audit conformance against an isolated database and real ApiHost. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_]{1,128}$')][string]$DatabaseName,
    [string]$SqlServer='(localdb)\MSSQLLocalDB',[int]$Port=5365,[int]$ReadyTimeoutSeconds=420,[switch]$KeepDatabase)
$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Net.Http
$script:Passed=0;$script:Failed=0;$script:Results=New-Object System.Collections.ArrayList;$script:Counter=0
$script:BaseUrl="http://127.0.0.1:$Port";$script:Token=$null;$script:WorkspaceId=$null;$script:MemberId=$null;$script:LastRequestId=$null
function Add-Result([string]$Name,[string]$Expected,[string]$Actual){if($Expected-eq$Actual){$script:Passed++;[void]$script:Results.Add("PASS | $Name | $Actual")}else{$script:Failed++;[void]$script:Results.Add("FAIL | $Name | expected=$Expected actual=$Actual")}}
function New-ConnectionString([string]$Database){"Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"}
function Invoke-SqlNonQuery([string]$Query,[string]$Database='master'){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;[void]$cmd.ExecuteNonQuery()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function Get-Scalar([string]$Query,[string]$Database){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;return $cmd.ExecuteScalar()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function New-RequestId{$script:Counter++;$script:LastRequestId='req-inv-audit-{0:d6}'-f$script:Counter;$script:LastRequestId}
function Invoke-Api{param([string]$Method,[string]$Path,[string]$Body,[string]$Token,[string]$WorkspaceId,[string]$IdempotencyKey)
    $req=New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method),"$script:BaseUrl$Path");[void]$req.Headers.TryAddWithoutValidation('X-Request-Id',(New-RequestId));[void]$req.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-invoices-read-audit-0001')
    if($Token){[void]$req.Headers.TryAddWithoutValidation('Authorization',"Bearer $Token")};if($WorkspaceId){[void]$req.Headers.TryAddWithoutValidation('X-Workspace-Id',$WorkspaceId)};if($IdempotencyKey){[void]$req.Headers.TryAddWithoutValidation('Idempotency-Key',$IdempotencyKey)};if($Body){$req.Content=New-Object System.Net.Http.StringContent($Body,[Text.Encoding]::UTF8,'application/json')}
    $h=New-Object System.Net.Http.HttpClientHandler;$h.UseProxy=$false;$h.AllowAutoRedirect=$false;$client=New-Object System.Net.Http.HttpClient($h,$true);$client.Timeout=[TimeSpan]::FromSeconds(60);$resp=$null
    try{$resp=$client.SendAsync($req).GetAwaiter().GetResult();$raw=$resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();$status=[int]$resp.StatusCode}finally{if($resp){$resp.Dispose()};$client.Dispose();$req.Dispose()};$payload=$null;if($raw){try{$payload=$raw|ConvertFrom-Json}catch{}};[pscustomobject]@{Status=$status;Body=$payload;Raw=$raw}}
function Invoke-Read([string]$Path){Invoke-Api -Method GET -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId}
function Get-AuditCount{[int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords")}
function Get-RecordDecisionCount{[int](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey='invoices'")}
# Issues one read and reports "<httpStatus>|<ownerAuditDelta>".
function Measure-Read([string]$Path){$b=Get-AuditCount;$r=Invoke-Read $Path;$d=(Get-AuditCount)-$b;[pscustomobject]@{Status=$r.Status;Delta=$d;Body=$r.Body;Raw=$r.Raw;Probe="$($r.Status)|$d"}}
function Set-Scope([string]$RoleId,[string]$Scope){Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_invoice_audit'; INSERT INTO access.RoleDataScopes(PolicyId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES('scope_invoice_audit','$RoleId','invoices','$Scope','[]');"}
function Clear-Fields{Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_inv_audit_%'"}
$root=(Resolve-Path(Join-Path $PSScriptRoot '..')).Path;$hostProject=Join-Path $root 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj';$invoicesRoot=Join-Path $root 'src/UnicoreCRM.Billing/Invoices'
$email='invoices.read.audit@example.test';$password='Invoices-Read-Audit!2026';$hostProcess=$null;$logPath=Join-Path([IO.Path]::GetTempPath())("unicore-invoices-audit-$([Guid]::NewGuid().ToString('N')).log")
$invoiceA='invoice_audit_a';$invoiceB='invoice_audit_b';$invoiceForeign='invoice_audit_foreign';$foreignWorkspace='ws_invoices_audit_foreign'
try{
    Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];"
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_URLS=$script:BaseUrl;$env:ConnectionStrings__UnicoreCRM=New-ConnectionString $DatabaseName;$env:Development__ApplyMigrations='true';$env:UNICORE_DEV_SEED_ENABLED='false';$env:IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog';$env:IdentityAuth__DevelopmentBootstrap__Enabled='true';$env:IdentityAuth__DevelopmentBootstrap__Email=$email;$env:IdentityAuth__DevelopmentBootstrap__Password=$password;$env:IdentityAuth__DevelopmentBootstrap__DisplayName='Invoices Audit Fixture';$env:Workspace__DevelopmentBootstrap__Enabled='false';$env:AccessControl__DevelopmentBootstrap__Enabled='false';$env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false';$env:AI__Provider__Kind='DevelopmentDeterministic'
    $hostProcess=Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$hostProject)-PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err";$ready=$false
    for($i=0;$i-lt$ReadyTimeoutSeconds;$i++){Start-Sleep -Seconds 1;if($hostProcess.HasExited){throw "ApiHost exited $($hostProcess.ExitCode). See $logPath"};try{if((Invoke-Api -Method GET -Path '/auth/session').Status-gt 0){$ready=$true;break}}catch{}};if(-not$ready){throw "ApiHost not ready. See $logPath"}
    $signIn=Invoke-Api -Method POST -Path '/auth/sessions' -IdempotencyKey 'idem-inv-audit-signin-0001' -Body(@{email=$email;password=$password}|ConvertTo-Json -Compress);if($signIn.Status-ne 200){throw "Sign-in failed: $($signIn.Raw)"};$script:Token=$signIn.Body.accessToken
    $prov=Invoke-Api -Method POST -Path '/workspaces/initial-provisioning' -Token $script:Token -IdempotencyKey 'idem-inv-audit-prov-0001' -Body '{"name":"Invoices Audit Workspace"}';Add-Result 'Workspace provisioning succeeds' '201' ([string]$prov.Status);$script:WorkspaceId=$prov.Body.workspaceId
    $roleId=Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:WorkspaceId)' AND Name='Workspace Owner'"
    $script:MemberId=Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 MemberId FROM workspace.Memberships WHERE WorkspaceId='$($script:WorkspaceId)'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','invoices.read')"

    # Storage shape.
    Add-Result 'invoices.ReadAuditRecords table exists' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='invoices' AND TABLE_NAME='ReadAuditRecords'"))
    Add-Result 'read-audit columns exact' 'ActorId,AuditId,CorrelationId,OccurredAt,Operation,Outcome,RecordId,RequestId,ResourceVersion,WorkspaceId' ((Get-Scalar -Database $DatabaseName -Query "SELECT STRING_AGG(COLUMN_NAME,',') WITHIN GROUP (ORDER BY COLUMN_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='invoices' AND TABLE_NAME='ReadAuditRecords'"))
    Add-Result 'read-audit Workspace-leading index' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('invoices.ReadAuditRecords') AND name='IX_ReadAuditRecords_WorkspaceId_OccurredAt'"))
    Add-Result 'read-audit has no foreign key' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('invoices.ReadAuditRecords')"))

    $seller='{"displayName":"Unicore Seller","addressLines":["1 Seller Way"],"countryCode":"US"}'
    $buyer='{"displayName":"Acme Buyer","addressLines":["9 Buyer Road"],"countryCode":"DE"}'
    $linesA='[{"id":"invoice_line_a","description":"Consulting","quantity":"4","unitPrice":{"amount":"100.50","currency":"USD"},"discountAmount":{"amount":"0","currency":"USD"},"taxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"402","currency":"USD"}}]'
    $totalsA='{"subtotal":{"amount":"402","currency":"USD"},"discountTotal":{"amount":"0","currency":"USD"},"taxTotal":{"amount":"0","currency":"USD"},"grandTotal":{"amount":"402","currency":"USD"}}'
    $linesF='[{"id":"invoice_line_secret","description":"foreign-secret-line","quantity":"1","unitPrice":{"amount":"999","currency":"USD"},"discountAmount":{"amount":"0","currency":"USD"},"taxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"999","currency":"USD"}}]'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO invoices.Invoices(WorkspaceId,InvoiceId,InvoiceNumber,BuyerType,BuyerId,SellerSnapshotJson,BuyerSnapshotJson,LifecycleState,DeliveryState,Currency,LinesJson,TotalsJson,SourceLinksJson,ResourceVersion,IdempotencyKey,CreatedAt,UpdatedAt) VALUES
('$($script:WorkspaceId)','$invoiceA','INV-2026-0001','CONTACT','contact_invoice_a',N'$seller',N'$buyer','ISSUED','SENT','USD',N'$linesA',N'$totalsA',N'{"orderId":"order_invoice_a"}',7,'idem-invoice-audit-a-01','2026-08-30T10:00:00+00:00','2026-08-30T11:00:00+00:00'),
('$($script:WorkspaceId)','$invoiceB','INV-2026-0002','ORGANIZATION_ACCOUNT','organization_invoice_b',N'$seller',N'$buyer','DRAFT','NOT_SENT','USD',N'$linesA',N'$totalsA',N'{}',0,'idem-invoice-audit-b-01','2026-08-29T00:00:00+00:00','2026-08-29T00:00:00+00:00'),
('$foreignWorkspace','$invoiceForeign','INV-FOREIGN-SECRET','CONTACT','contact_invoice_a',N'$seller',N'$buyer','ISSUED','SENT','USD',N'$linesF',N'$totalsA',N'{}',1,'idem-invoice-audit-f-01','2026-08-29T00:00:00+00:00','2026-08-29T00:00:00+00:00');
"@
    Set-Scope $roleId 'Workspace'

    # 1,3. Successful multi-row list writes exactly one row, never one per Invoice.
    $list=Measure-Read '/invoices'
    Add-Result 'listInvoices => 200 and +1' '200|1' $list.Probe
    Add-Result 'multi-row list returns 2 Invoices' '2' ([string]$list.Body.Count)
    # 4,5. List row carries no record identity or version.
    Add-Result 'list row recordId and version null' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE Operation='listInvoices' AND RecordId IS NULL AND ResourceVersion IS NULL"))

    # 6,7,8. Successful detail writes exactly one row with recordId and the Invoice version.
    Add-Result 'getInvoice => 200 and +1' '200|1' (Measure-Read "/invoices/$invoiceA").Probe
    Add-Result 'detail recordId and version exact' "$invoiceA|7" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(RecordId,'|',ResourceVersion) FROM invoices.ReadAuditRecords WHERE Operation='getInvoice' ORDER BY OccurredAt DESC"))
    Add-Result 'detail version matches response version' '7' ([string](Invoke-Read "/invoices/$invoiceA").Body.version)

    # 9-14. Provenance of the most recent row comes from trusted context and request metadata.
    [void](Invoke-Read "/invoices/$invoiceB");$lastRequestId=$script:LastRequestId
    Add-Result 'audit provenance exact' "getInvoice|$($script:WorkspaceId)|$($script:MemberId)|$lastRequestId|corr-invoices-read-audit-0001|READ|$invoiceB|0" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(Operation,'|',WorkspaceId,'|',ActorId,'|',RequestId,'|',CorrelationId,'|',Outcome,'|',RecordId,'|',ResourceVersion) FROM invoices.ReadAuditRecords ORDER BY OccurredAt DESC, AuditId DESC"))
    Add-Result 'every audit row outcome is READ' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE Outcome <> 'READ'"))
    Add-Result 'only admitted operationIds stored' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE Operation NOT IN ('listInvoices','getInvoice')"))
    Add-Result 'trusted Workspace provenance only' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE WorkspaceId <> '$($script:WorkspaceId)'"))
    Add-Result 'detail rows always carry recordId' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE Operation='getInvoice' AND RecordId IS NULL"))

    # 2. Empty successful list still writes exactly one row.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM invoices.Invoices WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceB'"
    $emptyProbe=Measure-Read '/invoices'
    Add-Result 'list after delete => 200 and +1' '200|1' $emptyProbe.Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM invoices.Invoices WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceA'"
    $empty=Measure-Read '/invoices'
    Add-Result 'empty listInvoices => 200 and +1' '200|1' $empty.Probe
    Add-Result 'empty list really empty' '0' ([string]$empty.Body.Count)
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO invoices.Invoices(WorkspaceId,InvoiceId,InvoiceNumber,BuyerType,BuyerId,SellerSnapshotJson,BuyerSnapshotJson,LifecycleState,DeliveryState,Currency,LinesJson,TotalsJson,SourceLinksJson,ResourceVersion,IdempotencyKey,CreatedAt,UpdatedAt) VALUES
('$($script:WorkspaceId)','$invoiceA','INV-2026-0001','CONTACT','contact_invoice_a',N'$seller',N'$buyer','ISSUED','SENT','USD',N'$linesA',N'$totalsA',N'{"orderId":"order_invoice_a"}',7,'idem-invoice-audit-a-01','2026-08-30T10:00:00+00:00','2026-08-30T11:00:00+00:00');
"@

    # 23. No Invoice business value copied into evidence.
    $auditDump=Get-Scalar -Database $DatabaseName -Query "SELECT STRING_AGG(CONCAT(AuditId,'|',Operation,'|',WorkspaceId,'|',ActorId,'|',ISNULL(RecordId,''),'|',RequestId,'|',CorrelationId,'|',Outcome,'|',ISNULL(CAST(ResourceVersion AS varchar(32)),'')),' ') FROM invoices.ReadAuditRecords"
    Add-Result 'no business values in audit evidence' 'True' ([string]($auditDump -notmatch 'INV-2026|INV-FOREIGN|Unicore Seller|Acme Buyer|contact_invoice|organization_invoice|order_invoice|ISSUED|DRAFT|USD|402|Consulting|idem-invoice'))

    # 24,25. Detail keeps canonical record-access evaluation; list introduces no per-row decisions.
    $b=Get-RecordDecisionCount;[void](Invoke-Read "/invoices/$invoiceA");Add-Result 'getInvoice writes 1 record decision' '1' ([string]((Get-RecordDecisionCount)-$b))
    Add-Result 'record decision content unchanged' "invoices|$invoiceA|getInvoice|1|RECORD_SCOPE_WORKSPACE" ((Get-Scalar -Database $DatabaseName -Query "SELECT TOP 1 CONCAT(ResourceKey,'|',RecordId,'|',EnforcementPoint,'|',CAST(Allowed AS int),'|',DecisionCode) FROM access.RecordAccessDecisions WHERE EnforcementPoint='getInvoice' ORDER BY EvaluatedAt DESC"))
    $b=Get-RecordDecisionCount;[void](Invoke-Read '/invoices');Add-Result 'list writes zero per-row record decisions' '0' ([string]((Get-RecordDecisionCount)-$b))

    # 17-19. Malformed, unknown and foreign identifiers disclose nothing and write no evidence.
    Add-Result 'malformed path authorized => 404 and +0' '404|0' (Measure-Read '/invoices/%20bad').Probe
    Add-Result 'unknown invoice        => 404 and +0' '404|0' (Measure-Read '/invoices/invoice_unknown').Probe
    Add-Result 'foreign Workspace inv  => 404 and +0' '404|0' (Measure-Read "/invoices/$invoiceForeign").Probe

    # 15,16. Capability denial writes no evidence and still precedes identifier validation.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='invoices.read'"
    Add-Result 'denied listInvoices    => 403 and +0' '403|0' (Measure-Read '/invoices').Probe
    Add-Result 'denied getInvoice      => 403 and +0' '403|0' (Measure-Read "/invoices/$invoiceA").Probe
    Add-Result 'denied malformed path  => 403 and +0' '403|0' (Measure-Read '/invoices/%20bad').Probe
    Add-Result 'denied unknown invoice => 403 and +0' '403|0' (Measure-Read '/invoices/invoice_unknown').Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','invoices.read')"

    # 20,27. Record-access denial writes no evidence; OWN/TEAM/CUSTOM stay fail closed.
    foreach($scope in @('Own','Team','Custom')){
        Set-Scope $roleId $scope
        Add-Result "$($scope.ToUpperInvariant()) detail denied => 404 and +0" '404|0' (Measure-Read "/invoices/$invoiceA").Probe
        $scoped=Measure-Read '/invoices'
        Add-Result "$($scope.ToUpperInvariant()) list fails closed to empty" '200|1' $scoped.Probe
        Add-Result "$($scope.ToUpperInvariant()) list discloses nothing" '0' ([string]$scoped.Body.Count)
    }
    Set-Scope $roleId 'Workspace'

    # 21. Required hidden field fails closed and writes no evidence; optional hidden stays a disclosure.
    Clear-Fields;Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity(PolicyId,RoleId,ResourceKey,FieldKey,Access)VALUES('field_inv_audit_lifecycle','$roleId','invoices','lifecycleState','Hidden')"
    Add-Result 'required hidden list   => 403 and +0' '403|0' (Measure-Read '/invoices').Probe
    Add-Result 'required hidden detail => 403 and +0' '403|0' (Measure-Read "/invoices/$invoiceA").Probe
    Clear-Fields;Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity(PolicyId,RoleId,ResourceKey,FieldKey,Access)VALUES('field_inv_audit_terms','$roleId','invoices','paymentTerms','Hidden')"
    Add-Result 'optional hidden detail => 200 and +1' '200|1' (Measure-Read "/invoices/$invoiceA").Probe
    Clear-Fields

    # 22. Contract-invalid persisted state fails before disclosure and writes no evidence.
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET LinesJson=N'[]' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceA'"
    $corruptDetail=Measure-Read "/invoices/$invoiceA"
    Add-Result 'corrupt state detail => 500 and +0' '500|0' $corruptDetail.Probe
    Add-Result 'corrupt detail emits no partial Invoice' 'True' ([string]($corruptDetail.Raw -notmatch 'INV-2026-0001'))
    Add-Result 'corrupt state list   => 500 and +0' '500|0' (Measure-Read '/invoices').Probe
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET LinesJson=N'$linesA' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceA'"
    Add-Result 'restored fixture reads cleanly' '200|1' (Measure-Read '/invoices').Probe

    # 26,28,29. WORKSPACE behavior, isolation and exact response shape unchanged.
    $shape=Invoke-Read "/invoices/$invoiceA"
    Add-Result 'detail response shape unchanged' 'buyerRef,buyerSnapshot,createdAt,currency,deliveryState,id,idempotencyKey,invoiceNumber,lifecycleState,lines,sellerSnapshot,sourceLinks,totals,updatedAt,version,workspaceId' (($shape.Body.PSObject.Properties.Name|Sort-Object)-join',')
    Add-Result 'foreign values never disclosed' 'True' ([string]((Invoke-Read '/invoices').Raw -notmatch 'INV-FOREIGN-SECRET|foreign-secret'))
    Add-Result 'no audit rows for foreign Workspace' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM invoices.ReadAuditRecords WHERE WorkspaceId='$foreignWorkspace'"))

    # 30-33. Owner-boundary and surface proof.
    $files=Get-ChildItem $invoicesRoot -Recurse -File|Where-Object Extension -eq '.cs';$source=($files|ForEach-Object{Get-Content $_.FullName -Raw})-join"`n"
    Add-Result 'no foreign DbContext' 'True' ([string]($source-notmatch'PaymentsDbContext|OrdersDbContext|QuotesDbContext|CustomersDbContext|ContactsDbContext|ProductsDbContext|FulfillmentDbContext'))
    Add-Result 'no Payments or Orders runtime lookup' 'True' ([string]($source-notmatch'UnicoreCRM\.Sales|UnicoreCRM\.Crm|UnicoreCRM\.Fulfillment|Billing\.Payments'))
    Add-Result 'no provider or tax runtime call' 'True' ([string]($source-notmatch'HttpClient|ProviderClient|TaxEngine|Webhook'))
    Add-Result 'no mutation workflow outbox idempotency' 'True' ([string]($source-notmatch'MapPost|MapPatch|MapDelete|MapPut|IWorkflow|Outbox'))
    Add-Result 'no generic cross-module audit framework' 'True' ([string]($source-notmatch'IAuditFramework|IReadAuditService|GenericAudit'))
    $saveChangesFiles=(Get-ChildItem $invoicesRoot -Recurse -File -Filter *.cs|Where-Object{(Get-Content $_.FullName -Raw)-match'SaveChangesAsync'}|ForEach-Object Name|Sort-Object)-join','
    Add-Result 'SaveChanges confined to read-audit append' 'EfInvoicesPersistence.cs,InvoiceReadAudit.cs,InvoicesApplication.cs' $saveChangesFiles
    $routes=Get-Content(Join-Path $invoicesRoot 'Contracts/InvoiceEndpoints.cs')-Raw
    Add-Result 'exactly two Invoice GET routes' '2' ([string]([regex]::Matches($routes,'\.MapGet\(').Count))
    foreach($case in @(@('POST','/invoices'),@('POST','/invoices/drafts'),@('PATCH',"/invoices/$invoiceA"),@('DELETE',"/invoices/$invoiceA"),@('POST',"/invoices/$invoiceA/issue"))){Add-Result "no mutation $($case[0]) $($case[1])" 'True' ([string]((Invoke-Api -Method $case[0] -Path $case[1] -Token $script:Token -WorkspaceId $script:WorkspaceId -Body '{}').Status-in 404,405))}
    Add-Result 'no unadmitted Invoice read route' 'True' ([string]((Invoke-Read "/invoices/$invoiceA/issue-readiness").Status-eq 404 -and (Invoke-Read '/invoice-deliveries').Status-eq 404 -and (Invoke-Read '/credit-notes').Status-eq 404))
}catch{$script:Failed++;[void]$script:Results.Add("FAIL | verifier execution | $($_.Exception.Message)");if(Test-Path $logPath){[void]$script:Results.Add((Get-Content $logPath -Tail 40)-join"`n")}}
finally{if($hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id -Force;$hostProcess.WaitForExit()};if(-not $KeepDatabase){try{Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;"}catch{}};Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue}
$script:Results|ForEach-Object{Write-Host $_};Write-Host "INVOICE READ AUDIT RESULT: PASS=$script:Passed FAIL=$script:Failed";if($script:Failed -gt 0){exit 1}
