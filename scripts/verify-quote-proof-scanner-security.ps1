#requires -Version 7.0
[CmdletBinding()]
param([switch]$Browser,[Parameter(Mandatory)][string]$FixtureResult)
$scannerEvidenceKind='security'
. (Join-Path $PSScriptRoot 'scanner/QuoteProofScannerVerificationRuntime.ps1')

try{
 $saved=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json -AsHashtable
 Assert-Demo ($saved.status -ceq 'PASS' -and $saved.workspaceKey.StartsWith('unicore-proof-scan-')) 'B62_FIXTURE' 'Only B6.2 synthetic fixtures admitted'
 $script:workspace=$saved.workspaceId;$securityKey=$saved.workspaceKey
 foreach($entry in @{ASPNETCORE_ENVIRONMENT='Development';DOTNET_ENVIRONMENT='Development';ASPNETCORE_URLS='http://127.0.0.1:5098';UNICORE_DEV_SEED_ENABLED='false';Development__ApplyMigrations='false';Workflows__InitialWorkspaceProvisioning__ResumeEnabled='false'}.GetEnumerator()){EnvValue $entry.Key $entry.Value}
 OverrideArray 'Frontend__AllowedOrigins' @('http://127.0.0.1:3044');SetPolicy 'b62-approval-v1';LifecyclePolicy;DocumentPolicy;ScanPolicy 'b62-recovered-v4'
 foreach($entry in @{QuoteVerifiedAcceptancePolicies='b6-approved-documentary-v1';QuoteProofStoragePolicies='b61-approved-v1'}.GetEnumerator()){
  EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Enabled" 'true';EnvValue "$($entry.Key)__Workspaces__$($script:workspace)__Version" $entry.Value
 }
 StartHost;LoginAll
 $source=Api $emails[0] GET "/quotes/$($saved.quoteId)";Check $source 200;$script:buyer=$source.body.buyerRef.id
 $draft=Assessed (DraftQuote 'scanner-security') 'scanner-security';$issued=Issue $draft 'scanner-security';Check $issued 201;$document=$issued.body.result.document
 $sent=Api $emails[0] POST "/quotes/$($draft.id)/send" @{deliveryId="b62_security_$run";channel='OTHER';evidenceType='USER_CONFIRMED_SENT';recipient='Synthetic security test only'} "b62-security-send-$run" $issued.body.version;Check $sent 200;$quote=$sent.body.result.quote
 $base="/quotes/$($quote.id)/acceptance-cases";$binding=@{documentId=$document.documentId;quoteRevision=$quote.quoteRevision;pdfSha256=$document.pdfSha256;commercialContentFingerprint=$document.commercialContentFingerprint}
 $created=Api $emails[0] POST $base $binding "b62-security-case-$run" $quote.resourceVersion;Check $created 201;$case=$created.body.result.case
 $bytes=[IO.File]::ReadAllBytes((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/fixtures/safe.pdf'))
 Assert-Demo (!(Get-NetTCPConnection -State Listen -LocalPort 5099 -ErrorAction SilentlyContinue)) 'B62_PORT' 'Secondary test port must be free'
 EnvValue 'ASPNETCORE_URLS' 'http://127.0.0.1:5099'
 $secondHost=Start-Process -FilePath (Join-Path $script:hostDirectory 'bin/Debug/net10.0/UnicoreCRM.ApiHost.exe') -WorkingDirectory $script:hostDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:stateDirectory 'second-host.log') -RedirectStandardError (Join-Path $script:stateDirectory 'second-host-error.log')
 EnvValue 'ASPNETCORE_URLS' 'http://127.0.0.1:5098'
 for($i=0;$i -lt 60;$i++){if(Get-NetTCPConnection -State Listen -LocalPort 5099 -ErrorAction SilentlyContinue){break};Start-Sleep -Milliseconds 500}
 Assert-Demo (!$secondHost.HasExited -and !!(Get-NetTCPConnection -State Listen -LocalPort 5099 -ErrorAction SilentlyContinue)) 'B62_SECOND_WORKER' 'Two actual worker hosts must be running'
 $concurrent=UploadFresh 'two-workers';$proofPath="$base/$($case.caseId)/proofs/$($concurrent.proofId)"
 $null=WaitScan $proofPath 'CLEAN';$concurrentSql=ScanRows $concurrent.proofId
 Assert-Demo ($concurrentSql.Attempts -eq 1 -and $concurrentSql.Results -eq 1) 'B62_CONCURRENT' 'Competing real workers produce one committed scan decision'
 Check (ProofContent $proofPath) 200
 Stop-Process -Id $secondHost.Id -ErrorAction Stop;$secondHost.WaitForExit();$secondHost=$null
 $http=[Net.Http.HttpClient]::new();try{$eicar=$http.GetByteArrayAsync('https://secure.eicar.org/eicar.com.txt').GetAwaiter().GetResult()}finally{$http.Dispose()}
 Assert-Demo ($eicar.Length -eq 68 -and [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($eicar)) -ceq '275A021BBFB6489E54D471899F7DB9D1663FC695EC2FE2A2C4538AABF651FD0F') 'B62_SAFE_DETECTION_IDENTITY' 'Official safe detection fixture must match'
 $testPdf=DetectionPdf $eicar
 $current=Api $emails[0] GET "$base/$($case.caseId)";Check $current 200
 $detected=Upload $quote.id $case.caseId $testPdf "b62-detection-$run" $current.body.resourceVersion;Check $detected 201;$unsafeProof=$detected.body.result.proof;$unsafePath="$base/$($case.caseId)/proofs/$($unsafeProof.proofId)"
 $unsafe=WaitScan $unsafePath 'UNSAFE' 60;Check (Api $emails[1] GET ($unsafePath+'/scans')) 200;Check (ProofContent $unsafePath) 409
 Check (Api $emails[0] POST ($proofPath+'/scans') @{scanStatus='CLEAN';isTrusted=$true}) 405
 $foreign=[string](Invoke-DemoSql 'SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=@key' @{'@key'=('unicore-b6-isolation-'+$securityKey.Split('-')[-1])}).Rows[0].WorkspaceId
 Assert-Demo (!!$foreign) 'B62_FOREIGN_FIXTURE' 'Official synthetic companion Workspace required'
 $own=$script:workspace;$script:workspace=$foreign
 Check (Api $emails[0] GET ($proofPath+'/scans')) 403;Check (ProofContent $proofPath) 403
 $script:workspace=$own
 $role=Api $emails[1] POST '/access/roles' @{name="B62 content hidden total $run";capabilities=@('workspace.context.resolve','quotes.read','quotes.readAcceptanceEvidence','quotes.readAcceptanceProofContent','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@(@{resourceKey='quotes';fieldKey='grandTotal';access='HIDDEN'})} "b62-fls-role-$run";Check $role 200
 $profile=$role.body.result.memberProfiles|Where-Object email -ceq $emails[2];$member=$role.body.result.members|Where-Object membershipId -ceq $profile.membershipId
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($role.body.aggregateId);teamIds=@()} "b62-fls-assign-$run" $memberVersion) 200
 Check (Api $emails[2] GET ($proofPath+'/scans')) 403;Check (ProofContent $proofPath $emails[2]) 403
 $restore=Api $emails[1] POST '/access/roles' @{name="B62 restricted restored $run";capabilities=@('workspace.context.resolve','quotes.read','contacts.read');dataScopes=@(@{resourceKey='quotes';scope='WORKSPACE'});fieldSecurity=@()} "b62-fls-restore-$run";Check $restore 200
 $memberVersion=[long](Invoke-DemoSql 'SELECT Version FROM access.MemberAccessVersions WHERE WorkspaceId=@ws AND MembershipId=@m' @{'@ws'=$script:workspace;'@m'=$member.membershipId}).Rows[0].Version
 Check (Api $emails[1] POST "/access/members/$($member.membershipId)/access" @{roleIds=@($restore.body.aggregateId);teamIds=@()} "b62-fls-restored-$run" $memberVersion) 200
 @{workers=2;concurrent=$concurrentSql;unsafe=$unsafe}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $script:stateDirectory 'security-sql.json')
 $status='PASS'
}finally{
 if($secondHost -and !$secondHost.HasExited){Stop-Process -Id $secondHost.Id;$secondHost.WaitForExit()}
 Stop-DemoHost
 foreach($key in $original.Keys){[Environment]::SetEnvironmentVariable($key,$original[$key])}
 $cases|ConvertTo-Json -Depth 65|Set-Content (Join-Path $script:stateDirectory 'cases.json')
 @{status=$status;workspaceId=$script:workspace;workspaceKey=$securityKey;quoteId=$quote.id;documentId=$document.documentId;caseId=$case.caseId;cleanProofId=$concurrent.proofId;unsafeProofId=$unsafeProof.proofId;directory=$script:stateDirectory}|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'result.json')
 Write-Host "$status B6.2 scanner security $script:stateDirectory"
}
