<# .SYNOPSIS Verifies the Invoices-owned Invoice read core against an isolated database and real ApiHost. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9_]{1,128}$')][string]$DatabaseName,
    [string]$SqlServer='(localdb)\MSSQLLocalDB',[int]$Port=5361,[int]$ReadyTimeoutSeconds=420,[switch]$KeepDatabase)
$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Net.Http
$script:Passed=0;$script:Failed=0;$script:Results=New-Object System.Collections.ArrayList;$script:Counter=0
$script:BaseUrl="http://127.0.0.1:$Port";$script:Token=$null;$script:WorkspaceId=$null
function Add-Result([string]$Name,[string]$Expected,[string]$Actual){if($Expected-eq$Actual){$script:Passed++;[void]$script:Results.Add("PASS | $Name | $Actual")}else{$script:Failed++;[void]$script:Results.Add("FAIL | $Name | expected=$Expected actual=$Actual")}}
function New-ConnectionString([string]$Database){"Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"}
function Invoke-SqlNonQuery([string]$Query,[string]$Database='master'){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;[void]$cmd.ExecuteNonQuery()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function Get-Scalar([string]$Query,[string]$Database){$c=New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString $Database);$cmd=$null;$c.Open();try{$cmd=$c.CreateCommand();$cmd.CommandText=$Query;$cmd.CommandTimeout=120;return $cmd.ExecuteScalar()}finally{if($cmd){$cmd.Dispose()};$c.Dispose()}}
function New-RequestId{$script:Counter++;'req-invoice-read-{0:d6}'-f$script:Counter}
function Invoke-Api{param([string]$Method,[string]$Path,[string]$Body,[string]$Token,[string]$WorkspaceId,[string]$IdempotencyKey)
    $req=New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method),"$script:BaseUrl$Path");[void]$req.Headers.TryAddWithoutValidation('X-Request-Id',(New-RequestId));[void]$req.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-invoice-read-core-0001')
    if($Token){[void]$req.Headers.TryAddWithoutValidation('Authorization',"Bearer $Token")};if($WorkspaceId){[void]$req.Headers.TryAddWithoutValidation('X-Workspace-Id',$WorkspaceId)};if($IdempotencyKey){[void]$req.Headers.TryAddWithoutValidation('Idempotency-Key',$IdempotencyKey)};if($Body){$req.Content=New-Object System.Net.Http.StringContent($Body,[Text.Encoding]::UTF8,'application/json')}
    $h=New-Object System.Net.Http.HttpClientHandler;$h.UseProxy=$false;$h.AllowAutoRedirect=$false;$client=New-Object System.Net.Http.HttpClient($h,$true);$client.Timeout=[TimeSpan]::FromSeconds(60);$resp=$null
    try{$resp=$client.SendAsync($req).GetAwaiter().GetResult();$raw=$resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();$status=[int]$resp.StatusCode}finally{if($resp){$resp.Dispose()};$client.Dispose();$req.Dispose()};$payload=$null;if($raw){try{$payload=$raw|ConvertFrom-Json}catch{}};[pscustomobject]@{Status=$status;Body=$payload;Raw=$raw}}
function Invoke-Invoice([string]$Path){Invoke-Api -Method GET -Path $Path -Token $script:Token -WorkspaceId $script:WorkspaceId}
function Set-Scope([string]$RoleId,[string]$Scope){Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleDataScopes WHERE PolicyId='scope_invoice_read'; INSERT INTO access.RoleDataScopes(PolicyId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES('scope_invoice_read','$RoleId','invoices','$Scope','[]');"}
function Clear-Fields{Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleFieldSecurity WHERE PolicyId LIKE 'field_invoice_read_%'"}
function Hide-Field([string]$RoleId,[string]$FieldKey){Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleFieldSecurity(PolicyId,RoleId,ResourceKey,FieldKey,Access)VALUES('field_invoice_read_$($FieldKey.ToLowerInvariant())','$RoleId','invoices','$FieldKey','Hidden')"}
$root=(Resolve-Path(Join-Path $PSScriptRoot '..')).Path;$hostProject=Join-Path $root 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj';$invoicesRoot=Join-Path $root 'src/UnicoreCRM.Billing/Invoices'
$email='invoice.read@example.test';$password='Invoice-Read-Core!2026';$hostProcess=$null;$logPath=Join-Path([IO.Path]::GetTempPath())("unicore-invoice-read-$([Guid]::NewGuid().ToString('N')).log")
$invoiceFull='invoice_read_full';$invoiceMinimal='invoice_read_minimal';$invoiceForeign='invoice_read_foreign';$foreignWorkspace='ws_invoice_read_foreign'
try{
    Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];"
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_URLS=$script:BaseUrl;$env:ConnectionStrings__UnicoreCRM=New-ConnectionString $DatabaseName;$env:Development__ApplyMigrations='true';$env:UNICORE_DEV_SEED_ENABLED='false';$env:IdentityAuth__EmailVerification__Sender__Kind='DevelopmentLog';$env:IdentityAuth__DevelopmentBootstrap__Enabled='true';$env:IdentityAuth__DevelopmentBootstrap__Email=$email;$env:IdentityAuth__DevelopmentBootstrap__Password=$password;$env:IdentityAuth__DevelopmentBootstrap__DisplayName='Invoice Read Fixture';$env:Workspace__DevelopmentBootstrap__Enabled='false';$env:AccessControl__DevelopmentBootstrap__Enabled='false';$env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false';$env:AI__Provider__Kind='DevelopmentDeterministic'
    $hostProcess=Start-Process dotnet -ArgumentList @('run','--no-build','--no-launch-profile','--project',$hostProject)-PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err";$ready=$false
    for($i=0;$i-lt$ReadyTimeoutSeconds;$i++){Start-Sleep -Seconds 1;if($hostProcess.HasExited){throw "ApiHost exited $($hostProcess.ExitCode). See $logPath"};try{if((Invoke-Api -Method GET -Path '/auth/session').Status-gt 0){$ready=$true;break}}catch{}};if(-not$ready){throw "ApiHost not ready. See $logPath"}

    # 1. unauthenticated rejection
    Add-Result 'unauthenticated list rejected' '401' ([string](Invoke-Api -Method GET -Path '/invoices' -WorkspaceId 'ws_unknown').Status)
    Add-Result 'unauthenticated detail rejected' '401' ([string](Invoke-Api -Method GET -Path "/invoices/$invoiceFull" -WorkspaceId 'ws_unknown').Status)

    $signIn=Invoke-Api -Method POST -Path '/auth/sessions' -IdempotencyKey 'idem-invoice-read-signin-0001' -Body(@{email=$email;password=$password}|ConvertTo-Json -Compress);if($signIn.Status-ne 200){throw "Sign-in failed: $($signIn.Raw)"};$script:Token=$signIn.Body.accessToken
    $provision=Invoke-Api -Method POST -Path '/workspaces/initial-provisioning' -Token $script:Token -IdempotencyKey 'idem-invoice-read-provision-0001' -Body '{"name":"Invoice Read Workspace"}';Add-Result 'Workspace provisioning succeeds' '201' ([string]$provision.Status);$script:WorkspaceId=$provision.Body.workspaceId
    $roleId=Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:WorkspaceId)' AND Name='Workspace Owner'"
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','invoices.read')"

    # 2. Trusted Workspace required
    Add-Result 'missing Trusted Workspace rejected on list' '403' ([string](Invoke-Api -Method GET -Path '/invoices' -Token $script:Token).Status)
    Add-Result 'missing Trusted Workspace rejected on detail' '403' ([string](Invoke-Api -Method GET -Path "/invoices/$invoiceFull" -Token $script:Token).Status)
    Add-Result 'unknown Workspace nondisclosing' '403' ([string](Invoke-Api -Method GET -Path '/invoices' -Token $script:Token -WorkspaceId 'ws_never_provisioned').Status)

    # 22. fresh migration valid: owner schema, owner table, Workspace-leading PK, no foreign key, no speculative index
    Add-Result 'fresh migration creates invoices.Invoices' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='invoices' AND TABLE_NAME='Invoices'"))
    Add-Result 'Workspace-leading primary key' 'WorkspaceId,InvoiceId' ((Get-Scalar -Database $DatabaseName -Query "SELECT STRING_AGG(c.name,',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('invoices.Invoices') AND i.is_primary_key=1"))
    Add-Result 'no foreign key on Invoices' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('invoices.Invoices')"))
    Add-Result 'no speculative index on Invoices' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('invoices.Invoices') AND index_id>0"))

    $seller='{"displayName":"Unicore Seller","legalName":"Unicore Seller Ltd","taxId":"TAX-1","email":"billing@seller.test","phone":"+1000","addressLines":["1 Seller Way","Suite 2"],"countryCode":"US"}'
    $buyer='{"displayName":"Acme Buyer","addressLines":["9 Buyer Road"],"countryCode":"DE"}'
    $lines='[{"id":"invoice_line_a","sourceOrderLineId":"order_line_a","orderLineId":"order_line_a","productId":"product_a","skuSnapshot":"SKU-A","description":"Consulting","unitOfMeasure":"hour","sourceOrderQuantity":"10","alreadyInvoicedQuantity":"2","invoiceableQuantity":"8","quantity":"4","unitPrice":{"amount":"100.50","currency":"USD"},"discountRate":"0.1","discountAmount":{"amount":"40.20","currency":"USD"},"taxRate":"0.2","taxAmount":{"amount":"72.36","currency":"USD"},"lineTotal":{"amount":"434.16","currency":"USD"},"notes":"line note"}]'
    $totals='{"subtotal":{"amount":"402","currency":"USD"},"discountTotal":{"amount":"40.20","currency":"USD"},"taxTotal":{"amount":"72.36","currency":"USD"},"roundingAdjustment":{"amount":"0.01","currency":"USD"},"grandTotal":{"amount":"434.17","currency":"USD"}}'
    $sourceLinks='{"orderId":"order_invoice_a","paymentScheduleLineIds":["schedule_a","schedule_b"],"shippingBookingIds":["booking_a"],"returnIds":["return_a"],"milestoneCodes":["M1"]}'
    $rate='{"fromCurrency":"USD","toCurrency":"EUR","rate":"0.92","effectiveAt":"2026-08-30T10:00:00Z","source":"CONNECTED_PROVIDER","rateId":"rate_a","rateVersion":3}'
    $evidence='[{"id":"evidence_a","type":"INVOICE_ISSUE_RESULT","fileName":"issue.pdf","mimeType":"application/pdf","url":"/evidence/a","externalReference":"provider-issue-a","capturedAt":"2026-08-30T10:01:00Z","capturedBy":"member_a","verificationState":"VERIFIED","notes":"issued","lockedByBusinessEvent":true,"createdAt":"2026-08-30T10:02:00Z"}]'
    $minimalLines='[{"id":"invoice_line_b","description":"Support","quantity":"1","unitPrice":{"amount":"99","currency":"EUR"},"discountAmount":{"amount":"0","currency":"EUR"},"taxAmount":{"amount":"0","currency":"EUR"},"lineTotal":{"amount":"99","currency":"EUR"}}]'
    $minimalTotals='{"subtotal":{"amount":"99","currency":"EUR"},"discountTotal":{"amount":"0","currency":"EUR"},"taxTotal":{"amount":"0","currency":"EUR"},"grandTotal":{"amount":"99","currency":"EUR"}}'
    $foreignLines='[{"id":"invoice_line_secret","description":"foreign-secret-line","quantity":"1","unitPrice":{"amount":"999","currency":"USD"},"discountAmount":{"amount":"0","currency":"USD"},"taxAmount":{"amount":"0","currency":"USD"},"lineTotal":{"amount":"999","currency":"USD"}}]'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO invoices.Invoices(WorkspaceId,InvoiceId,InvoiceNumber,BuyerType,BuyerId,SellerSnapshotJson,BuyerSnapshotJson,LifecycleState,DeliveryState,IssueDate,DueDate,Currency,ExchangeRateSnapshotJson,PaymentTerms,CreationIntentId,LinesJson,TotalsJson,SourceLinksJson,ResourceVersion,IdempotencyKey,CreatedAt,UpdatedAt,IssuedAt,IssueFailureCode,IssueEvidenceJson,DiscardedAt,VoidedAt,VoidReason) VALUES
('$($script:WorkspaceId)','$invoiceFull','INV-2026-0001','CONTACT','contact_invoice_a',N'$seller',N'$buyer','ISSUED','SENT','2026-08-30','2026-09-29','USD',N'$rate','NET30','intent_a',N'$lines',N'$totals',N'$sourceLinks',7,'idem-invoice-read-full-0001','2026-08-30T10:00:00+00:00','2026-08-30T11:00:00+00:00','2026-08-30T10:30:00+00:00','ISSUE_PROVIDER_TIMEOUT',N'$evidence','2026-08-30T12:00:00+00:00','2026-08-30T13:00:00+00:00','Voided by seller'),
('$($script:WorkspaceId)','$invoiceMinimal',NULL,'ORGANIZATION_ACCOUNT','organization_invoice_b',N'$seller',N'$buyer','DRAFT','NOT_SENT',NULL,NULL,'EUR',NULL,NULL,NULL,N'$minimalLines',N'$minimalTotals',N'{}',0,'idem-invoice-read-minimal-01','2026-08-29T00:00:00+00:00','2026-08-29T00:00:00+00:00',NULL,NULL,NULL,NULL,NULL,NULL),
('$foreignWorkspace','$invoiceForeign','INV-FOREIGN-SECRET','CONTACT','contact_invoice_a',N'$seller',N'$buyer','ISSUED','SENT',NULL,NULL,'USD',NULL,'foreign-secret-terms',NULL,N'$foreignLines',N'$minimalTotals',N'{}',1,'idem-invoice-read-foreign-01','2026-08-29T00:00:00+00:00','2026-08-29T00:00:00+00:00',NULL,NULL,NULL,NULL,NULL,NULL);
"@

    # 5,6,8,9,10,13. WORKSPACE reads, Workspace isolation, exact shape and exact required/optional fields
    Set-Scope $roleId 'Workspace';$list=Invoke-Invoice '/invoices';$detail=Invoke-Invoice "/invoices/$invoiceFull"
    Add-Result 'WORKSPACE list succeeds' '200' ([string]$list.Status)
    Add-Result 'WORKSPACE list count' '2' ([string]$list.Body.Count)
    Add-Result 'WORKSPACE detail succeeds' '200' ([string]$detail.Status)
    Add-Result 'foreign Workspace record excluded' 'False' ([string]($list.Body.id-contains$invoiceForeign))
    Add-Result 'foreign detail nondisclosing' '404' ([string](Invoke-Invoice "/invoices/$invoiceForeign").Status)
    Add-Result 'foreign values never emitted' 'True' ([string](($list.Raw-notmatch'INV-FOREIGN-SECRET|foreign-secret')-and($detail.Raw-notmatch'INV-FOREIGN-SECRET|foreign-secret')))
    Add-Result 'full document exact shape' 'buyerRef,buyerSnapshot,createdAt,creationIntentId,currency,deliveryState,discardedAt,dueDate,exchangeRateSnapshot,id,idempotencyKey,invoiceNumber,issuedAt,issueDate,issueEvidence,issueFailureCode,lifecycleState,lines,paymentTerms,sellerSnapshot,sourceLinks,totals,updatedAt,version,voidedAt,voidReason,workspaceId' (($detail.Body.PSObject.Properties.Name|Sort-Object)-join',')
    $minimal=@($list.Body|Where-Object id -eq $invoiceMinimal)[0]
    Add-Result 'optional fields omitted when absent' 'buyerRef,buyerSnapshot,createdAt,currency,deliveryState,id,idempotencyKey,lifecycleState,lines,sellerSnapshot,sourceLinks,totals,updatedAt,version,workspaceId' (($minimal.PSObject.Properties.Name|Sort-Object)-join',')
    Add-Result 'empty sourceLinks emitted as empty object' '0' ([string]@($minimal.sourceLinks.PSObject.Properties).Count)
    $listFullShape=((@($list.Body|Where-Object id -eq $invoiceFull)[0]).PSObject.Properties.Name|Sort-Object)-join','
    $detailShape=($detail.Body.PSObject.Properties.Name|Sort-Object)-join','
    Add-Result 'list and detail share one representation' $detailShape $listFullShape

    # 8. exact enums, money, snapshots and version emitted as persisted, never recomputed
    Add-Result 'scalar contract values exact' 'ISSUED|SENT|USD|7|INV-2026-0001|2026-08-30|2026-09-29|NET30' "$($detail.Body.lifecycleState)|$($detail.Body.deliveryState)|$($detail.Body.currency)|$($detail.Body.version)|$($detail.Body.invoiceNumber)|$($detail.Body.issueDate)|$($detail.Body.dueDate)|$($detail.Body.paymentTerms)"
    Add-Result 'buyerRef exact' 'CONTACT|contact_invoice_a' "$($detail.Body.buyerRef.type)|$($detail.Body.buyerRef.id)"
    Add-Result 'totals emitted as persisted' '402|40.20|72.36|0.01|434.17' "$($detail.Body.totals.subtotal.amount)|$($detail.Body.totals.discountTotal.amount)|$($detail.Body.totals.taxTotal.amount)|$($detail.Body.totals.roundingAdjustment.amount)|$($detail.Body.totals.grandTotal.amount)"
    Add-Result 'line snapshot emitted as persisted' '1|invoice_line_a|4|100.50|434.16' "$($detail.Body.lines.Count)|$($detail.Body.lines[0].id)|$($detail.Body.lines[0].quantity)|$($detail.Body.lines[0].unitPrice.amount)|$($detail.Body.lines[0].lineTotal.amount)"
    Add-Result 'nested snapshots and links projected' 'Unicore Seller|Acme Buyer|order_invoice_a|2|CONNECTED_PROVIDER|1' "$($detail.Body.sellerSnapshot.displayName)|$($detail.Body.buyerSnapshot.displayName)|$($detail.Body.sourceLinks.orderId)|$($detail.Body.sourceLinks.paymentScheduleLineIds.Count)|$($detail.Body.exchangeRateSnapshot.source)|$($detail.Body.issueEvidence.Count)"
    Add-Result 'UTC timestamps emitted' 'True' ([string](($detail.Raw-match'"createdAt":"[^"]+Z"')-and($detail.Raw-match'"issuedAt":"[^"]+Z"')))

    # 14,15. exact admitted filters (none) and no invented ordering guarantee
    Add-Result 'unadmitted query parameters ignored not filtered' '2' ([string](Invoke-Invoice '/invoices?buyerId=contact_invoice_a&limit=1&cursor=x&sort=createdAt').Body.Count)
    Add-Result 'unknown detail nondisclosing' '404' ([string](Invoke-Invoice '/invoices/invoice_unknown').Status)

    # 3,4. capability required and capability denial precedes malformed path validation
    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='invoices.read'"
    Add-Result 'missing capability denies list' '403' ([string](Invoke-Invoice '/invoices').Status)
    Add-Result 'missing capability denies detail' '403' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)
    Add-Result 'capability denial precedes malformed path' '403' ([string](Invoke-Invoice '/invoices/%20bad').Status)
    Add-Result 'capability denial precedes unknown identifier' '403' ([string](Invoke-Invoice '/invoices/invoice_unknown').Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities(RoleId,Capability)VALUES('$roleId','invoices.read')"
    Add-Result 'malformed path nondisclosing once authorized' '404' ([string](Invoke-Invoice '/invoices/%20bad').Status)

    # 7. OWN/TEAM/CUSTOM fail closed - no authoritative Invoice ownership fact exists
    foreach($scope in @('Own','Team','Custom')){Set-Scope $roleId $scope;Add-Result "$($scope.ToUpperInvariant()) list fails closed" '0' ([string](Invoke-Invoice '/invoices').Body.Count);Add-Result "$($scope.ToUpperInvariant()) detail fails closed" '404' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)}
    Set-Scope $roleId 'Workspace'

    # 10,11,12. representation-specific field security
    Clear-Fields;Hide-Field $roleId 'lifecycleState'
    Add-Result 'required hidden field fails list closed' '403' ([string](Invoke-Invoice '/invoices').Status)
    Add-Result 'required hidden field fails detail closed' '403' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)
    Clear-Fields;Hide-Field $roleId 'paymentTerms'
    $hiddenList=Invoke-Invoice '/invoices';$hiddenDetail=Invoke-Invoice "/invoices/$invoiceFull"
    Add-Result 'optional hidden field still returns 200' '200' ([string]$hiddenDetail.Status)
    Add-Result 'optional hidden field omitted from detail' 'False' ([string]($hiddenDetail.Body.PSObject.Properties.Name-contains'paymentTerms'))
    Add-Result 'optional hidden field omitted from list' 'True' ([string]($hiddenList.Raw-notmatch'paymentTerms'))
    Clear-Fields;Hide-Field $roleId 'planId'
    Add-Result 'undeclared foreign field does not affect Invoices' '200' ([string](Invoke-Invoice '/invoices').Status)
    Clear-Fields

    # 16. contract-invalid persisted nested state fails closed and emits no partial Invoice
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET LinesJson=N'[]' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceFull'"
    $emptyLines=Invoke-Invoice "/invoices/$invoiceFull";Add-Result 'lines minItems violation fails closed' '500' ([string]$emptyLines.Status);Add-Result 'lines violation emits no partial Invoice' 'True' ([string]($emptyLines.Raw-notmatch'INV-2026-0001'))
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET LinesJson=N'[{`"id`":`"invoice_line_a`",`"description`":`"Consulting`",`"quantity`":`"4`",`"unitPrice`":{`"amount`":`"100.50`",`"currency`":`"USD`"},`"discountAmount`":{`"amount`":`"40.20`",`"currency`":`"USD`"},`"taxAmount`":{`"amount`":`"72.36`",`"currency`":`"USD`"},`"lineTotal`":{`"amount`":`"434.16`",`"currency`":`"USD`"},`"unexpected`":`"x`"}]' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceFull'"
    Add-Result 'additionalProperties violation fails closed' '500' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)
    Add-Result 'corrupt record fails whole list closed' '500' ([string](Invoke-Invoice '/invoices').Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET LinesJson=N'$lines', TotalsJson=N'{`"subtotal`":{`"amount`":`"4.0e2`",`"currency`":`"USD`"},`"discountTotal`":{`"amount`":`"40.20`",`"currency`":`"USD`"},`"taxTotal`":{`"amount`":`"72.36`",`"currency`":`"USD`"},`"grandTotal`":{`"amount`":`"434.17`",`"currency`":`"USD`"}}' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceFull'"
    Add-Result 'non-decimal money string fails closed' '500' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)
    Invoke-SqlNonQuery -Database $DatabaseName -Query "UPDATE invoices.Invoices SET TotalsJson=N'$totals', LifecycleState='DRAFT' WHERE WorkspaceId='$($script:WorkspaceId)' AND InvoiceId='$invoiceFull'"
    Add-Result 'restored fixture reads cleanly' '200' ([string](Invoke-Invoice "/invoices/$invoiceFull").Status)

    # 19. no mutation routes on the Invoice surface
    foreach($case in @(@('POST','/invoices'),@('POST','/invoices/drafts'),@('PATCH',"/invoices/$invoiceFull"),@('PATCH',"/invoices/$invoiceFull/draft"),@('DELETE',"/invoices/$invoiceFull"),@('POST',"/invoices/$invoiceFull/issue"),@('POST',"/invoices/$invoiceFull/void"),@('POST',"/invoices/$invoiceFull/send"),@('POST',"/invoices/$invoiceFull/credit-notes"))){Add-Result "no mutation $($case[0]) $($case[1])" 'True' ([string]((Invoke-Api -Method $case[0] -Path $case[1] -Token $script:Token -WorkspaceId $script:WorkspaceId -Body '{}').Status-in 404,405))}
    Add-Result 'no unadmitted Invoice read route' 'True' ([string]((Invoke-Invoice "/invoices/$invoiceFull/issue-readiness").Status-eq 404 -and (Invoke-Invoice '/invoice-deliveries').Status-eq 404 -and (Invoke-Invoice '/credit-notes').Status-eq 404 -and (Invoke-Invoice '/receivables').Status-eq 404))

    # 17,18,20,21. source-level owner-boundary proof
    $files=Get-ChildItem $invoicesRoot -Recurse -File|Where-Object Extension -eq '.cs';$source=($files|ForEach-Object{Get-Content $_.FullName -Raw})-join"`n"
    Add-Result 'no foreign DbContext' 'True' ([string]($source-notmatch'PaymentsDbContext|OrdersDbContext|QuotesDbContext|CustomersDbContext|ContactsDbContext|ProductsDbContext|ShippingDbContext|FulfillmentDbContext|CrmDbContext'))
    Add-Result 'no Payments or Orders runtime lookup' 'True' ([string]($source-notmatch'UnicoreCRM\.Sales|UnicoreCRM\.Crm|UnicoreCRM\.Fulfillment|Billing\.Payments|IPaymentsPersistence|IOrders'))
    Add-Result 'no external provider or tax runtime call' 'True' ([string]($source-notmatch'HttpClient|ProviderClient|TaxEngine|Webhook'))
    Add-Result 'no mutation workflow outbox idempotency implementation' 'True' ([string]($source-notmatch'MapPost|MapPatch|MapDelete|MapPut|IWorkflow|Outbox'))
    # SaveChanges is admitted only for the frozen read-evidence append, nowhere else in Invoices.
    $saveChangesFiles=(Get-ChildItem $invoicesRoot -Recurse -File -Filter *.cs|Where-Object{(Get-Content $_.FullName -Raw)-match'SaveChangesAsync'}|ForEach-Object Name|Sort-Object)-join','
    Add-Result 'SaveChanges confined to read-audit append' 'EfInvoicesPersistence.cs,InvoiceReadAudit.cs,InvoicesApplication.cs' $saveChangesFiles
    Add-Result 'frozen READ_ACCESS_LOG read audit table present' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='invoices' AND TABLE_NAME='ReadAuditRecords'"))
    $routes=Get-Content(Join-Path $invoicesRoot 'Contracts/InvoiceEndpoints.cs')-Raw
    Add-Result 'exactly two Invoice GET routes' '2' ([string]([regex]::Matches($routes,'\.MapGet\(').Count))
    $persistence=Get-Content(Join-Path $invoicesRoot 'Infrastructure/Persistence/EfInvoicesPersistence.cs')-Raw
    Add-Result 'no invented ordering pagination or filter' 'True' ([string]($persistence-notmatch'OrderBy|OrderByDescending|Skip|Take'))
    $migration=(Get-ChildItem(Join-Path $invoicesRoot 'Infrastructure/Persistence/Migrations')-Filter '*_InvoicesReadCore.cs'|ForEach-Object{Get-Content $_.FullName -Raw})-join"`n"
    Add-Result 'migration declares no foreign key' 'True' ([string]($migration-notmatch'ForeignKey'))
    Add-Result 'migration owns only the invoices schema' 'True' ([string](($migration-match'name: "invoices"')-and($migration-notmatch'schema: "payments"|schema: "orders"|schema: "sales"|schema: "crm"')))
}catch{$script:Failed++;[void]$script:Results.Add("FAIL | verifier execution | $($_.Exception.Message)");if(Test-Path $logPath){[void]$script:Results.Add((Get-Content $logPath -Tail 40)-join"`n")}}
finally{if($hostProcess -and -not $hostProcess.HasExited){Stop-Process -Id $hostProcess.Id -Force;$hostProcess.WaitForExit()};if(-not $KeepDatabase){try{Invoke-SqlNonQuery -Query "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;"}catch{}};Remove-Item Env:ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue}
$script:Results|ForEach-Object{Write-Host $_};Write-Host "INVOICE READ CORE RESULT: PASS=$script:Passed FAIL=$script:Failed";if($script:Failed -gt 0){exit 1}
