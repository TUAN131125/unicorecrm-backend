<#
.SYNOPSIS
    Public-API verification of Lead interestedProducts capture.

.DESCRIPTION
    Drives Lead create and replace over HTTP against a real ApiHost and an isolated database, using
    real Products created through the public Products API. It proves the frozen snapshot rules:
    Products decides resolution and eligibility, the captured snapshot is immutable, and replace
    preserves a retained snapshot while capturing a newly added one.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5353,

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
$script:MemberId = $null

function Add-Result {
    param([string] $Name, [string] $Expected, [string] $Actual)
    if ($Expected -eq $Actual) {
        $script:Passed++
        [void]$script:Results.Add(('PASS | {0} | {1}' -f $Name, $Actual))
    }
    else {
        $script:Failed++
        [void]$script:Results.Add(('FAIL | {0} | expected={1} actual={2}' -f $Name, $Expected, $Actual))
    }
}

function New-ConnectionString { param([string] $Database)
    return "Server=$SqlServer;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" }

function Invoke-Sql {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $connection.Open()
    try {
        $command = $connection.CreateCommand(); $command.CommandText = $Query; $command.CommandTimeout = 120
        $reader = $command.ExecuteReader()
        $rows = New-Object System.Collections.ArrayList
        while ($reader.Read()) {
            $row = @{}
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                $name = $reader.GetName($i)
                if ([string]::IsNullOrWhiteSpace($name)) { $name = "Column$i" }
                $row[$name] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
            }
            [void]$rows.Add([pscustomobject]$row)
        }
        $reader.Close(); return $rows
    }
    finally { $connection.Dispose() }
}

function Invoke-SqlNonQuery {
    param([string] $Query, [string] $Database = 'master')
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString -Database $Database)
    $connection.Open()
    try { $c = $connection.CreateCommand(); $c.CommandText = $Query; $c.CommandTimeout = 120; [void]$c.ExecuteNonQuery() }
    finally { $connection.Dispose() }
}

function Get-Scalar {
    param([string] $Query, [string] $Database)
    $rows = Invoke-Sql -Query $Query -Database $Database
    if ($rows.Count -eq 0) { return $null }
    return $rows[0].($($rows[0].PSObject.Properties | Select-Object -First 1).Name)
}

function New-RequestId { $script:RequestCounter++; return ('req-lead-products-{0:d6}' -f $script:RequestCounter) }

function Invoke-Api {
    param([string] $Method, [string] $Path, [string] $Body, [string] $Token,
          [string] $WorkspaceId, [string] $IdempotencyKey, [string] $IfMatch)
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    [void]$request.Headers.TryAddWithoutValidation('X-Request-Id', (New-RequestId))
    [void]$request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-lead-products-0001')
    if (-not [string]::IsNullOrWhiteSpace($Token)) { [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { [void]$request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { [void]$request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    if (-not [string]::IsNullOrWhiteSpace($IfMatch)) { [void]$request.Headers.TryAddWithoutValidation('If-Match', $IfMatch) }
    if (-not [string]::IsNullOrEmpty($Body)) { $request.Content = New-Object System.Net.Http.StringContent ($Body, [Text.Encoding]::UTF8, 'application/json') }

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.UseProxy = $false; $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient ($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
    }
    finally { $client.Dispose(); $request.Dispose() }
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null } }
    return [pscustomobject]@{ Status = $status; Body = $payload; Raw = $raw }
}

function New-Product {
    param([string] $Sku, [string] $Name, [string] $Type = 'service')
    $body = @{
        sku = $Sku; name = $Name; type = $Type; status = 'ACTIVE'; category = 'general'; unit = 'each'
        unitPrice = @{ amount = '100'; currency = 'USD' }; taxRate = '0'; taxMode = 'none'
        billingCycle = 'one_time'; isSubscription = $false; isRenewable = $false; tags = @()
    } | ConvertTo-Json -Depth 6 -Compress
    $created = Invoke-Api -Method 'POST' -Path '/products' -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey ('idem-product-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)) -Body $body
    if ($created.Status -ne 201) { throw "Product creation failed with $($created.Status): $($created.Raw)" }
    return $created.Body.result.product.id
}

function New-LeadBody {
    param([string] $DisplayName = 'Interested Lead', [array] $Products = @())
    $profile = @{
        displayName = $DisplayName; phone = '0911000111'; source = 'verifier'
        ownerId = $script:MemberId; estimatedValue = @{ amount = '0'; currency = 'USD' }
    }
    if ($Products.Count -gt 0) { $profile.interestedProducts = $Products }
    return ($profile | ConvertTo-Json -Depth 8 -Compress)
}

function New-ProductEntry {
    param([string] $ProductId, [string] $InterestLevel = 'medium', [int] $Quantity = 0, [string] $Note)
    $entry = @{ productId = $ProductId; interestLevel = $InterestLevel }
    if ($Quantity -gt 0) { $entry.estimatedQuantity = $Quantity }
    if ($Note) { $entry.note = $Note }
    return $entry
}

function New-Lead {
    param([string] $Body, [string] $Key)
    if (-not $Key) { $Key = 'idem-lead-' + [Guid]::NewGuid().ToString('N').Substring(0, 16) }
    return Invoke-Api -Method 'POST' -Path '/leads' -Token $script:Token -WorkspaceId $script:WorkspaceId -IdempotencyKey $Key -Body $Body
}

function Set-LeadProfile {
    param([string] $LeadId, [string] $Body, [string] $Key, [long] $Version = -1)
    if (-not $Key) { $Key = 'idem-repl-' + [Guid]::NewGuid().ToString('N').Substring(0, 16) }
    if ($Version -lt 0) { $Version = [long](Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM leads.Leads WHERE LeadId='$LeadId'") }
    return Invoke-Api -Method 'PUT' -Path "/leads/$LeadId" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey $Key -IfMatch ('"{0}"' -f $Version) -Body $Body
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostProject = Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj'
$demoEmail = 'lead.interested.products@example.test'
$demoPassword = 'Lead-Interested-Products!2026'
$hostProcess = $null
$logPath = Join-Path ([IO.Path]::GetTempPath()) ("unicore-lead-products-$([Guid]::NewGuid().ToString('N')).log")

try {
    Write-Host "Provisioning isolated database $DatabaseName on $SqlServer ..."
    Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END;
CREATE DATABASE [$DatabaseName];
"@

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = New-ConnectionString -Database $DatabaseName
    $env:Development__ApplyMigrations = 'true'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $demoEmail
    $env:IdentityAuth__DevelopmentBootstrap__Password = $demoPassword
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Lead Interested Products Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'

    Push-Location $repositoryRoot
    try { & dotnet build $hostProject -v q --nologo | Out-Null } finally { Pop-Location }

    $hostProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--no-build', '--no-launch-profile', '--project', $hostProject) `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

    $ready = $false
    for ($attempt = 0; $attempt -lt $ReadyTimeoutSeconds; $attempt++) {
        Start-Sleep -Seconds 1
        if ($hostProcess.HasExited) { throw "ApiHost exited with code $($hostProcess.ExitCode). See $logPath" }
        try { $probe = Invoke-Api -Method 'GET' -Path '/auth/session'; if ($probe.Status -gt 0) { $ready = $true; break } } catch { }
    }
    if (-not $ready) { throw "ApiHost did not become ready within $ReadyTimeoutSeconds seconds. See $logPath" }

    $signIn = Invoke-Api -Method 'POST' -Path '/auth/sessions' -IdempotencyKey 'idem-lp-signin-0001' `
        -Body (@{ email = $demoEmail; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($signIn.Status -ne 200) { throw "Sign-in failed with $($signIn.Status): $($signIn.Raw)" }
    $script:Token = $signIn.Body.accessToken
    $script:MemberId = (Invoke-Api -Method 'GET' -Path '/auth/session' -Token $script:Token).Body.principal.memberId
    $provisioning = Invoke-Api -Method 'POST' -Path '/workspaces/initial-provisioning' -Token $script:Token `
        -IdempotencyKey 'idem-lp-provisioning-0001' -Body '{"name":"Lead Products Workspace"}'
    if ($provisioning.Status -ne 201) { throw "Provisioning failed with $($provisioning.Status): $($provisioning.Raw)" }
    $script:WorkspaceId = $provisioning.Body.workspaceId
    $roleId = Get-Scalar -Database $DatabaseName -Query "SELECT RoleId FROM access.Roles WHERE WorkspaceId='$($script:WorkspaceId)' AND Name='Workspace Owner'"

    $alpha = New-Product -Sku 'LP-ALPHA' -Name 'Alpha Service' -Type 'service'
    $beta  = New-Product -Sku 'LP-BETA'  -Name 'Beta Licence'  -Type 'license'
    $gamma = New-Product -Sku 'LP-GAMMA' -Name 'Gamma Addon'   -Type 'addon'

    function Get-ProductCommandCount {
        return [long](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM products.AuditRecords WHERE Operation IN ('createProduct','replaceProduct','archiveProduct','restoreProduct')")
    }

    # ---------------------------------------------------------------- create

    $one = New-Lead -Body (New-LeadBody -DisplayName 'One Product Lead' -Products @((New-ProductEntry -ProductId $alpha -InterestLevel 'high' -Quantity 5 -Note 'first note')))
    Add-Result 'create with one interested Product' '201' $one.Status
    $oneLead = $one.Body.result.id
    $oneItem = $one.Body.result.interestedProducts[0]
    Add-Result 'snapshot carries the Product name' 'Alpha Service' $oneItem.productNameSnapshot
    Add-Result 'snapshot carries the SKU' 'LP-ALPHA' $oneItem.skuSnapshot
    Add-Result 'snapshot carries the Product type' 'service' $oneItem.productTypeSnapshot
    Add-Result 'caller-owned interest level is kept' 'high' $oneItem.interestLevel
    Add-Result 'caller-owned quantity is kept' '5' ([string]$oneItem.estimatedQuantity)
    Add-Result 'no version is projected onto the wire' 'True' ([string]($one.Raw -notmatch 'productVersionSnapshot'))
    Add-Result 'no Product price crosses into the Lead' 'True' ([string]($one.Raw -notmatch 'unitPrice|taxRate|taxMode|billingCycle'))
    Add-Result 'the capture version is persisted owner-locally' '0' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT JSON_VALUE(Profile,'`$.interestedProducts[0].productVersionSnapshot') FROM leads.Leads WHERE LeadId='$oneLead'"))

    $many = New-Lead -Body (New-LeadBody -DisplayName 'Many Product Lead' -Products @(
        (New-ProductEntry -ProductId $alpha -InterestLevel 'low'),
        (New-ProductEntry -ProductId $beta  -InterestLevel 'medium'),
        (New-ProductEntry -ProductId $gamma -InterestLevel 'high')))
    Add-Result 'create with several interested Products' '201' $many.Status
    Add-Result 'all three snapshots are captured' '3' ([string]@($many.Body.result.interestedProducts).Count)
    Add-Result 'snapshots keep submitted order' 'Alpha Service,Beta Licence,Gamma Addon' `
        ((@($many.Body.result.interestedProducts | ForEach-Object { $_.productNameSnapshot })) -join ',')

    # ---------------------------------------------------------------- rejection paths

    $leadsBefore = [long](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.Leads")

    $duplicate = New-Lead -Body (New-LeadBody -DisplayName 'Duplicate Lead' -Products @(
        (New-ProductEntry -ProductId $alpha), (New-ProductEntry -ProductId $alpha)))
    Add-Result 'duplicate productId rejected' '422' $duplicate.Status
    Add-Result 'duplicate names the offending entry' 'True' ([string]($duplicate.Raw -match 'interestedProducts\[1\]\.productId'))

    $unknown = New-Lead -Body (New-LeadBody -DisplayName 'Unknown Lead' -Products @((New-ProductEntry -ProductId 'product_does_not_exist')))
    Add-Result 'unknown Product rejected' '422' $unknown.Status
    Add-Result 'unknown discloses no Product fact' 'True' ([string]($unknown.Raw -notmatch 'Alpha|LP-ALPHA|ACTIVE'))

    # A Product that exists, but in another Workspace, must be byte-identical to an unknown one.
    $foreignWorkspaceId = 'ws_lead_products_foreign'
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
IF NOT EXISTS (SELECT 1 FROM workspace.Workspaces WHERE WorkspaceId='$foreignWorkspaceId')
INSERT INTO workspace.Workspaces (WorkspaceId,[Key],[Name],LogoText,CreatedAt)
VALUES (N'$foreignWorkspaceId',N'$foreignWorkspaceId',N'Foreign',N'FW',SYSDATETIMEOFFSET());
"@
    $foreignProductId = 'product_foreign_' + [Guid]::NewGuid().ToString('N')
    Invoke-SqlNonQuery -Database $DatabaseName -Query @"
INSERT INTO products.Products (ProductId, WorkspaceId, Profile, NormalizedSku, CreatedAt, UpdatedAt, [Version])
SELECT N'$foreignProductId', N'$foreignWorkspaceId', Profile, N'LP-FOREIGN', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0
FROM products.Products WHERE ProductId = N'$alpha';
"@
    $foreign = New-Lead -Body (New-LeadBody -DisplayName 'Foreign Lead' -Products @((New-ProductEntry -ProductId $foreignProductId)))
    Add-Result 'foreign-Workspace Product rejected' '422' $foreign.Status
    Add-Result 'foreign and unknown Products are indistinguishable' `
        ('{0}|{1}' -f $unknown.Status, $unknown.Body.fieldErrors.'interestedProducts[0].productId') `
        ('{0}|{1}' -f $foreign.Status, $foreign.Body.fieldErrors.'interestedProducts[0].productId')

    $archivedProduct = New-Product -Sku 'LP-ARCHIVED' -Name 'Archived Service'
    $archiveVersion = Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM products.Products WHERE ProductId='$archivedProduct'"
    $archived = Invoke-Api -Method 'POST' -Path "/products/$archivedProduct/archive" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-lp-archive-0001' -IfMatch ('"{0}"' -f $archiveVersion) -Body '{"reason":"verifier archive"}'
    if ($archived.Status -ne 200) { throw "Product archive failed with $($archived.Status): $($archived.Raw)" }
    $productCommandsAfterFixtures = Get-ProductCommandCount
    $ineligible = New-Lead -Body (New-LeadBody -DisplayName 'Archived Lead' -Products @((New-ProductEntry -ProductId $archivedProduct)))
    Add-Result 'archived Product rejected as ineligible' '422' $ineligible.Status
    $ineligibleMessage = (@($ineligible.Body.fieldErrors.'interestedProducts[0].productId') -join '|')
    $unresolvableMessage = (@($unknown.Body.fieldErrors.'interestedProducts[0].productId') -join '|')
    Add-Result 'ineligible is distinct from unresolvable' 'True' ([string]($ineligibleMessage -ne $unresolvableMessage))

    $mixed = New-Lead -Body (New-LeadBody -DisplayName 'Mixed Lead' -Products @(
        (New-ProductEntry -ProductId $alpha), (New-ProductEntry -ProductId 'product_not_there'), (New-ProductEntry -ProductId $beta)))
    Add-Result 'mixed-validity batch rejected' '422' $mixed.Status
    Add-Result 'all-or-nothing: no Lead was written by any rejection' ([string]$leadsBefore) `
        ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.Leads"))

    Invoke-SqlNonQuery -Database $DatabaseName -Query "DELETE FROM access.RoleCapabilities WHERE RoleId='$roleId' AND Capability='products.read'"
    $denied = New-Lead -Body (New-LeadBody -DisplayName 'No Products Read Lead' -Products @((New-ProductEntry -ProductId $alpha)))
    Add-Result 'missing products.read is denied' '403' $denied.Status
    Add-Result 'denial discloses no Product fact' 'True' ([string]($denied.Raw -notmatch 'Alpha|LP-ALPHA|ACTIVE'))
    Add-Result 'denial writes no Lead' ([string]$leadsBefore) ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.Leads"))
    $withoutProducts = New-Lead -Body (New-LeadBody -DisplayName 'No Products At All')
    Add-Result 'a Lead without interested Products needs no products.read' '201' $withoutProducts.Status
    Invoke-SqlNonQuery -Database $DatabaseName -Query "INSERT INTO access.RoleCapabilities (RoleId, Capability) VALUES ('$roleId','products.read')"

    # No Lead operation may mutate a Product. The baseline is taken after the archived fixture is
    # created, so it covers every Lead create and every rejection path above.
    Add-Result 'no Lead create or rejection mutated a Product' ([string]$productCommandsAfterFixtures) ([string](Get-ProductCommandCount))

    # ---------------------------------------------------------------- replace

    $replaceLead = $many.Body.result.id
    $alphaEntryId = ($many.Body.result.interestedProducts | Where-Object { $_.productId -eq $alpha }).id

    $updated = Set-LeadProfile -LeadId $replaceLead -Body (New-LeadBody -DisplayName 'Many Product Lead' -Products @(
        (New-ProductEntry -ProductId $alpha -InterestLevel 'high' -Quantity 9 -Note 'changed note'),
        (New-ProductEntry -ProductId $beta  -InterestLevel 'medium')))
    Add-Result 'replace succeeds' '200' $updated.Status
    Add-Result 'replace removed the omitted Product' '2' ([string]@($updated.Body.result.interestedProducts).Count)
    Add-Result 'replace dropped gamma' 'True' ([string](@($updated.Body.result.interestedProducts | ForEach-Object { $_.productId }) -notcontains $gamma))
    $retained = $updated.Body.result.interestedProducts | Where-Object { $_.productId -eq $alpha }
    Add-Result 'retained entry keeps its snapshot id' $alphaEntryId $retained.id
    Add-Result 'retained entry keeps its Product name' 'Alpha Service' $retained.productNameSnapshot
    Add-Result 'retained entry takes the new interest level' 'high' $retained.interestLevel
    Add-Result 'retained entry takes the new quantity' '9' ([string]$retained.estimatedQuantity)
    Add-Result 'retained entry takes the new note' 'changed note' $retained.note

    # Rename the Product: the captured snapshot must not follow it.
    $alphaVersion = Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM products.Products WHERE ProductId='$alpha'"
    $renamed = Invoke-Api -Method 'PUT' -Path "/products/$alpha" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-lp-rename-0001' -IfMatch ('"{0}"' -f $alphaVersion) -Body (@{
            sku = 'LP-ALPHA'; name = 'Alpha Service RENAMED'; type = 'service'; status = 'ACTIVE'; category = 'general'
            unit = 'each'; unitPrice = @{ amount = '100'; currency = 'USD' }; taxRate = '0'; taxMode = 'none'
            billingCycle = 'one_time'; isSubscription = $false; isRenewable = $false; tags = @()
        } | ConvertTo-Json -Depth 6 -Compress)
    if ($renamed.Status -ne 200) { throw "Product rename failed with $($renamed.Status): $($renamed.Raw)" }

    $afterRename = Invoke-Api -Method 'GET' -Path "/leads/$replaceLead" -Token $script:Token -WorkspaceId $script:WorkspaceId
    Add-Result 'rename does not rewrite the captured snapshot' 'Alpha Service' `
        (($afterRename.Body.interestedProducts | Where-Object { $_.productId -eq $alpha }).productNameSnapshot)
    Add-Result 'the original create response is unchanged too' 'Alpha Service' `
        ((Invoke-Api -Method 'GET' -Path "/leads/$oneLead" -Token $script:Token -WorkspaceId $script:WorkspaceId).Body.interestedProducts[0].productNameSnapshot)

    # Archive the Product: an unrelated Lead edit must still succeed.
    $alphaVersion2 = Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM products.Products WHERE ProductId='$alpha'"
    $archivedAlpha = Invoke-Api -Method 'POST' -Path "/products/$alpha/archive" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-lp-archive-alpha' -IfMatch ('"{0}"' -f $alphaVersion2) -Body '{"reason":"verifier archive alpha"}'
    if ($archivedAlpha.Status -ne 200) { throw "Alpha archive failed with $($archivedAlpha.Status): $($archivedAlpha.Raw)" }

    $unrelatedEdit = Set-LeadProfile -LeadId $replaceLead -Body (New-LeadBody -DisplayName 'Renamed Lead Display' -Products @(
        (New-ProductEntry -ProductId $alpha -InterestLevel 'high' -Quantity 9 -Note 'changed note'),
        (New-ProductEntry -ProductId $beta  -InterestLevel 'medium')))
    Add-Result 'archived retained Product does not block an unrelated edit' '200' $unrelatedEdit.Status
    Add-Result 'the retained snapshot is still the captured one' 'Alpha Service' `
        (($unrelatedEdit.Body.result.interestedProducts | Where-Object { $_.productId -eq $alpha }).productNameSnapshot)

    # Adding a NEW product is a fresh capture and must see current Product truth.
    $addNew = Set-LeadProfile -LeadId $replaceLead -Body (New-LeadBody -DisplayName 'Renamed Lead Display' -Products @(
        (New-ProductEntry -ProductId $alpha -InterestLevel 'high' -Quantity 9 -Note 'changed note'),
        (New-ProductEntry -ProductId $beta  -InterestLevel 'medium'),
        (New-ProductEntry -ProductId $gamma -InterestLevel 'low')))
    Add-Result 'replace adds a new Product' '200' $addNew.Status
    Add-Result 're-added Product captures a fresh snapshot' 'Gamma Addon' `
        (($addNew.Body.result.interestedProducts | Where-Object { $_.productId -eq $gamma }).productNameSnapshot)
    Add-Result 're-added Product gets a new entry id' 'True' `
        ([string](($addNew.Body.result.interestedProducts | Where-Object { $_.productId -eq $gamma }).id -ne ($many.Body.result.interestedProducts | Where-Object { $_.productId -eq $gamma }).id))

    # Adding an archived product as a NEW entry must be refused.
    $addArchived = Set-LeadProfile -LeadId $replaceLead -Body (New-LeadBody -DisplayName 'Renamed Lead Display' -Products @(
        (New-ProductEntry -ProductId $alpha -InterestLevel 'high' -Quantity 9 -Note 'changed note'),
        (New-ProductEntry -ProductId $archivedProduct -InterestLevel 'low')))
    Add-Result 'newly added archived Product is refused' '422' $addArchived.Status

    # ---------------------------------------------------------------- idempotency and concurrency

    $replayKey = 'idem-lp-replay-000001'
    $replaySource = New-LeadBody -DisplayName 'Replay Lead' -Products @((New-ProductEntry -ProductId $beta -InterestLevel 'medium'))
    $firstReplay = New-Lead -Body $replaySource -Key $replayKey
    Add-Result 'replay baseline created' '201' $firstReplay.Status
    $betaVersion = Get-Scalar -Database $DatabaseName -Query "SELECT [Version] FROM products.Products WHERE ProductId='$beta'"
    $renamedBeta = Invoke-Api -Method 'PUT' -Path "/products/$beta" -Token $script:Token -WorkspaceId $script:WorkspaceId `
        -IdempotencyKey 'idem-lp-rename-beta' -IfMatch ('"{0}"' -f $betaVersion) -Body (@{
            sku = 'LP-BETA'; name = 'Beta Licence RENAMED'; type = 'license'; status = 'ACTIVE'; category = 'general'
            unit = 'each'; unitPrice = @{ amount = '100'; currency = 'USD' }; taxRate = '0'; taxMode = 'none'
            billingCycle = 'one_time'; isSubscription = $false; isRenewable = $false; tags = @()
        } | ConvertTo-Json -Depth 6 -Compress)
    if ($renamedBeta.Status -ne 200) { throw "Beta rename failed with $($renamedBeta.Status)" }
    # The last deliberate Product mutation. Everything after this is Lead work only.
    $productCommandsAfterRenames = Get-ProductCommandCount

    $replayed = New-Lead -Body $replaySource -Key $replayKey
    Add-Result 'replay after a Product rename still succeeds' '201' $replayed.Status
    Add-Result 'replay returns the original snapshot' 'Beta Licence' $replayed.Body.result.interestedProducts[0].productNameSnapshot
    Add-Result 'replay reports REPLAYED' 'REPLAYED' $replayed.Body.outcome
    Add-Result 'replay creates no second Lead' '1' ([string](Get-Scalar -Database $DatabaseName -Query "SELECT COUNT(*) FROM leads.Leads WHERE JSON_VALUE(Profile,'`$.displayName')='Replay Lead'"))

    $changedIntent = New-Lead -Body (New-LeadBody -DisplayName 'Replay Lead' -Products @((New-ProductEntry -ProductId $beta -InterestLevel 'high'))) -Key $replayKey
    Add-Result 'changed interested-product intent conflicts' '409' $changedIntent.Status
    Add-Result 'changed intent reports idempotency reuse' 'IDEMPOTENCY_KEY_REUSED' $changedIntent.Body.code

    $staleReplace = Set-LeadProfile -LeadId $replaceLead -Version 999 -Body (New-LeadBody -DisplayName 'Stale' -Products @((New-ProductEntry -ProductId $gamma)))
    Add-Result 'stale If-Match still rejected' '412' $staleReplace.Status

    Add-Result 'no Lead replay, conflict or stale command mutated a Product' ([string]$productCommandsAfterRenames) ([string](Get-ProductCommandCount))
}
finally {
    # Printed here so a mid-script failure still shows every assertion that did run.
    $script:Results | ForEach-Object { Write-Host $_ }
    Write-Host ("Lead interested products verification: passed={0} failed={1}" -f $script:Passed, $script:Failed)

    if ($hostProcess -and -not $hostProcess.HasExited) {
        try { $hostProcess.Kill($true) } catch { }
        try { $hostProcess.WaitForExit(30000) | Out-Null } catch { }
    }
    foreach ($name in @(
        'ConnectionStrings__UnicoreCRM','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT',
        'Development__ApplyMigrations','IdentityAuth__EmailVerification__Sender__Kind','UNICORE_DEV_SEED_ENABLED',
        'IdentityAuth__DevelopmentBootstrap__Enabled','IdentityAuth__DevelopmentBootstrap__Email',
        'IdentityAuth__DevelopmentBootstrap__Password','IdentityAuth__DevelopmentBootstrap__DisplayName',
        'Workspace__DevelopmentBootstrap__Enabled','AccessControl__DevelopmentBootstrap__Enabled',
        'Workflows__InitialWorkspaceProvisioning__ResumeEnabled','AI__Provider__Kind')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    if (-not $KeepDatabase) {
        try {
            Invoke-SqlNonQuery -Query @"
IF DB_ID('$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END;
"@
        }
        catch { }
    }
}

if ($script:Failed -ne 0) { throw 'Lead interested products verification failed.' }
Write-Host 'LEAD INTERESTED PRODUCTS: PASS'
