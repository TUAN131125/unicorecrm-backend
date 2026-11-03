#requires -Version 7.0
# Shared guarded Development verification harness. Credentials remain process-only.
if($scannerEvidenceKind -notin @('api','advanced','security','fault','read')){throw 'Invalid scanner evidence scope'}
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot)
$verificationScripts=Join-Path $repo 'scripts'
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) ('task-runtime/quote-proof-scanner/'+$scannerEvidenceKind+'-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force $script:stateDirectory)
. (Join-Path $verificationScripts 'demo/DemoRuntime.ps1')
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $verificationScripts 'verify-quote-approval-synthetic.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Verification helper parse failure'}
$helpers=@('SetPolicy','Draft','RequestApproval','EnvValue','OverrideArray','DisableBootstraps','StartHost','LoginAll','Api','Check','Snapshot','SQLRows')
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){if($definition.Name -in $helpers){. ([ScriptBlock]::Create($definition.Extent.Text))}}
function OverrideArray([string]$Prefix,[string[]]$Values){
 if(!$Values.Count){throw 'Empty override array is not admitted'}
 $key=$Prefix.Replace('__',':')
 $indices=@($configuration.Keys|Where-Object{$_ -match ('^'+[regex]::Escape($key)+':\d+$')}|ForEach-Object{[int]($_.Split(':')[-1])})
 $indices+=@([Environment]::GetEnvironmentVariables().Keys|Where-Object{$_ -match ('^'+[regex]::Escape($Prefix)+'__\d+$')}|ForEach-Object{[int](($_ -split '__')[-1])})
 $maximum=[Math]::Max($Values.Count-1, $(if($indices.Count){($indices|Measure-Object -Maximum).Maximum}else{0}))
 for($i=0;$i -le $maximum;$i++){EnvValue ($Prefix+'__'+$i) $Values[$i % $Values.Count]}
}
$configuration=Read-DemoConfiguration;$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Verified Development database required'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Machine").Rows[0].Machine
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL must be local'
$script:workspace='ws_096302f33ec94b3fb6ab9a41422d5531'
$emails=@('quote.creator@example.test','quote.approver@example.test','quote.approver2@example.test');$passwords=@{}
$secret=$configuration['IdentityAuth:RefreshTokenPepper'];if(!$secret){$secret=$configuration['DevelopmentDemoBootstrap:Password']}
Assert-Demo (!!$secret) 'BLOCKED_ENVIRONMENT' 'Existing process-only credential derivation required'
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
try{foreach($email in $emails){$passwords[$email]='B1-'+[Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes('quote-approval-synthetic-v1|'+$email)))+'!a9'}}finally{$hmac.Dispose();$secret=$null}
$original=@{};$script:tokens=@{};$script:token=$null;$script:httpEvidence=[Collections.Generic.List[object]]::new();$cases=[Collections.Generic.List[object]]::new()
$run=[guid]::NewGuid().ToString('N');$status='FAIL'
$securityKey='unicore-proof-scan-'+$run.Substring(0,8)
$securityParseTokens=$null;$securityParseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $verificationScripts 'verify-quote-delivery.ps1'),[ref]$securityParseTokens,[ref]$securityParseErrors)
$definition=$ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'ProvisionSecurityMember'},$false)[0]
. ([ScriptBlock]::Create($definition.Extent.Text.Replace('unicore-quote-approval-isolation',('unicore-b6-isolation-'+$run.Substring(0,8))).Replace("@('contacts','quotes')","@('contacts','quotes','deals')").Replace("@('crm')","@('crm','studio','people')").Replace('B2','B6')))
function LifecyclePolicy{
 $prefix="QuoteLifecyclePolicies__Workspaces__$($script:workspace)__"
 foreach($item in @{Version='b4-package-a-v1';AllowRevision='true';AllowExpiration='true';AllowArchive='true'}.GetEnumerator()){EnvValue ($prefix+$item.Key) $item.Value}
}

function DocumentPolicy{
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Version" 'b6-adjusted-package-a-v1'
 EnvValue "QuoteDocumentPolicies__Workspaces__$($script:workspace)__Enabled" 'true'
}
function DraftQuote([string]$Name,[string]$Amount='1000',[string]$Currency='VND'){
 $body=Draft "B6 synthetic $Name $run" $Amount;$body.currency=$Currency;$body.lineItems[0].unitPrice.currency=$Currency
 $body.validUntil=[DateTime]::UtcNow.Date.AddDays(30).ToString('yyyy-MM-dd')
 $r=Api $emails[0] POST '/quotes' $body "b6-create-$Name-$run";Check $r 201;return $r.body.result.quote
}
function Assessed($Quote,[string]$Name){
 $q=RequestApproval $Quote "b6-$Name-$run"
 if($q.approvalStatus -eq 'PENDING'){$r=Api $emails[1] POST "/quotes/$($q.id)/approve" @{} "b6-approve-$Name-$run" $q.resourceVersion;Check $r 200;$q=$r.body.result.quote}
 return $q
}
function Issue($Quote,[string]$Name,[string]$Actor=$emails[0],[string]$Reason=''){
 $body=@{};if($Reason){$body.reason=$Reason}
 return Api $Actor POST "/quotes/$($Quote.id)/documents/issue" $body "b6-issue-$Name-$run" $Quote.resourceVersion
}
function Download([string]$QuoteId,[string]$DocumentId,[string]$Name,[string]$Actor=$emails[0]){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098/quotes/$QuoteId/documents/$DocumentId/content")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'="b6-download-$Name-$run";'X-Correlation-Id'="b6-download-$Name-$run"}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{
   $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();$status=[int]$response.StatusCode
   if($status -eq 200){
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));$path=Join-Path $script:stateDirectory "$Name.pdf";[IO.File]::WriteAllBytes($path,$bytes)
    $value=@{status=$status;sha256=$hash;bytes=$bytes.Length;mediaType=[string]$response.Content.Headers.ContentType;cacheControl=[string]$response.Headers.CacheControl;fileName=[string]$response.Content.Headers.ContentDisposition.FileNameStar;path=$path}
   }else{$value=@{status=$status;body=([Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable)}}
   $cases.Add(@{actor=$Actor;method='GET';path="/quotes/$QuoteId/documents/$DocumentId/content";binary=$status -eq 200;response=$value});return $value
  }finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
function Upload([string]$QuoteId,[string]$CaseId,[byte[]]$Bytes,[string]$Key,[long]$Version,[string]$Actor=$emails[0],[string]$Mime='application/pdf',[hashtable]$Extra=@{},[string]$Name='synthetic-proof.pdf'){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098/quotes/$QuoteId/acceptance-cases/$CaseId/proofs")
 try{
  $headers=@{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'=('proof-'+[guid]::NewGuid().ToString('N'));'Idempotency-Key'=$Key;'If-Match'=('"'+$Version+'"');'X-Proof-File-Name'=[Uri]::EscapeDataString($Name);'X-Proof-Evidence-Type'='SUPPORTING_DOCUMENT'}
  foreach($h in $Extra.GetEnumerator()){$headers[$h.Key]=$h.Value}
  foreach($h in $headers.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $request.Content=[Net.Http.ByteArrayContent]::new($Bytes);$request.Content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new($Mime)
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{$value=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable)};$cases.Add(@{scenario='B61_BINARY_UPLOAD';actor=$Actor;quoteId=$QuoteId;caseId=$CaseId;response=$value});return $value}finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
function ScanPolicy([string]$Version='b62-adjusted-v1',[int]$Freshness=24){
 $prefix="QuoteProofScan__Workspaces__$($script:workspace)__"
 foreach($entry in @{Enabled='true';Version=$Version;SignatureFreshnessHours="$Freshness";FirstBackoffSeconds='1';SubsequentBackoffSeconds='1'}.GetEnumerator()){EnvValue ($prefix+$entry.Key) $entry.Value}
 EnvValue 'QuoteProofScan__Enabled' 'true'
 EnvValue 'QuoteProofScan__RuntimeManifestPath' (Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/runtime/trusted-runtime-manifest.json')
 EnvValue 'QuoteProofScan__RuntimeManifestSha256' ([IO.File]::ReadAllText((Join-Path (Split-Path $repo) 'task-runtime/quote-proof-scanner/trusted-runtime-manifest.sha256')).Trim())
}
function ProofContent([string]$Path,[string]$Actor=$emails[0]){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098$Path/content")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$Actor])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'=('scan-download-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'=('scan-download-'+[guid]::NewGuid().ToString('N'))}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{
   $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();$value=@{status=[int]$response.StatusCode}
   if($value.status -eq 200){$value.sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes));$value.bytes=$bytes.Length;$value.headers=@{};foreach($h in $response.Headers){$value.headers[$h.Key]=[string]::Join(',',$h.Value)};$value.disposition=[string]$response.Content.Headers.ContentDisposition}
   else{$value.body=[Text.Encoding]::UTF8.GetString($bytes)|ConvertFrom-Json -AsHashtable}
   $cases.Add(@{scenario='B62_PROTECTED_CONTENT';actor=$Actor;workspace=$script:workspace;path=$Path;response=$value});return $value
  }finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
function HistoryWire([string]$Path){
 $client=[Net.Http.HttpClient]::new();$request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,"http://127.0.0.1:5098$Path/scans")
 try{
  foreach($h in @{Authorization="Bearer $($script:tokens[$emails[1]])";'X-Workspace-Id'=$script:workspace;'X-Request-Id'=('scan-wire-'+[guid]::NewGuid().ToString('N'));'X-Correlation-Id'=('scan-wire-'+[guid]::NewGuid().ToString('N'))}.GetEnumerator()){[void]$request.Headers.TryAddWithoutValidation($h.Key,$h.Value)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try{$value=@{status=[int]$response.StatusCode;body=($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json -AsHashtable -DateKind String)};$cases.Add(@{scenario='B62_HISTORY_WIRE';response=$value});return $value}finally{$response.Dispose()}
 }finally{$request.Dispose();$client.Dispose()}
}
function WaitScan([string]$Path,[string]$State,[int]$Seconds=60){
 $end=[DateTime]::UtcNow.AddSeconds($Seconds)
 do{$r=Api $emails[0] GET ($Path+'/security');Check $r 200;if($r.body.scanStatus -cin ($State -split '\|')){return $r.body};Start-Sleep -Milliseconds 700}while([DateTime]::UtcNow -lt $end)
 throw "Scan did not reach $State; actual $($r.body.scanStatus)/$($r.body.reasonCode)"
}
function ScanRows([string]$ProofId){
 return SQLRows 'SELECT j.Status,j.LifetimeAttempts,j.EpochAttempts,j.ResourceVersion,(SELECT COUNT(*) FROM quotes.AcceptanceProofScanAttempts a WHERE a.WorkspaceId=j.WorkspaceId AND a.ProofId=j.ProofId) Attempts,(SELECT COUNT(*) FROM quotes.AcceptanceProofScanResults r WHERE r.WorkspaceId=j.WorkspaceId AND r.ProofId=j.ProofId) Results FROM quotes.AcceptanceProofScanJobs j WHERE j.WorkspaceId=@ws AND j.ProofId=@proof' @{'@ws'=$script:workspace;'@proof'=$ProofId}
}
function UploadFresh([string]$Name){
 $current=Api $emails[0] GET "$base/$($case.caseId)";Check $current 200
 $content=[byte[]]($bytes+[Text.Encoding]::ASCII.GetBytes("`n% synthetic $Name $run`n"))
 $r=Upload $quote.id $case.caseId $content "b62-$Name-$run" $current.body.resourceVersion;Check $r 201;return $r.body.result.proof
}
function DetectionPdf([byte[]]$Eicar){
 $text='BT /F1 12 Tf (SYNTHETIC SAFE SCANNER DETECTION TEST ONLY) Tj ET'
 $objects=@('<< /Type /Catalog /Pages 2 0 R >>','<< /Type /Pages /Kids [3 0 R] /Count 1 >>','<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>','<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',("<< /Length $($text.Length) >>`nstream`n$text`nendstream"),("<< /Length 68 >>`nstream`n"+[Text.Encoding]::ASCII.GetString($Eicar)+"`nendstream"))
 $stream=[IO.MemoryStream]::new();$offsets=[Collections.Generic.List[long]]::new()
 try{
  $part=[Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n");$stream.Write($part)
  for($i=0;$i -lt $objects.Count;$i++){$offsets.Add($stream.Length);$part=[Text.Encoding]::ASCII.GetBytes("$($i+1) 0 obj`n$($objects[$i])`nendobj`n");$stream.Write($part)}
  $xref=$stream.Length;$tail="xref`n0 $($objects.Count+1)`n0000000000 65535 f `n"
  foreach($offset in $offsets){$tail+=$offset.ToString('D10')+" 00000 n `n"}
  $tail+="trailer`n<< /Size $($objects.Count+1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n";$stream.Write([Text.Encoding]::ASCII.GetBytes($tail));return ,$stream.ToArray()
 }finally{$stream.Dispose()}
}
$script:scannerOriginalStartHost=(Get-Item Function:StartHost).ScriptBlock
function StartHost {
 foreach($name in @('host.log','host-error.log')){
  $current=Join-Path $script:stateDirectory $name
  if(Test-Path -LiteralPath $current){Copy-Item -LiteralPath $current -Destination (Join-Path $script:stateDirectory ([guid]::NewGuid().ToString('N')+'-'+$name))}
 }
 & $script:scannerOriginalStartHost
}
