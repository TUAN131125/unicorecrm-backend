#requires -Version 7.0
[CmdletBinding()]
param()
$scannerEvidenceKind='api'
. (Join-Path $PSScriptRoot 'scanner/QuoteProofScannerVerificationRuntime.ps1')

try{
 $before=Snapshot
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false';QuoteProofScan__Enabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 ProvisionSecurityMember $emails[0] @('workspace.context.resolve','quotes.read','quotes.create','quotes.update','quotes.issueDocument','quotes.submitAcceptanceEvidence','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','contacts.read','contacts.create')
 ProvisionSecurityMember $emails[1] @('workspace.context.resolve','quotes.read','quotes.update','quotes.approve','quotes.readAcceptanceEvidence','contacts.read','studio.read','studio.configure','access.read','access.configure')
 ProvisionSecurityMember $emails[2] @('workspace.context.resolve','quotes.read','contacts.read')
 $script:workspace=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=$securityKey}).Rows[0].WorkspaceId
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 StartHost;LoginAll
 $contact=Api $emails[0] POST '/contacts' @{fullName="KHÁCH HÀNG SYNTHETIC SCANNER $run";workEmail='b62-buyer@example.test'} "b62-buyer-$run";Check $contact 201;$script:buyer=$contact.body.result.contact.id
 $cfg=Api $emails[1] GET '/workspace-configuration';Check $cfg 200
 $business=$cfg.body.businessInformation;$business.displayName='SYNTHETIC SCANNER SELLER';$business.legalName='CÔNG TY SYNTHETIC — CHỈ KIỂM THỬ';$business.email='b62-seller@example.test';$business.taxId='';$business.logoReference=''
 $address=@{id="b62_registered_$run";name='Địa chỉ đăng ký synthetic';purposes=@('REGISTERED');addressLine1='ĐỊA CHỈ SYNTHETIC — KHÔNG DÙNG THỰC';addressLine2='';countryCode='VN';provinceCode='';districtCode='';wardCode='';postalCode='';contactName='';contactPhone='';active=$true}
 Check (Api $emails[1] PATCH '/workspace-configuration/business-information' @{businessInformation=$business;addresses=@($address)} "b62-seller-$run" $cfg.body.revision) 200
 $draft=Assessed (DraftQuote 'scanner') 'scanner';$issued=Issue $draft 'scanner';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($draft.id)/send" @{deliveryId="b62_sent_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic scanner test only'} "b62-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
 $base="/quotes/$($quote.id)/acceptance-cases"
 $binding=@{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
 $created=Api $emails[0] POST $base $binding "b62-case-$run" $quote.resourceVersion;Check $created 201;$case=$created.body.result.case
 $fixture=Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf';$bytes=[IO.File]::ReadAllBytes($fixture)
 $uploaded=Upload $quote.id $case.caseId $bytes "b62-upload-$run" $case.resourceVersion;Check $uploaded 201;$proof=$uploaded.body.result.proof
 $proofPath="$base/$($case.caseId)/proofs/$($proof.proofId)"
 Check (ProofContent $proofPath) 409 'PROOF_CONTENT_BLOCKED';Check (Api $emails[1] GET ($proofPath+'/scans')) 200
 Check (ProofContent $proofPath $emails[1]) 403;Check (Api $emails[2] GET ($proofPath+'/scans')) 403
 Stop-DemoHost;ScanPolicy;StartHost;LoginAll
 $clean=WaitScan $proofPath 'CLEAN'
 Assert-Demo ($clean.contentAvailable -and $clean.reviewable -and $clean.structuralStatus -ceq 'PASS' -and $clean.malwareVerdict -ceq 'CLEAN') 'B62_CLEAN' 'Real parser/scanner binding required'
 $file=ProofContent $proofPath;Check $file 200
 Assert-Demo ($file.sha256 -ceq $proof.sha256 -and $file.bytes -eq $proof.fileSize -and $file.disposition.StartsWith('attachment')) 'B62_DOWNLOAD' 'Downloaded immutable bytes must match'
 Check (ProofContent $proofPath $emails[1]) 403
 $history=Api $emails[1] GET ($proofPath+'/scans');Check $history 200;Assert-Demo ($history.body.items.Count -eq 1 -and $history.body.items[0].status -ceq 'CLEAN') 'B62_HISTORY' 'One terminal scan decision'
 $replay=Upload $quote.id $case.caseId $bytes "b62-upload-$run" 1;Check $replay 201
 Assert-Demo ($replay.body.outcome -ceq 'REPLAYED' -and $replay.body.result.proof.proofId -ceq $proof.proofId) 'B62_REPLAY' 'No new proof from replay'
 $invalid=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/active.pdf'))
 $bad=Upload $quote.id $case.caseId $invalid "b62-active-$run" 2;Check $bad 201;$badPath="$base/$($case.caseId)/proofs/$($bad.body.result.proof.proofId)"
 $null=WaitScan $badPath 'INVALID_PDF';Check (Api $emails[1] GET ($badPath+'/scans')) 200;Check (ProofContent $badPath) 409
 Stop-DemoHost;ScanPolicy 'b62-policy-v2';EnvValue 'QuoteProofScanVerification__Enabled' 'true';EnvValue 'QuoteProofScanVerification__WorkspaceId' $script:workspace;EnvValue 'QuoteProofScanVerification__PauseAfterClaimMilliseconds' '6000';StartHost;LoginAll
 Check (ProofContent $proofPath) 409
 $null=WaitScan $proofPath 'CLEAN';Check (ProofContent $proofPath) 200
 $history=Api $emails[1] GET ($proofPath+'/scans');Check $history 200;Assert-Demo ($history.body.items.Count -eq 2) 'B62_RESCAN' 'Policy change retains both scan results'
 Stop-DemoHost;ScanPolicy 'b62-stale-signature-v3' 1;EnvValue 'QuoteProofScanVerification__PauseAfterClaimMilliseconds' '0';StartHost;LoginAll
 Check (ProofContent $proofPath) 409
 $exhausted=WaitScan $proofPath 'NEEDS_INTERVENTION' 60
 Check (Api $emails[1] GET ($proofPath+'/scans')) 200;Check (ProofContent $proofPath) 409
 Stop-DemoHost;ScanPolicy 'b62-recovered-v4' 24;StartHost;LoginAll
 $stuck=Api $emails[0] GET ($proofPath+'/security');Check $stuck 200;Assert-Demo ($stuck.body.scanStatus -ceq 'NEEDS_INTERVENTION') 'B62_NO_RESET' 'New policy cannot reset retry budget'
 $newBytes=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% synthetic recovery proof $run`n"))
 $recovery=Upload $quote.id $case.caseId $newBytes "b62-recovery-$run" 3;Check $recovery 201;$recoveryProof=$recovery.body.result.proof;$recoveryPath="$base/$($case.caseId)/proofs/$($recoveryProof.proofId)"
 $null=WaitScan $recoveryPath 'CLEAN';$recovered=ProofContent $recoveryPath;Check $recovered 200;Assert-Demo ($recovered.sha256 -ceq $recoveryProof.sha256) 'B62_RECOVERY_HASH' 'New proof receives a genuine scan'
 Stop-DemoHost;StartHost;LoginAll
 Check (ProofContent $recoveryPath) 200
 $readiness=Api $emails[0] GET "/quotes/$($quote.id)/customer-acceptance?documentId=$($document.documentId)";Check $readiness 200
 Assert-Demo ($readiness.body.status -ceq 'BLOCKED_CONTRACT' -and $readiness.body.blockerCodes -contains 'BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE' -and $readiness.body.blockerCodes -notcontains 'BLOCKED_CONTRACT_TRUSTED_SCANNER') 'B62_READINESS' 'Technical scan never verifies customer acceptance'
 $sql=SQLRows 'SELECT (SELECT COUNT(*) FROM quotes.AcceptanceProofScanAttempts WHERE WorkspaceId=@ws) Attempts,(SELECT COUNT(*) FROM quotes.AcceptanceProofScanResults WHERE WorkspaceId=@ws) Results,(SELECT COUNT(*) FROM quotes.Quotes WHERE WorkspaceId=@ws AND Status=''ACCEPTED'') Accepted,(SELECT COUNT(*) FROM deals.Deals WHERE WorkspaceId=@ws AND WonAt IS NOT NULL) Won' @{'@ws'=$script:workspace}
 $sql|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'scan-sql-counts.json')
 $status='PASS'
}finally{
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;proofId=$proof.proofId;recoveryProofId=$recoveryProof.proofId;sha256=$proof.sha256;requests=$cases.Count;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.2 API/SQL $script:stateDirectory"
}
