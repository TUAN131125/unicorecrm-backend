#requires -Version 7.0
# Read-only evidence from the owner-approved database copy; no mutation commands.
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$evidence=Join-Path $root 'task-runtime/shared-smtp-quote/anhduong'
$context=Get-Content (Join-Path $evidence 'database-context.json') -Raw|ConvertFrom-Json
$journey=Get-Content (Join-Path $evidence 'journey-context.json') -Raw|ConvertFrom-Json
if($context.database -notmatch '^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$' -or $journey.database -cne $context.database){throw 'Unexpected test database'}
$connection=[System.Data.SqlClient.SqlConnection]::new("Server=$($context.server);Database=$($context.database);Integrated Security=True;TrustServerCertificate=True")
function ReadRows([string]$Query,[hashtable]$Parameters=@{}){
 $cmd=$connection.CreateCommand();$cmd.CommandText=$Query
 foreach($e in $Parameters.GetEnumerator()){[void]$cmd.Parameters.AddWithValue($e.Key,$e.Value)}
 $r=$cmd.ExecuteReader();try{$rows=@();while($r.Read()){$row=@{};for($i=0;$i -lt $r.FieldCount;$i++){$row[$r.GetName($i)]=$(if($r.IsDBNull($i)){$null}else{$r.GetValue($i)})};$rows+=[pscustomobject]$row};return $rows}finally{$r.Dispose();$cmd.Dispose()}
}
try{
 $connection.Open()
 $p=@{'@live'=$journey.liveQuote.id;'@staff'=$journey.staff.quoteId;'@customer'=$journey.customer.quoteId}
 $quotes=@(ReadRows 'SELECT QuoteId,QuoteNumber,Status,ResourceVersion,Currency,GrandTotalAmount FROM quotes.Quotes WHERE QuoteId IN (@live,@staff,@customer)' $p)
 $documents=@(ReadRows 'SELECT QuoteId,DocumentId,PdfSha256,FileSize,PolicyVersion FROM quotes.Documents WHERE QuoteId IN (@live,@staff,@customer)' $p)
 $acceptances=@(ReadRows 'SELECT QuoteId,AcceptanceId,Source,AssuranceLevel,OrderId,AutoConfirmRequested FROM quotes.SalesAcceptances WHERE QuoteId IN (@live,@staff,@customer)' $p)
 $orders=@(ReadRows 'SELECT SourceQuoteId,OrderId,State,Currency,GrandTotalAmount FROM orders.Orders WHERE SourceQuoteId IN (@live,@staff,@customer)' $p)
 $plans=@(ReadRows 'SELECT p.OrderId,p.PaymentPlanId,p.State FROM payments.PaymentPlans p JOIN orders.Orders o ON o.OrderId=p.OrderId WHERE o.SourceQuoteId IN (@live,@staff,@customer)' $p)
 $liveInvitations=@(ReadRows 'SELECT InvitationId,DocumentId,DeliveryStatus,DeliveryErrorCode,DeliveredAt,ConsentedAt FROM quotes.AcceptanceInvitations WHERE QuoteId=@live' @{'@live'=$journey.liveQuote.id})
 $liveEvidence=@(ReadRows "SELECT JSON_VALUE(value,'$.evidenceType') EvidenceType,JSON_VALUE(value,'$.documentAuthority') DocumentAuthority FROM quotes.Quotes CROSS APPLY OPENJSON(DeliveryHistoryJson) WHERE QuoteId=@live" @{'@live'=$journey.liveQuote.id})
 $paymentRecords=(ReadRows 'SELECT COUNT(*) C FROM payments.PaymentRecords p JOIN orders.Orders o ON o.OrderId=p.OrderId WHERE o.SourceQuoteId IN (@live,@staff,@customer)' $p)[0].C
 $source=@(ReadRows "SELECT QuoteId,Status,ResourceVersion,GrandTotalAmount,CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),LineItemsJson+COALESCE(PaymentAgreementJson,''))),2) CommercialHash FROM [UnicoreCRM_Development].quotes.Quotes WHERE QuoteId='quote_ed19adbfe15842eaa2adfbe2e6c281ef'")[0]
 $role='role_69cacf3b860f42309dba4dceb9b63514'
 $roleCapabilities=@(ReadRows 'SELECT Capability FROM access.RoleCapabilities WHERE RoleId=@role' @{'@role'=$role})
 $roleAssignments=@(ReadRows "SELECT RoleId FROM access.MembershipRoleAssignments WHERE MembershipId='wsm_f018446a88674b4c9327a62c3919838f'")
 $sourceRoleCount=(ReadRows 'SELECT COUNT(*) C FROM [UnicoreCRM_Development].access.Roles WHERE RoleId=@role' @{'@role'=$role})[0].C
 $unchanged=$source.ResourceVersion -eq $context.sourceQuoteBefore.ResourceVersion -and $source.Status -ceq $context.sourceQuoteBefore.Status -and $source.CommercialHash -ceq $context.sourceQuoteBefore.CommercialHash
 $checks=@{
  sourceQuoteUnchanged=$unchanged
  threeIssuedDocuments=($documents.Count -eq 3)
  livePdfHashMatches=(@($documents|Where-Object{$_.QuoteId -ceq $journey.liveQuote.id -and $_.PdfSha256 -ceq $journey.liveQuote.pdfHash}).Count -eq 1)
  threeUnverifiedAcceptances=($acceptances.Count -eq 3 -and @($acceptances|Where-Object{$_.AssuranceLevel -cne 'ORDINARY_SALES_UNVERIFIED'}).Count -eq 0)
  threeConfirmedOrders=($orders.Count -eq 3 -and @($orders|Where-Object{$_.State -cne 'CONFIRMED' -or $_.GrandTotalAmount -ne 51500000 -or $_.Currency -cne 'VND'}).Count -eq 0)
  threeActivePlans=($plans.Count -eq 3 -and @($plans|Where-Object{$_.State -cne 'ACTIVE'}).Count -eq 0)
  newRoleOnlyIssuesDocuments=($roleCapabilities.Count -eq 1 -and $roleCapabilities[0].Capability -ceq 'quotes.issueDocument')
  demoHasOriginalAndAdditionalRole=($roleAssignments.Count -eq 2 -and $role -cin $roleAssignments.RoleId -and 'role_d059f8fc6dd249e69dada218d3bc7a91' -cin $roleAssignments.RoleId)
  noNewRoleInDevelopment=($sourceRoleCount -eq 0)
  liveProviderAcceptedAndConsented=($liveInvitations.Count -eq 1 -and $liveInvitations[0].DeliveryStatus -ceq 'SENT' -and $null -ne $liveInvitations[0].DeliveredAt -and $null -ne $liveInvitations[0].ConsentedAt)
  liveProviderEvidenceBoundToIssuedPdf=($liveEvidence.Count -eq 1 -and $liveEvidence[0].EvidenceType -ceq 'PROVIDER_CONFIRMED_SENT' -and $liveEvidence[0].DocumentAuthority -ceq $journey.liveQuote.documentId)
  oneOrderPerQuote=(@($orders.SourceQuoteId|Select-Object -Unique).Count -eq 3)
  noPaymentRecordCreated=($paymentRecords -eq 0)
 }
 @{database=$context.database;quotes=$quotes;documents=$documents;acceptances=$acceptances;orders=$orders;plans=$plans;liveInvitations=$liveInvitations;liveEvidence=$liveEvidence;sourceQuote=$source;checks=$checks;sqlWrites=0}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $evidence 'sql-readback.json')
 if(@($checks.Values|Where-Object{$_ -ne $true}).Count){throw 'SQL evidence check failed'}
 Write-Output ('SQL readback PASS: '+$checks.Count+' checks; 0 writes.')
}finally{$connection.Dispose()}
