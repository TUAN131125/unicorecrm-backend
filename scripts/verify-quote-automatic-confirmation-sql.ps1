#requires -Version 7.0
[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidatePattern('^UnicoreCRM_OrderConfirmationTest_[A-Za-z0-9_]+$')][string]$DatabaseName,
 [string]$SqlServer='DESKTOP-ICH225R',
 [string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
if(!$EvidenceDirectory){$EvidenceDirectory=Join-Path (Split-Path (Split-Path $PSScriptRoot)) ('task-runtime/quote-detail-auto-confirm/'+$DatabaseName)}
[void](New-Item -ItemType Directory -Path $EvidenceDirectory -Force)
$connection=[Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True")
function ReadRows([string]$Query){
 $command=$connection.CreateCommand();$command.CommandText=$Query;$reader=$command.ExecuteReader();$rows=@()
 try{while($reader.Read()){$row=@{};for($i=0;$i -lt $reader.FieldCount;$i++){$row[$reader.GetName($i)]=$(if($reader.IsDBNull($i)){$null}else{$reader.GetValue($i)})};$rows+=[pscustomobject]$row}}finally{$reader.Dispose();$command.Dispose()}
 return $rows
}
$connection.Open()
try{
 $invariants=@(ReadRows @'
SELECT 'duplicate Orders' [Check],COUNT(*) Violations
FROM (SELECT WorkspaceId,SourceQuoteId FROM orders.Orders GROUP BY WorkspaceId,SourceQuoteId HAVING COUNT(*)>1) d
UNION ALL SELECT 'duplicate current Plans',COUNT(*)
FROM (SELECT WorkspaceId,OrderId FROM payments.PaymentPlans WHERE SupersededByPlanId IS NULL GROUP BY WorkspaceId,OrderId HAVING COUNT(*)>1) d
UNION ALL SELECT 'confirmed with missing/inactive Plan',COUNT(*) FROM orders.Orders o
LEFT JOIN payments.PaymentPlans p ON p.WorkspaceId=o.WorkspaceId AND p.OrderId=o.OrderId AND p.SupersededByPlanId IS NULL
WHERE o.State='CONFIRMED' AND (p.State IS NULL OR p.State<>'ACTIVE')
UNION ALL SELECT 'active Plan without confirmed Order',COUNT(*) FROM payments.PaymentPlans p
LEFT JOIN orders.Orders o ON o.WorkspaceId=p.WorkspaceId AND o.OrderId=p.OrderId WHERE p.State='ACTIVE' AND (o.State IS NULL OR o.State<>'CONFIRMED')
UNION ALL SELECT 'commercial fingerprint mismatch',COUNT(*) FROM orders.Orders o
JOIN quotes.SalesAcceptances a ON a.WorkspaceId=o.WorkspaceId AND a.AcceptanceId=o.SourceAcceptanceId WHERE o.CommercialSnapshotFingerprint<>a.CommercialFingerprint
UNION ALL SELECT 'auto-confirm attributed to human',COUNT(*) FROM orders.Orders WHERE ConfirmationOrigin='WORKSPACE_POLICY' AND ConfirmedBy<>'system:quote-policy'
UNION ALL SELECT 'receipt missing',COUNT(*) FROM orders.Orders o WHERE ConfirmationOrigin='WORKSPACE_POLICY'
AND NOT EXISTS(SELECT 1 FROM workflow.OrderConfirmationReceipts r WHERE r.WorkspaceId=o.WorkspaceId AND r.OrderId=o.OrderId)
UNION ALL SELECT 'payment satisfied without collection',COUNT(*) FROM payments.PaymentScheduleLines WHERE SatisfiedAmount<>0
UNION ALL SELECT 'durable auto draft',COUNT(*) FROM orders.Orders o JOIN quotes.SalesAcceptances a
ON a.WorkspaceId=o.WorkspaceId AND a.AcceptanceId=o.SourceAcceptanceId WHERE a.AutoConfirmRequested=1 AND o.State='DRAFT'
UNION ALL SELECT 'orphan creation evidence',COUNT(*) FROM orders.CreationEvidence e
WHERE NOT EXISTS(SELECT 1 FROM orders.Orders o WHERE o.WorkspaceId=e.WorkspaceId AND o.OrderId=e.OrderId)
UNION ALL SELECT 'orphan Plan events',COUNT(*) FROM payments.PlanEvents e
WHERE NOT EXISTS(SELECT 1 FROM payments.PaymentPlans p WHERE p.WorkspaceId=e.WorkspaceId AND p.PaymentPlanId=e.PlanId)
UNION ALL SELECT 'orphan receipts',COUNT(*) FROM workflow.OrderConfirmationReceipts r
WHERE NOT EXISTS(SELECT 1 FROM orders.Orders o WHERE o.WorkspaceId=r.WorkspaceId AND o.OrderId=r.OrderId)
'@)
 $invariants|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-invariants.json')
 $invariants|Format-Table
 $migrations=@(ReadRows @'
SELECT 'quotes' Owner,MigrationId,ProductVersion FROM quotes.__EFMigrationsHistory
UNION ALL SELECT 'orders',MigrationId,ProductVersion FROM orders.__EFMigrationsHistory
UNION ALL SELECT 'payments',MigrationId,ProductVersion FROM payments.__EFMigrationsHistory
UNION ALL SELECT 'workflow',MigrationId,ProductVersion FROM workflow.__EFMigrationsHistory
'@)
 $migrations|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-migrations.json')
 foreach($required in @('20261102014225_QuoteAutomaticConfirmationIntent','20261102014228_OrderPolicyConfirmationOrigin')){if($required -notin $migrations.MigrationId){throw "Missing migration $required"}}
 @(ReadRows 'SELECT AcceptanceId,QuoteId,Source,AssuranceLevel,ConversionStatus,OrderId,LastErrorCode,AutoConfirmRequested FROM quotes.SalesAcceptances')|ConvertTo-Json|Set-Content (Join-Path $EvidenceDirectory 'sql-acceptances.json')
 if(@($invariants|Where-Object Violations -ne 0).Count){throw 'SQL invariants failed'}
}finally{$connection.Dispose()}
