#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Migrate','Api','Browser')][string]$Mode='Api')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$script:hostDirectory=Join-Path $repo 'src/UnicoreCRM.ApiHost'
$script:stateDirectory=Join-Path (Split-Path $repo) 'task-runtime/quote-core/acceptance'
[void](New-Item -ItemType Directory -Force -Path $script:stateDirectory)
. (Join-Path $PSScriptRoot 'demo/DemoRuntime.ps1')
$script:httpEvidence=[Collections.Generic.List[object]]::new()
$cases=[Collections.Generic.List[object]]::new()
$configuration=Read-DemoConfiguration
$script:connectionString=$configuration['ConnectionStrings:UnicoreCRM']
$builder=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:connectionString)
Assert-Demo ($builder.DataSource -ceq 'DESKTOP-ICH225R' -and $builder.InitialCatalog -ceq 'UnicoreCRM_Development') 'BLOCKED_ENVIRONMENT' 'Existing verified SQL Development only'
$machine=(Invoke-DemoSql "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) Name").Rows[0].Name
Assert-Demo ($machine -ieq $env:COMPUTERNAME) 'BLOCKED_ENVIRONMENT' 'SQL Server must be local'
$fixture=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline/backend/provisioning-acceptance.json')|ConvertFrom-Json -AsHashtable
$review=Get-Content -Raw (Join-Path (Split-Path $repo) 'task-runtime/deal-pipeline-final-acceptance/api-review.json')|ConvertFrom-Json -AsHashtable
Assert-Demo ($fixture.workspaceId -ceq 'ws_772b487b87e84456b5020631fbaedd27') 'FIXTURE_REQUIRED' 'Existing synthetic workspace required'
$script:tables=@{Lead='leads.Leads';Contact='contacts.Contacts';Organization='organizations.Organizations';Customer='customers.Customers';Deal='deals.Deals';Task='tasks.Tasks';Activity='tasks.Activities';Product='products.Products';Support='support.SupportCases'}
$baselinePath=Join-Path $script:stateDirectory 'crm-before.json'
if(!(Test-Path $baselinePath)) {Get-DemoCounts|ConvertTo-Json -Depth 10|Set-Content $baselinePath}
$baseline=Get-Content -Raw $baselinePath|ConvertFrom-Json -AsHashtable
function Preserve {
    $after=Get-DemoCounts
    foreach($old in $baseline){
        if($old.workspaceId -ceq $fixture.workspaceId -and $old.module -ceq 'Product'){continue}
        $new=$after|Where-Object {$_.module -ceq $old.module -and $_.workspaceId -ceq $old.workspaceId}
        Assert-Demo ($new.count -eq $old.count -and $new.hash -ceq $old.hash) 'EXISTING_DATA_CHANGED' "$($old.module) $($old.workspaceId)"
    }
    $after|ConvertTo-Json -Depth 10|Set-Content (Join-Path $script:stateDirectory 'crm-after.json')
}
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $response=Invoke-DemoApi $Method $Path $Body $Key $Version
    if($response.body -is [array] -and $response.body.Count -gt 0 -and $response.body[0] -is [long]){$response.body=[Text.Encoding]::UTF8.GetString([byte[]]$response.body)|ConvertFrom-Json -AsHashtable}
    if($Path -notlike '/auth/*'){$cases.Add(@{request=@{method=$Method;path=$Path;body=$Body;idempotencyKey=$Key;expectedVersion=$Version;workspaceId=$script:workspace};response=$response})}
    return $response
}
function Require([string]$Method,[string]$Path,$Body=$null,[string]$Key='', $Version=$null){
    $response=Api $Method $Path $Body $Key $Version
    Assert-Demo ($response.status -in 200,201) 'API_FAILED' "$Method $Path HTTP $($response.status) $($response.body|ConvertTo-Json -Depth 12 -Compress)"
    return $response.body
}
function Login([string]$Email){
    $script:token=$null;$script:workspace=$null
    $result=Require POST '/auth/sessions' @{email=$Email;password=$password} "quote-login-$([Guid]::NewGuid().ToString('N'))"
    $script:token=$result.accessToken;$script:workspace=$fixture.workspaceId
}
function ParallelQuotes([string]$Path,[array]$Inputs){
    $client=[Net.Http.HttpClient]::new()
    $requests=[Collections.Generic.List[object]]::new()
    try{
        foreach($input in $Inputs){
            $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:5098$Path")
            $request.Headers.Add('Authorization',"Bearer $script:token");$request.Headers.Add('X-Workspace-Id',$script:workspace)
            $request.Headers.Add('X-Request-Id',"quote-parallel-$([Guid]::NewGuid().ToString('N'))")
            $request.Headers.Add('Idempotency-Key',$input.key)
            if($null -ne $input.version){$request.Headers.Add('If-Match','"'+$input.version+'"')}
            $request.Content=[Net.Http.StringContent]::new(($input.body|ConvertTo-Json -Depth 40 -Compress),[Text.Encoding]::UTF8,'application/json')
            $requests.Add(@{request=$request;task=$client.SendAsync($request);input=$input})
        }
        $results=@(foreach($pending in $requests){
            $response=$pending.task.GetAwaiter().GetResult()
            try{
                $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $result=@{status=[int]$response.StatusCode;body=($text|ConvertFrom-Json -AsHashtable)}
                $cases.Add(@{request=@{method='POST';path=$Path;body=$pending.input.body;idempotencyKey=$pending.input.key;expectedVersion=$pending.input.version;workspaceId=$script:workspace;concurrent=$true};response=$result})
                $result
            }finally{$response.Dispose()}
        })
        return ,$results
    }finally{foreach($pending in $requests){$pending.request.Dispose()};$client.Dispose()}
}
$prior=@{}
function EnvironmentValue([string]$Name,[string]$Value){$prior[$Name]=[Environment]::GetEnvironmentVariable($Name);[Environment]::SetEnvironmentVariable($Name,$Value)}
try{
    EnvironmentValue 'ASPNETCORE_ENVIRONMENT' 'Development';EnvironmentValue 'DOTNET_ENVIRONMENT' 'Development'
    if($Mode -eq 'Migrate'){
        $legacy=(Invoke-DemoSql "SELECT COUNT(*) Count,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId FOR JSON PATH)),2) RowHash FROM quotes.Quotes").Rows[0]
        $old=@{count=[int]$legacy.Count;hash=[string]$legacy.RowHash}
        $old|ConvertTo-Json|Set-Content (Join-Path $script:stateDirectory 'quotes-before-migration.json')
        EnvironmentValue 'ConnectionStrings__UnicoreCRM' $script:connectionString
        Push-Location $repo
        try{& dotnet ef database update --project src/UnicoreCRM.Sales --startup-project src/UnicoreCRM.Sales --context QuotesDbContext --no-build *> (Join-Path $script:stateDirectory 'migration.log');$code=$LASTEXITCODE}finally{Pop-Location}
        Assert-Demo ($code -eq 0) 'MIGRATION_FAILED' 'Inspect migration.log'
        $current=(Invoke-DemoSql "SELECT COUNT(*) Count,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM quotes.Quotes ORDER BY WorkspaceId,QuoteId FOR JSON PATH)),2) RowHash FROM quotes.Quotes").Rows[0]
        Assert-Demo ([int]$current.Count -eq $old.count -and [string]$current.RowHash -ceq $old.hash) 'LEGACY_QUOTES_CHANGED' 'Additive migration must preserve every existing Quote'
        Preserve
        @{status='PASS';before=$old;after=@{count=[int]$current.Count;hash=[string]$current.RowHash}}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $script:stateDirectory 'migration-preservation.json')
        Write-Host 'PASS: Quote additive migration and all original business hashes preserved';return
    }
    $password=$configuration['UNICORE_DEV_SEED_PASSWORD'];if(!$password){$password=$configuration['DevelopmentDemoBootstrap:Password']}
    if($Mode -eq 'Browser'){EnvironmentValue 'Frontend__AllowedOrigins__0' 'http://127.0.0.1:3044'}
    Start-DemoHost
    Login 'pipeline.provisioning@example.com'
    if($Mode -eq 'Browser'){
        foreach($entry in @{
            UNICORECRM_TEST_API_BASE_URL='http://127.0.0.1:5098';UNICORECRM_TEST_EMAIL='pipeline.provisioning@example.com';UNICORECRM_TEST_PASSWORD=$password
            UNICORECRM_TEST_WORKSPACE_ID=$fixture.workspaceId;UNICORECRM_TEST_WORKSPACE_KEY=$fixture.workspaceKey
            UNICORECRM_TEST_DEAL_ID=$review.dealId;UNICORECRM_TEST_EVIDENCE_DIR=(Join-Path $script:stateDirectory 'browser')
        }.GetEnumerator()){EnvironmentValue $entry.Key $entry.Value}
        Push-Location (Join-Path (Split-Path $repo) 'frontend/unicorecrm-web')
        try{& npx playwright test --config playwright.quote-core.config.ts;$code=$LASTEXITCODE}finally{Pop-Location}
        Preserve;Assert-Demo ($code -eq 0) 'BROWSER_FAILED' 'Inspect connected browser results'
        $browser=Get-Content -Raw (Join-Path $script:stateDirectory 'browser/browser-result.json')|ConvertFrom-Json -AsHashtable
        Assert-Demo ($browser.status -ceq 'PASS' -and $browser.quoteId) 'BROWSER_RESULT_REQUIRED' 'Connected browser must identify its committed Quote'
        $stored=(Invoke-DemoSql 'SELECT QuoteId,QuoteNumber,SourceDealId,Status,Title,Currency,ResourceVersion,GrandTotalAmount,OwnerId,LineItemsJson,PaymentAgreementJson FROM quotes.Quotes WHERE WorkspaceId=@ws AND QuoteId=@id' @{'@ws'=$fixture.workspaceId;'@id'=$browser.quoteId}).Rows[0]
        Assert-Demo ($stored.ResourceVersion -eq 2 -and $stored.SourceDealId -ceq $review.dealId -and $stored.Status -ceq 'DRAFT') 'BROWSER_SQL_MISMATCH' 'Browser create/edit must be committed to the correct SQL Quote'
        $sqlEvidence=@{};foreach($column in $stored.Table.Columns){$sqlEvidence[$column.ColumnName]=if($stored[$column.ColumnName] -is [DBNull]){$null}else{$stored[$column.ColumnName]}}
        @{status='PASS';workspaceId=$fixture.workspaceId;quote=$sqlEvidence;originalCrmPreserved=$true}|ConvertTo-Json -Depth 20|Set-Content (Join-Path $script:stateDirectory 'browser/sql-result.json')
        Write-Host 'PASS: browser-created and edited Draft verified in SQL, original CRM hashes preserved';return
    }
    $deal=Require GET "/deals/$($review.dealId)"
    $buyer=$deal.buyerRef
    $run=[Guid]::NewGuid().ToString('N')
    $line=@{productNameSnapshot='Dịch vụ kiểm thử Quote';quantity='2';unitPrice=@{amount='100000';currency='VND'};discountRate='10';taxRate='10';taxMode='EXCLUSIVE'}
    $draft=@{buyerRef=$buyer;sourcePath='DIRECT_SALE';title='Báo giá trực tiếp kiểm thử';currency='VND';lineItems=@($line);validUntil='2027-06-01';notes='Synthetic Quote Core API acceptance';paymentAgreement=@{kind='FULL_PAYMENT';currency='VND';lines=@(@{sequence=1;label='Thanh toán toàn bộ';purpose='FULL';amountRule=@{type='REMAINDER'};dueRule=@{type='EVENT_RELATIVE';event='ORDER_CONFIRMED';offsetDays=0;dayBasis='CALENDAR'};allowedMethodCodes=@('BANK_TRANSFER');fulfillmentGate='NONE'})}}
    $direct=Require POST '/quotes' $draft "quote-direct-$run"
    Assert-Demo ($direct.result.quote.grandTotal.amount -ceq '198000' -and $direct.result.quote.quoteNumber -match '^Q-[0-9]+$' -and $direct.result.quote.resourceVersion -eq 1) 'QT-01' 'Authoritative direct Quote pricing/number/version'
    Assert-Demo ($direct.result.quote.paymentAgreement.lines[0].previewAmount.amount -ceq '198000') 'PAYMENT_PREVIEW' 'Payment preview must use backend total'
    foreach($scenario in @(
        @{name='inclusive';price='110';quantity='1';mode='INCLUSIVE';tax='10';total='110';taxTotal='10'},
        @{name='no-tax';price='100';quantity='2';mode='NONE';tax='0';total='200';taxTotal='0'},
        @{name='rounding';price='1.5';quantity='1';mode='NONE';tax='0';total='2';taxTotal='0'},
        @{name='zero';price='0';quantity='1';mode='NONE';tax='0';total='0';taxTotal='0'},
        @{name='large';price='1000000000000000000';quantity='2';mode='NONE';tax='0';total='2000000000000000000';taxTotal='0'})){
        $pricingDraft=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable
        $pricingDraft.title="Quote pricing $($scenario.name)";$pricingDraft.lineItems[0].unitPrice.amount=$scenario.price
        $pricingDraft.lineItems[0].quantity=$scenario.quantity;$pricingDraft.lineItems[0].discountRate='0';$pricingDraft.lineItems[0].taxMode=$scenario.mode;$pricingDraft.lineItems[0].taxRate=$scenario.tax
        $priced=(Require POST '/quotes' $pricingDraft "quote-price-$($scenario.name)-$run").result.quote
        Assert-Demo ($priced.grandTotal.amount -ceq $scenario.total -and $priced.taxTotal.amount -ceq $scenario.taxTotal) 'AUTHORITATIVE_PRICING' $scenario.name
    }
    $mixed=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable
    $mixed.title='Quote mixed authoritative adjustments'
    $mixed.lineItems=@(
        @{productNameSnapshot='Exclusive';quantity='2';unitPrice=@{amount='100';currency='VND'};discountRate='10';taxRate='10';taxMode='EXCLUSIVE'},
        @{productNameSnapshot='Inclusive';quantity='1';unitPrice=@{amount='110';currency='VND'};discountRate='0';taxRate='10';taxMode='INCLUSIVE'},
        @{productNameSnapshot='No tax';quantity='1';unitPrice=@{amount='50';currency='VND'};discountRate='0';taxRate='0';taxMode='NONE'})
    $mixed.adjustments=@(@{id='discount';label='Discount';type='DISCOUNT';calculation='PERCENTAGE';value='10'},@{id='shipping';label='Shipping';type='SHIPPING_FEE';calculation='FIXED_AMOUNT';value='5'},@{id='tax';label='Tax';type='TAX';calculation='PERCENTAGE';value='10'})
    $mixedQuote=(Require POST '/quotes' $mixed "quote-mixed-$run").result.quote
    Assert-Demo ($mixedQuote.subtotal.amount -ceq '360' -and $mixedQuote.discountTotal.amount -ceq '54' -and $mixedQuote.taxTotal.amount -ceq '59' -and $mixedQuote.grandTotal.amount -ceq '360') 'QT-05-08' 'Multiple lines, mixed taxes, document discounts and fees must reconcile'
    foreach($invalidAmount in @('-1','1e3','1.1234567')){
        $invalid=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$invalid.lineItems[0].unitPrice.amount=$invalidAmount
        Assert-Demo ((Api POST '/quotes' $invalid "quote-invalid-$([Guid]::NewGuid().ToString('N'))").status -eq 422) 'INVALID_DECIMAL' $invalidAmount
    }
    $replay=Require POST '/quotes' $draft "quote-direct-$run"
    Assert-Demo ($replay.outcome -ceq 'REPLAYED' -and $replay.aggregateId -ceq $direct.aggregateId) 'QT-14' 'Same key must replay without duplicate'
    $changed=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$changed.title='Changed payload'
    $reuse=Api POST '/quotes' $changed "quote-direct-$run"
    Assert-Demo ($reuse.status -eq 409 -and $reuse.body.code -ceq 'IDEMPOTENCY_KEY_REUSED') 'KEY_REUSE' 'Changed payload must conflict'
    $dealDraft=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;[void]$dealDraft.Remove('buyerRef');[void]$dealDraft.Remove('sourcePath');$dealDraft.title='Báo giá liên kết Deal kiểm thử'
    $linked=Require POST "/deals/$($deal.id)/quotes" $dealDraft "quote-deal-$run" $deal.resourceVersion
    Assert-Demo ($linked.result.quote.sourceDealId -ceq $deal.id -and $linked.result.quote.buyerRef.id -ceq $buyer.id) 'QT-02' 'Deal source must be owner-derived'
    $read=Require GET "/quotes/$($linked.aggregateId)"
    Assert-Demo ($read.sourceDealId -ceq $deal.id) 'QT-11' 'Quote readback must survive request boundary'
    $list=Require GET "/quotes?sourceDealId=$($deal.id)&limit=250"
    Assert-Demo (@($list.items|Where-Object id -ceq $linked.aggregateId).Count -eq 1) 'QT-22' 'Linked quote list must include committed Quote'
    $update=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$update.title='Báo giá trực tiếp đã sửa';$update.lineItems[0].quantity='3'
    $updated=Require POST "/quotes/$($direct.aggregateId)/update-draft" $update "quote-update-$run" 1
    Assert-Demo ($updated.result.quote.resourceVersion -eq 2 -and $updated.result.quote.quoteRevision -eq 1 -and $updated.result.quote.grandTotal.amount -ceq '297000') 'QT-10' 'Draft replacement increments resource version without revising commercial identity and recalculates'
    $stale=Api POST "/quotes/$($direct.aggregateId)/update-draft" $update "quote-stale-$run" 1
    Assert-Demo ($stale.status -eq 412) 'QT-15' 'Stale version cannot overwrite'
    $updateA=$update|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$updateA.title='Concurrent writer A'
    $updateB=$update|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$updateB.title='Concurrent writer B'
    $parallelUpdate=ParallelQuotes "/quotes/$($direct.aggregateId)/update-draft" @(@{body=$updateA;key="quote-concurrent-a-$run";version=2},@{body=$updateB;key="quote-concurrent-b-$run";version=2})
    Assert-Demo (@($parallelUpdate|Where-Object status -eq 200).Count -eq 1 -and @($parallelUpdate|Where-Object status -eq 412).Count -eq 1) 'QT-16' 'Concurrent version-2 updates must have exactly one winner'
    $parallelCreate=ParallelQuotes '/quotes' @(@{body=$draft;key="quote-concurrent-create-$run"},@{body=$draft;key="quote-concurrent-create-$run"})
    Assert-Demo (@($parallelCreate|Where-Object status -eq 201).Count -eq 2 -and $parallelCreate[0].body.aggregateId -ceq $parallelCreate[1].body.aggregateId -and @($parallelCreate|Where-Object {$_.body.outcome -ceq 'REPLAYED'}).Count -eq 1) 'CONCURRENT_CREATE' 'Concurrent retries must create exactly one Quote'
    $distinctCreate=ParallelQuotes '/quotes' @(@{body=$draft;key="quote-distinct-a-$run"},@{body=$draft;key="quote-distinct-b-$run"})
    Assert-Demo (@($distinctCreate|Where-Object status -eq 201).Count -eq 2 -and $distinctCreate[0].body.aggregateId -cne $distinctCreate[1].body.aggregateId -and $distinctCreate[0].body.result.quote.quoteNumber -cne $distinctCreate[1].body.result.quote.quoteNumber) 'CONCURRENT_NUMBER' 'Concurrent independent creates require distinct server business numbers'
    $productInput=@{sku="QC-$($run.Substring(0,12))";name='Sản phẩm Quote kiểm thử';type='service';status='ACTIVE';category='Quote control';unit='service';unitPrice=@{amount='250000';currency='VND'};taxRate='0';taxMode='none';billingCycle='one_time';isSubscription=$false;isRenewable=$false;tags=@();description='Synthetic Quote Core catalog snapshot'}
    $product=(Require POST '/products' $productInput "quote-product-$run").result.product
    $catalogDraft=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$catalogDraft.title='Báo giá Product snapshot';$catalogDraft.lineItems=@(@{productId=$product.id;productNameSnapshot='Untrusted name';quantity='1';unitPrice=@{amount='250000';currency='VND'};discountRate='0';taxRate='0';taxMode='NONE'})
    $catalogQuote=Require POST '/quotes' $catalogDraft "quote-catalog-$run"
    Assert-Demo ($catalogQuote.result.quote.lineItems[0].productNameSnapshot -ceq $product.name -and $catalogQuote.result.quote.grandTotal.amount -ceq '250000') 'QT-03' 'Catalog capture must replace untrusted metadata'
    $bad=$catalogDraft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$bad.lineItems[0].unitPrice.amount='1'
    $priceOverride=Api POST '/quotes' $bad "quote-price-override-$run"
    Assert-Demo ($priceOverride.status -eq 422) 'PRICE_POLICY' 'quotes.create does not authorize catalog price override'
    $productInput.unitPrice.amount='300000'
    $null=Require PUT "/products/$($product.id)" $productInput "quote-product-change-$run" $product.version
    $historical=Require GET "/quotes/$($catalogQuote.aggregateId)"
    Assert-Demo ($historical.lineItems[0].unitPrice.amount -ceq '250000' -and $historical.grandTotal.amount -ceq '250000') 'CATALOG_HISTORY' 'Catalog price changes cannot rewrite Quote history'
    $catalogDraft.title='Báo giá snapshot giữ nguyên khi sửa'
    $catalogUpdated=Require POST "/quotes/$($catalogQuote.aggregateId)/update-draft" $catalogDraft "quote-catalog-update-$run" 1
    Assert-Demo ($catalogUpdated.result.quote.grandTotal.amount -ceq '250000') 'CATALOG_DRAFT_HISTORY' 'Unchanged captured pricing survives Draft edit'
    $bad=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$bad.lineItems[0].unitPrice.currency='USD'
    Assert-Demo ((Api POST '/quotes' $bad "quote-currency-$run").status -eq 422) 'QT-09' 'Mixed currencies refused'
    $bad=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$bad.buyerRef.id='contact_nonexistent_quote_control'
    Assert-Demo ((Api POST '/quotes' $bad "quote-buyer-$run").status -eq 422) 'QT-20' 'Unknown buyer refused'
    Assert-Demo ((Api POST '/deals/deal_nonexistent_quote_control/quotes' $dealDraft "quote-foreigndeal-$run" 0).status -eq 404) 'QT-19' 'Unknown or foreign Deal neutral'
    # Resolve existing foreign records read-only; never seed or mutate the original demo.
    $foreignDeal=(Invoke-DemoSql 'SELECT TOP(1) DealId FROM deals.Deals WHERE WorkspaceId<>@ws ORDER BY DealId' @{'@ws'=$fixture.workspaceId}).Rows[0].DealId
    Assert-Demo (-not [string]::IsNullOrWhiteSpace($foreignDeal)) 'FOREIGN_DEAL_FIXTURE' 'An existing foreign Deal is required'
    Assert-Demo ((Api POST "/deals/$foreignDeal/quotes" $dealDraft "quote-real-foreign-deal-$run" 0).status -eq 404) 'QT-19' 'Existing foreign Deal must remain invisible'
    $foreignBuyer=(Invoke-DemoSql 'SELECT TOP(1) ContactId FROM contacts.Contacts WHERE WorkspaceId<>@ws ORDER BY ContactId' @{'@ws'=$fixture.workspaceId}).Rows[0].ContactId
    $bad=$draft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$bad.buyerRef=@{type='CONTACT';id=$foreignBuyer}
    Assert-Demo ((Api POST '/quotes' $bad "quote-real-foreign-buyer-$run").status -eq 422) 'FOREIGN_BUYER' 'Existing foreign Buyer must be rejected'
    $foreignProduct=(Invoke-DemoSql 'SELECT TOP(1) ProductId FROM products.Products WHERE WorkspaceId<>@ws ORDER BY ProductId' @{'@ws'=$fixture.workspaceId}).Rows[0].ProductId
    $bad=$catalogDraft|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable;$bad.lineItems[0].productId=$foreignProduct
    Assert-Demo ((Api POST '/quotes' $bad "quote-real-foreign-product-$run").status -eq 422) 'FOREIGN_PRODUCT' 'Existing foreign Product must be rejected'
    Login 'pipeline.readonly@example.com'
    Assert-Demo ((Api POST '/quotes' $draft "quote-restricted-$run").status -eq 403) 'QT-17' 'Restricted user cannot write'
    Assert-Demo ((Api POST '/quotes' $draft "quote-direct-$run").status -eq 403) 'REPLAY_AUTH' 'Unauthorized replay cannot expose result'
    $script:workspace='ws_f2332a12d56a421d8a722bd2f65c5efb'
    Assert-Demo ((Api POST '/quotes' $draft "quote-foreign-$run").status -eq 403) 'QT-18' 'Foreign Workspace refused'
    Login 'pipeline.provisioning@example.com'
    $afterLogin=Require GET "/quotes/$($direct.aggregateId)"
    Assert-Demo ($afterLogin.resourceVersion -eq 3) 'QT-12' 'New authenticated session retains SQL data'
    Stop-DemoHost;Start-DemoHost;Login 'pipeline.provisioning@example.com'
    $afterRestart=Require GET "/quotes/$($direct.aggregateId)"
    Assert-Demo ($afterRestart.grandTotal.amount -ceq '297000') 'QT-13' 'Backend restart retains SQL data'
    $sql=(Invoke-DemoSql 'SELECT QuoteId,QuoteNumber,ResourceVersion,GrandTotalAmount,SourceDealId,LineItemsJson,PaymentAgreementJson,ProductSnapshotsJson FROM quotes.Quotes WHERE WorkspaceId=@ws AND QuoteId IN (@a,@b,@c) ORDER BY QuoteId' @{'@ws'=$fixture.workspaceId;'@a'=$direct.aggregateId;'@b'=$linked.aggregateId;'@c'=$catalogQuote.aggregateId}).Rows
    $sqlRows=@(foreach($row in $sql){$value=@{};foreach($column in $row.Table.Columns){$value[$column.ColumnName]=if($row[$column.ColumnName] -is [DBNull]){$null}else{$row[$column.ColumnName]}};$value})
    Preserve
    @{status='PASS';workspaceId=$fixture.workspaceId;workspaceKey=$fixture.workspaceKey;dealId=$deal.id;directQuoteId=$direct.aggregateId;linkedQuoteId=$linked.aggregateId;productId=$product.id;catalogQuoteId=$catalogQuote.aggregateId;sql=$sqlRows;requests=$cases;businessDataPreserved=$true}|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'api-result.json')
    Write-Host 'PASS: connected Quote Core create/read/update/retry/catalog/version/security/login/restart and original CRM preservation'
}finally{
    $cases|ConvertTo-Json -Depth 60|Set-Content (Join-Path $script:stateDirectory 'last-cases.json')
    Stop-DemoHost
    foreach($entry in $prior.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value)}
}
