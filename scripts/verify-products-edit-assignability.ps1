<#
.SYNOPSIS
    Verifies that products.edit is assignable through AccessControl role management, and that an
    ordinarily assigned role makes replaceProduct, restoreProduct and restoreProductsBatch reachable.
.DESCRIPTION
    products.edit is the canonical Products modification capability: the pinned OpenAPI declares it
    for all three operations, the operation and command registries record it, the runtime enforces it,
    and it is the updateCapability published to the AccessControl record-access evaluator. The
    capability matrix previously carried the UI-side name products.update with no operation consumers,
    which made the capability unassignable. This verifier recomputes the frozen assignability rule
    from the authority file, asserts the runtime code projection is exactly it, and proves the
    delegation path end to end through the real endpoints.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5629,
    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$script:Passed = 0
$script:Failed = 0
$script:Results = [System.Collections.Generic.List[string]]::new()
$script:Counter = 0
$script:BaseUrl = "http://127.0.0.1:$Port"
$script:Token = $null
$script:WorkspaceId = $null
$script:HostProcess = $null
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionRoot = (Resolve-Path (Join-Path $repositoryRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost')).Path
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$password = 'Products-Edit-Assign!2026'
$email = 'admin@unicorecrm.local'
$matrixPath = Join-Path $solutionRoot 'design-authority/canonical-design/authority/capability-authorization-matrix.json'
$catalogPath = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/Common/AssignableCapabilityCatalog.cs'
$seedPolicyPath = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/ProvisionInitialWorkspaceAccess/InitialWorkspaceAccessPolicy.cs'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-products-edit-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $logRoot

function Add-Result([string] $Name, [object] $Expected, [object] $Actual) {
    $expectedText = [string]$Expected
    $actualText = [string]$Actual
    if ($expectedText -ceq $actualText) {
        $script:Passed++
        $script:Results.Add("PASS | $Name | $actualText")
    }
    else {
        $script:Failed++
        $script:Results.Add("FAIL | $Name | expected=$expectedText actual=$actualText")
    }
}

function Assert-True([string] $Name, [bool] $Condition) {
    Add-Result $Name 'True' $Condition.ToString()
}

function New-Connection([string] $Database = $DatabaseName) {
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$SqlServer;Database=$Database;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True")
    $connection.Open()
    return $connection
}

function Invoke-Sql([string] $Query, [string] $Database = $DatabaseName) {
    $connection = New-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $reader = $command.ExecuteReader()
        $rows = [System.Collections.Generic.List[object]]::new()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $name = $reader.GetName($index)
                if ([string]::IsNullOrWhiteSpace($name)) { $name = "Column$index" }
                $row[$name] = $reader.GetValue($index)
            }
            $rows.Add([pscustomobject]$row)
        }
        $reader.Close()
        return $rows
    }
    finally { $connection.Dispose() }
}

function Invoke-SqlNonQuery([string] $Query, [string] $Database = $DatabaseName) {
    $connection = New-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        $null = $command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Get-Scalar([string] $Query) {
    $rows = @(Invoke-Sql $Query)
    if ($rows.Count -eq 0) { return $null }
    return ($rows[0].PSObject.Properties | Select-Object -First 1).Value
}

function New-ApiRequest(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $IdempotencyKey,
    [string] $IfMatch,
    [string] $RawBody
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pea-' + $script:Counter.ToString('d6'))
    $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pea-' + $script:Counter.ToString('d6'))
    if ($Token -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($Token)) {
        $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token")
    }
    if ($WorkspaceId -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($WorkspaceId)) {
        $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId)
    }
    if ($IdempotencyKey -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey)
    }
    if ($IfMatch -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($IfMatch)) {
        $null = $request.Headers.TryAddWithoutValidation('If-Match', $IfMatch)
    }
    if ($RawBody -ne 'omit' -and -not [string]::IsNullOrWhiteSpace($RawBody)) {
        $request.Content = [System.Net.Http.StringContent]::new($RawBody, [Text.Encoding]::UTF8, 'application/json')
    }
    return $request
}

function Invoke-Request([object] $Request) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(120)
    try {
        $response = $client.SendAsync($Request).GetAwaiter().GetResult()
        try {
            $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $payload = $null
            if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { $payload = $null } }
            return [pscustomobject]@{ Status = [int]$response.StatusCode; Raw = $raw; Body = $payload }
        }
        finally { $response.Dispose() }
    }
    finally { $Request.Dispose(); $client.Dispose() }
}

function New-RoleBody([string] $Name, [string[]] $Capabilities) {
    return [ordered]@{ name = $Name; capabilities = $Capabilities; dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress -Depth 8
}

function New-ReplaceRoleBody([string] $Name, [string[]] $Capabilities) {
    return [ordered]@{ name = $Name; isActive = $true; capabilities = $Capabilities; dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress -Depth 8
}

function Invoke-CreateRole([string] $Body, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $key = 'idem-pea-role-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'POST' '/access/roles' $script:Token $Workspace $key 'omit' $Body)
}

function Invoke-ReplaceRole([string] $RoleId, [string] $Body, [string] $IfMatch) {
    $key = 'idem-pea-replace-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'PUT' "/access/roles/$RoleId" $script:Token $script:WorkspaceId $key $IfMatch $Body)
}

function Invoke-MemberAccess([string] $MembershipId, [string[]] $RoleIds, [string] $IfMatch) {
    $key = 'idem-pea-member-' + [Guid]::NewGuid().ToString('N')
    $body = [ordered]@{ roleIds = $RoleIds; teamIds = @() } | ConvertTo-Json -Compress -Depth 5
    return Invoke-Request (New-ApiRequest 'POST' "/access/members/$MembershipId/access" $script:Token $script:WorkspaceId $key $IfMatch $body)
}

function New-ProductBody([string] $Sku, [string] $Name = $null) {
    if ([string]::IsNullOrEmpty($Name)) { $Name = "Products Edit $Sku" }
    return [ordered]@{
        sku = $Sku; name = $Name; type = 'service'; status = 'ACTIVE'
        category = 'Software License'; unit = 'item'
        unitPrice = [ordered]@{ amount = '10.00'; currency = 'USD' }
        taxRate = '0'; taxMode = 'none'; billingCycle = 'one_time'
        isSubscription = $false; isRenewable = $false; tags = @()
    } | ConvertTo-Json -Compress -Depth 8
}

function New-Product([string] $Sku) {
    $key = 'idem-pea-prod-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'POST' '/products' $script:Token $script:WorkspaceId $key 'omit' (New-ProductBody $Sku))
}

function Invoke-ReplaceProduct([string] $ProductId, [string] $Sku, [string] $Name, [string] $IfMatch, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $key = 'idem-pea-repl-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'PUT' "/products/$ProductId" $script:Token $Workspace $key $IfMatch (New-ProductBody $Sku $Name))
}

function Invoke-ArchiveProduct([string] $ProductId, [string] $IfMatch) {
    $key = 'idem-pea-arch-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'POST' "/products/$ProductId/archive" $script:Token $script:WorkspaceId $key $IfMatch '{"reason":"assignability fixture"}')
}

function Invoke-RestoreProduct([string] $ProductId, [string] $IfMatch) {
    $key = 'idem-pea-rest-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'POST' "/products/$ProductId/restore" $script:Token $script:WorkspaceId $key $IfMatch '{}')
}

function Invoke-RestoreBatch([string] $ProductId, [long] $ExpectedVersion) {
    $key = 'idem-pea-restb-' + [Guid]::NewGuid().ToString('N')
    $body = [ordered]@{ items = @(@{ productId = $ProductId; expectedVersion = $ExpectedVersion }) } | ConvertTo-Json -Compress -Depth 6
    return Invoke-Request (New-ApiRequest 'POST' '/products/restore-batch' $script:Token $script:WorkspaceId $key 'omit' $body)
}

function Get-ProductVersion([string] $ProductId) {
    return [long](Get-Scalar "SELECT Version FROM products.Products WHERE ProductId=N'$ProductId'")
}

function Get-ProductName([string] $ProductId) {
    return [string](Get-Scalar "SELECT JSON_VALUE(Profile,'`$.name') FROM products.Products WHERE ProductId=N'$ProductId'")
}

function Get-RoleCapabilities([string] $RoleId) {
    return (@(Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId=N'$RoleId' ORDER BY Capability") | ForEach-Object { [string]$_.Capability }) -join ','
}

function Get-MemberAccessVersion([string] $MembershipId) {
    $value = Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE MembershipId=N'$MembershipId'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

function Start-ApiHost {
    $stdout = Join-Path $logRoot ('host.out.' + [Guid]::NewGuid().ToString('N') + '.log')
    $stderr = Join-Path $logRoot ('host.err.' + [Guid]::NewGuid().ToString('N') + '.log')
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Request (New-ApiRequest 'GET' '/auth/session' 'omit' 'omit' 'omit' 'omit' 'omit')
            if ($probe.Status -eq 401) { $ready = $true; break }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }
}

function Stop-ApiHost {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
    $script:HostProcess = $null
}

try {
    # =============================================================================================
    # A. The frozen assignability rule, recomputed from the authority file
    # =============================================================================================
    $matrix = Get-Content -Raw -LiteralPath $matrixPath | ConvertFrom-Json
    $catalogSource = Get-Content -Raw -LiteralPath $catalogPath
    $catalogValues = [string[]] @([regex]::Matches($catalogSource, '"([a-z][a-z0-9_.]*)"') | ForEach-Object { $_.Groups[1].Value })
    $derivedList = [string[]] @($matrix.capabilities |
        Where-Object {
            ($_.admittedStatus -ceq 'ADMITTED_IMPLEMENTED' -or $_.admittedStatus -ceq 'ADMITTED_NOT_IMPLEMENTED') -and
            (@($_.workspaceScope) -ccontains 'REQUIRED')
        } | ForEach-Object { [string]$_.capability })
    [System.Array]::Sort($derivedList, [System.StringComparer]::Ordinal)
    $sortedCatalog = [string[]] $catalogValues.Clone()
    [System.Array]::Sort($sortedCatalog, [System.StringComparer]::Ordinal)
    Add-Result 'code projection matches the authority derivation exactly' ($derivedList -join ',') ($catalogValues -join ',')
    Add-Result 'code projection is ordinal-sorted' ($sortedCatalog -join ',') ($catalogValues -join ',')

    $editRow = @($matrix.capabilities | Where-Object { $_.capability -ceq 'products.edit' })[0]
    Assert-True 'products.edit has exactly one catalog row' (@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.edit' }).Count -eq 1)
    Add-Result 'products.edit row key follows the convention' 'PRODUCTS_EDIT' ([string]$editRow.key)
    Add-Result 'products.edit admittedStatus is admitted' 'ADMITTED_NOT_IMPLEMENTED' ([string]$editRow.admittedStatus)
    Assert-True 'products.edit workspaceScope contains REQUIRED' (@($editRow.workspaceScope) -ccontains 'REQUIRED')
    Add-Result 'products.edit data scope' 'WORKSPACE' ((@($editRow.dataScopes)) -join ',')
    Add-Result 'products.edit resource scopes' 'RESOURCE,WORKSPACE' ((@($editRow.resourceScopes)) -join ',')
    Add-Result 'products.edit operation consumers' 'replaceProduct,restoreProduct,restoreProductsBatch' ((@($editRow.operationConsumers)) -join ',')
    Add-Result 'products.edit semantic owner' 'Products' ([string]$editRow.semanticOwner)
    Add-Result 'products.edit enforcement owner' 'AccessControl' ([string]$editRow.enforcementOwner)
    Assert-True 'products.edit is present in the code projection' ($catalogValues -ccontains 'products.edit')
    # The UI-side name is corrected, not duplicated: one concept, one row.
    Add-Result 'products.update is no longer a capability row' 0 (@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.update' }).Count)
    Add-Result 'matrix capability count unchanged by the correction' ([int]$matrix.capabilityCount) (@($matrix.capabilities).Count)
    Add-Result 'products.export remains without backend operation authority' 'NO_BACKEND_OPERATION_AUTHORITY' ([string](@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.export' })[0].admittedStatus))
    Add-Result 'BLOCKED rows unchanged by this correction' 7 (@($matrix.capabilities | Where-Object { $_.admittedStatus -ceq 'BLOCKED' }).Count)

    # The frozen provisioning seed already contained products.edit and is not extended here.
    $seedSource = Get-Content -Raw -LiteralPath $seedPolicyPath
    Assert-True 'frozen provisioning seed still carries products.edit unchanged' ($seedSource -match '"products\.edit"')
    Assert-True 'frozen provisioning seed gains no new capability' ($seedSource -notmatch 'products\.configure')

    # =============================================================================================
    # B. Runtime fixture. The bootstrap role can create and archive, but cannot edit.
    # =============================================================================================
    Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL THROW 50001, 'Verification database already exists.', 1; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = $connectionString
    $env:Development__ApplyMigrations = 'true'
    $env:UNICORE_DEV_SEED_ENABLED = 'true'
    $env:UNICORE_DEV_SEED_EMAIL = $email
    $env:UNICORE_DEV_SEED_PASSWORD = $password
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'access.configure'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__1 = 'products.create'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__2 = 'products.delete'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__3 = 'products.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    Start-ApiHost

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pea-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pea-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pea-signin')
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{ email = $email; password = $password } | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose(); $signInResponse.Dispose(); $signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    $accountId = [string](Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=N'$($email.ToUpperInvariant())'")
    $membershipId = [string](Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId=N'$script:WorkspaceId' AND AccountId=N'$accountId'")
    $bootstrapRoleId = [string](Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND c.Capability=N'access.configure'")
    Assert-True 'access.configure fixture exists' (-not [string]::IsNullOrWhiteSpace($bootstrapRoleId))

    # The seeded provisioning role already grants products.edit, and the development bootstrap merges
    # into that same role. The grant is stripped from every seeded role so the denial baseline below
    # is genuinely tested rather than satisfied by the seed; the capability is then reintroduced only
    # through the admitted delegation path under test.
    Add-Result 'seeded roles grant products.edit before the fixture strips it' 1 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE Capability=N'products.edit'"))
    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE Capability=N'products.edit'"
    Add-Result 'no role carries products.edit at the denial baseline' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE Capability=N'products.edit'"))
    Add-Result 'the bootstrap role retains its other capabilities' 1 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE RoleId=N'$bootstrapRoleId' AND Capability=N'access.configure'"))

    # =============================================================================================
    # C. Baseline: the three admitted operations are unreachable without the capability
    # =============================================================================================
    $created = New-Product 'PEA-0001'
    Add-Result 'fixture Product created' 201 $created.Status
    $productId = [string]$created.Body.result.product.id
    $version = [long]$created.Body.version

    $deniedReplace = Invoke-ReplaceProduct $productId 'PEA-0001' 'Denied Rename' ('"' + $version + '"')
    Add-Result 'replaceProduct denied without products.edit' 403 $deniedReplace.Status
    Add-Result 'replaceProduct denial uses ACCESS_DENIED' 'ACCESS_DENIED' ([string]$deniedReplace.Body.code)
    Add-Result 'denied replaceProduct changed nothing' 'Products Edit PEA-0001' (Get-ProductName $productId)
    Add-Result 'denied replaceProduct advanced no version' $version (Get-ProductVersion $productId)

    $version = Get-ProductVersion $productId
    $archived = Invoke-ArchiveProduct $productId ('"' + $version + '"')
    Add-Result 'fixture Product archived with products.delete' 200 $archived.Status
    $version = Get-ProductVersion $productId
    $deniedRestore = Invoke-RestoreProduct $productId ('"' + $version + '"')
    Add-Result 'restoreProduct denied without products.edit' 403 $deniedRestore.Status
    $deniedBatch = Invoke-RestoreBatch $productId $version
    Add-Result 'restoreProductsBatch denied without products.edit' 403 $deniedBatch.Status
    Add-Result 'denied restore left the Product archived' 1 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.Products WHERE ProductId=N'$productId' AND ArchivedAt IS NOT NULL"))

    # =============================================================================================
    # D. createAccessRole may grant products.edit
    # =============================================================================================
    $editRole = Invoke-CreateRole (New-RoleBody 'Product Editors' @('products.edit'))
    Add-Result 'createAccessRole accepts products.edit' 200 $editRole.Status
    $editRoleId = [string]$editRole.Body.aggregateId
    Add-Result 'capability persisted exactly' 'products.edit' (Get-RoleCapabilities $editRoleId)
    $mixed = Invoke-CreateRole (New-RoleBody 'Mixed Product Editors' @('products.edit', 'products.read', 'products.delete'))
    Add-Result 'products.edit combines with other admitted capabilities' 200 $mixed.Status
    Add-Result 'mixed role capabilities persisted' 'products.delete,products.edit,products.read' (Get-RoleCapabilities ([string]$mixed.Body.aggregateId))

    # =============================================================================================
    # E. Non-assignable capabilities are still rejected
    # =============================================================================================
    $rejections = @(
        @{ Name = 'blocked capability'; Value = 'contacts.create' },
        @{ Name = 'authority-gap capability'; Value = 'identity.account.recover' },
        @{ Name = 'reconciliation-required capability'; Value = 'studio.configure' },
        @{ Name = 'no-operation-authority capability'; Value = 'products.export' },
        @{ Name = 'retired UI capability name'; Value = 'products.update' },
        @{ Name = 'unknown capability'; Value = 'products.edit.extra' },
        @{ Name = 'wrong-case capability'; Value = 'Products.Edit' },
        @{ Name = 'upper-case capability'; Value = 'PRODUCTS.EDIT' },
        @{ Name = 'non-Workspace capability'; Value = 'identity.account.register' }
    )
    foreach ($case in $rejections) {
        $result = Invoke-CreateRole (New-RoleBody ('Rejected ' + $case.Value) @($case.Value))
        Add-Result "createAccessRole rejects $($case.Name)" 422 $result.Status
        Add-Result "createAccessRole $($case.Name) uses VALIDATION_FAILED" 'VALIDATION_FAILED' ([string]$result.Body.code)
        Add-Result "createAccessRole $($case.Name) indexes the offending position" 1 (@($result.Body.fieldErrors.PSObject.Properties.Name | Where-Object { $_ -ceq 'capabilities[0]' }).Count)
    }
    $padded = Invoke-CreateRole '{"name":"Padded Edit","capabilities":["  products.edit  "],"dataScopes":[],"fieldSecurity":[]}'
    Add-Result 'padded canonical capability is trimmed and accepted' 200 $padded.Status
    Add-Result 'padded capability persisted canonically' 'products.edit' (Get-RoleCapabilities ([string]$padded.Body.aggregateId))
    $duplicate = Invoke-CreateRole (New-RoleBody 'Duplicate Edit' @('products.edit', 'products.edit'))
    Add-Result 'duplicate products.edit rejected' 422 $duplicate.Status

    # =============================================================================================
    # F. replaceAccessRole may add and remove it
    # =============================================================================================
    $fixture = Invoke-CreateRole (New-RoleBody 'Replaceable Editors' @('products.read'))
    Add-Result 'replace fixture created' 200 $fixture.Status
    $fixtureId = [string]$fixture.Body.aggregateId
    $added = Invoke-ReplaceRole $fixtureId (New-ReplaceRoleBody 'Replaceable Editors' @('products.edit', 'products.read')) '"0"'
    Add-Result 'replaceAccessRole adds products.edit' 200 $added.Status
    Add-Result 'replacement persisted the added capability' 'products.edit,products.read' (Get-RoleCapabilities $fixtureId)
    $removedCapability = Invoke-ReplaceRole $fixtureId (New-ReplaceRoleBody 'Replaceable Editors' @('products.read')) '"1"'
    Add-Result 'replaceAccessRole removes products.edit' 200 $removedCapability.Status
    Add-Result 'removal persisted' 'products.read' (Get-RoleCapabilities $fixtureId)
    $bypass = Invoke-ReplaceRole $fixtureId (New-ReplaceRoleBody 'Replaceable Editors' @('products.update')) '"2"'
    Add-Result 'replaceAccessRole cannot bypass create-time capability rules' 422 $bypass.Status
    Add-Result 'rejected replacement changed nothing' 'products.read' (Get-RoleCapabilities $fixtureId)

    # =============================================================================================
    # G. Reachability through an ordinarily assigned role
    # =============================================================================================
    $memberVersion = Get-MemberAccessVersion $membershipId
    $assigned = Invoke-MemberAccess $membershipId @($bootstrapRoleId, $editRoleId) ('"' + $memberVersion + '"')
    Add-Result 'replaceWorkspaceMemberAccess assigns the editor role' 200 $assigned.Status

    $version = Get-ProductVersion $productId
    $restore = Invoke-RestoreProduct $productId ('"' + $version + '"')
    Add-Result 'restoreProduct reachable with an ordinarily assigned role' 200 $restore.Status
    Add-Result 'restoreProduct actually restored the Product' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.Products WHERE ProductId=N'$productId' AND ArchivedAt IS NOT NULL"))

    $version = Get-ProductVersion $productId
    $replace = Invoke-ReplaceProduct $productId 'PEA-0001' 'Granted Rename' ('"' + $version + '"')
    Add-Result 'replaceProduct reachable with an ordinarily assigned role' 200 $replace.Status
    Add-Result 'replaceProduct actually applied the change' 'Granted Rename' (Get-ProductName $productId)

    $version = Get-ProductVersion $productId
    $archivedAgain = Invoke-ArchiveProduct $productId ('"' + $version + '"')
    Add-Result 'fixture re-archived for the batch path' 200 $archivedAgain.Status
    $version = Get-ProductVersion $productId
    $batch = Invoke-RestoreBatch $productId $version
    Add-Result 'restoreProductsBatch reachable with an ordinarily assigned role' 200 $batch.Status
    Add-Result 'restoreProductsBatch actually restored the Product' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.Products WHERE ProductId=N'$productId' AND ArchivedAt IS NOT NULL"))

    # Removing the capability from the assigned role revokes reachability again.
    $revoke = Invoke-ReplaceRole $editRoleId (New-ReplaceRoleBody 'Product Editors' @('products.read')) '"0"'
    Add-Result 'capability removed from the assigned role' 200 $revoke.Status
    $version = Get-ProductVersion $productId
    $revoked = Invoke-ReplaceProduct $productId 'PEA-0001' 'Revoked Rename' ('"' + $version + '"')
    Add-Result 'replaceProduct denied again after the capability is removed' 403 $revoked.Status
    Add-Result 'revoked replaceProduct changed nothing' 'Granted Rename' (Get-ProductName $productId)
    $restoreRevoked = Invoke-RestoreProduct $productId ('"' + $version + '"')
    Add-Result 'restoreProduct denied again after the capability is removed' 403 $restoreRevoked.Status

    $restored = Invoke-ReplaceRole $editRoleId (New-ReplaceRoleBody 'Product Editors' @('products.edit')) '"1"'
    Add-Result 'capability restored on the assigned role' 200 $restored.Status
    $version = Get-ProductVersion $productId
    $reachableAgain = Invoke-ReplaceProduct $productId 'PEA-0001' 'Restored Rename' ('"' + $version + '"')
    Add-Result 'replaceProduct reachable again after the capability is restored' 200 $reachableAgain.Status
    Add-Result 'restored replaceProduct applied the change' 'Restored Rename' (Get-ProductName $productId)

    # =============================================================================================
    # H. Workspace isolation
    # =============================================================================================
    Add-Result 'the granting role exists only in its own Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE RoleId=N'$editRoleId' AND WorkspaceId=N'$foreignWorkspace'"))
    Add-Result 'no capability grant leaked into the foreign Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId=N'$foreignWorkspace' AND c.Capability=N'products.edit'"))
    $foreignReplace = Invoke-ReplaceProduct $productId 'PEA-0001' 'Foreign Rename' ('"' + (Get-ProductVersion $productId) + '"') $foreignWorkspace
    Add-Result 'the capability does not reach a foreign Workspace' 403 $foreignReplace.Status
    Add-Result 'foreign attempt changed nothing' 'Restored Rename' (Get-ProductName $productId)
    $foreignRole = Invoke-CreateRole (New-RoleBody 'Foreign Editor Role' @('products.edit')) $foreignWorkspace
    Add-Result 'role creation in a non-member Workspace denied' 403 $foreignRole.Status
    Add-Result 'no role created in the foreign Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE WorkspaceId=N'$foreignWorkspace'"))
}
finally {
    Stop-ApiHost
    if (-not $KeepDatabase) {
        try { Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END" 'master' } catch { }
        Remove-Item -LiteralPath $logRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Host "Verification logs retained at $logRoot"
    }
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { throw "products.edit assignability verification failed: $script:Failed check(s)." }
