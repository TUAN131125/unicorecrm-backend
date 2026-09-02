<#
.SYNOPSIS
    Verifies that products.configure is assignable through AccessControl role management, and that
    an ordinarily assigned role makes updateProductConfigurationType reachable.
.DESCRIPTION
    The frozen assignability rule (create-access-role-authority.md section 1, reused verbatim by
    replace-access-role-authority.md) admits a capability if and only if one
    capability-authorization-matrix.json row has an exact ordinal capability match, an admittedStatus
    of ADMITTED_IMPLEMENTED or ADMITTED_NOT_IMPLEMENTED, and a workspaceScope containing REQUIRED.
    This verifier recomputes that rule from the authority file, asserts the runtime code projection
    is exactly it, and then proves the runtime behaviour end to end through the real endpoints.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5621,
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
$password = 'Products-Configure-Assign!2026'
$email = 'admin@unicorecrm.local'
$matrixPath = Join-Path $solutionRoot 'design-authority/canonical-design/authority/capability-authorization-matrix.json'
$catalogPath = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/Common/AssignableCapabilityCatalog.cs'
$seedPolicyPath = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform/AccessControl/Application/ProvisionInitialWorkspaceAccess/InitialWorkspaceAccessPolicy.cs'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-products-configure-' + [Guid]::NewGuid().ToString('N'))
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
    $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pca-' + $script:Counter.ToString('d6'))
    $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pca-' + $script:Counter.ToString('d6'))
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

# replaceAccessRole requires isActive and requires it to be true: the operation replaces mutable
# configuration and cannot change the active state of a role.
function New-ReplaceBody([string] $Name, [string[]] $Capabilities) {
    return [ordered]@{ name = $Name; isActive = $true; capabilities = $Capabilities; dataScopes = @(); fieldSecurity = @() } | ConvertTo-Json -Compress -Depth 8
}

function Invoke-CreateRole([string] $Body, [string] $Key = $null, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Key)) { $Key = 'idem-pca-role-' + [Guid]::NewGuid().ToString('N') }
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    return Invoke-Request (New-ApiRequest 'POST' '/access/roles' $script:Token $Workspace $Key 'omit' $Body)
}

function Invoke-ReplaceRole([string] $RoleId, [string] $Body, [string] $IfMatch, [string] $Key = $null) {
    if ([string]::IsNullOrEmpty($Key)) { $Key = 'idem-pca-replace-' + [Guid]::NewGuid().ToString('N') }
    return Invoke-Request (New-ApiRequest 'PUT' "/access/roles/$RoleId" $script:Token $script:WorkspaceId $Key $IfMatch $Body)
}

function Invoke-MemberAccess([string] $MembershipId, [string[]] $RoleIds, [string] $IfMatch, [string] $Key = $null) {
    if ([string]::IsNullOrEmpty($Key)) { $Key = 'idem-pca-member-' + [Guid]::NewGuid().ToString('N') }
    $body = [ordered]@{ roleIds = $RoleIds; teamIds = @() } | ConvertTo-Json -Compress -Depth 5
    return Invoke-Request (New-ApiRequest 'POST' "/access/members/$MembershipId/access" $script:Token $script:WorkspaceId $Key $IfMatch $body)
}

function Invoke-ConfigurationPatch([string] $TypeId, [string] $Status, [string] $IfMatch, [string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $body = @{ status = $Status } | ConvertTo-Json -Compress
    $key = 'idem-pca-config-' + [Guid]::NewGuid().ToString('N')
    return Invoke-Request (New-ApiRequest 'PATCH' "/products/configuration/types/$TypeId" $script:Token $Workspace $key $IfMatch $body)
}

function Get-RoleCapabilities([string] $RoleId) {
    return (@(Invoke-Sql "SELECT Capability FROM access.RoleCapabilities WHERE RoleId=N'$RoleId' ORDER BY Capability") | ForEach-Object { [string]$_.Capability }) -join ','
}

function Get-RoleVersion([string] $RoleId) {
    $value = Get-Scalar "SELECT Version FROM access.Roles WHERE RoleId=N'$RoleId'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

function Get-MemberAccessVersion([string] $MembershipId) {
    $value = Get-Scalar "SELECT Version FROM access.MemberAccessVersions WHERE MembershipId=N'$MembershipId'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

function Get-ConfigurationRevision([string] $Workspace = $null) {
    if ([string]::IsNullOrEmpty($Workspace)) { $Workspace = $script:WorkspaceId }
    $value = Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'"
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
    # The three frozen conditions, applied verbatim rather than restated.
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

    $configureRow = @($matrix.capabilities | Where-Object { $_.capability -ceq 'products.configure' })[0]
    Assert-True 'products.configure has exactly one catalog row' (@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.configure' }).Count -eq 1)
    Add-Result 'products.configure admittedStatus is admitted' 'ADMITTED_NOT_IMPLEMENTED' ([string]$configureRow.admittedStatus)
    Assert-True 'products.configure workspaceScope contains REQUIRED' (@($configureRow.workspaceScope) -ccontains 'REQUIRED')
    Add-Result 'products.configure semantic owner' 'Products' ([string]$configureRow.semanticOwner)
    Add-Result 'products.configure enforcement owner' 'AccessControl' ([string]$configureRow.enforcementOwner)
    Add-Result 'products.configure data scope' 'WORKSPACE' ((@($configureRow.dataScopes)) -join ',')
    Assert-True 'products.configure is present in the code projection' ($catalogValues -ccontains 'products.configure')

    # The remaining non-assignable classes are untouched by this closure.
    foreach ($blocked in @('contacts.create', 'contacts.update', 'organizations.create', 'organizations.update', 'customers.onboard_existing', 'customers.edit', 'payments.customer_credit.allocate')) {
        $row = @($matrix.capabilities | Where-Object { $_.capability -ceq $blocked })[0]
        Add-Result "$blocked remains BLOCKED" 'BLOCKED' ([string]$row.admittedStatus)
        Assert-True "$blocked stays out of the code projection" (-not ($catalogValues -ccontains $blocked))
    }
    Add-Result 'BLOCKED rows remaining in the matrix' 7 (@($matrix.capabilities | Where-Object { $_.admittedStatus -ceq 'BLOCKED' }).Count)
    Add-Result 'studio.configure remains unreconciled' 'CONTRACT_READY_REQUIRES_RECONCILIATION' ([string](@($matrix.capabilities | Where-Object { $_.capability -ceq 'studio.configure' })[0].admittedStatus))
    Add-Result 'products.export remains without backend operation authority' 'NO_BACKEND_OPERATION_AUTHORITY' ([string](@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.export' })[0].admittedStatus))
    # products.update was the UI-side name for the Products modification capability and has been
    # corrected to the canonical server capability products.edit. It must no longer exist as a row.
    Add-Result 'products.update is no longer a capability row' 0 (@($matrix.capabilities | Where-Object { $_.capability -ceq 'products.update' }).Count)
    Add-Result 'matrix capability count unchanged' ([int]$matrix.capabilityCount) (@($matrix.capabilities).Count)

    # The frozen initial-provisioning seed is explicitly not extended by this closure.
    $seedSource = Get-Content -Raw -LiteralPath $seedPolicyPath
    Assert-True 'frozen provisioning seed does not gain products.configure' ($seedSource -notmatch 'products\.configure')

    # =============================================================================================
    # B. Runtime fixture. The bootstrap role deliberately does NOT carry products.configure.
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
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    Start-ApiHost

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pca-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pca-signin')
    $null = $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key', 'idem-pca-signin')
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
    Add-Result 'no seeded or bootstrapped role carries products.configure' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities WHERE Capability=N'products.configure'"))

    # =============================================================================================
    # C. Baseline: the admitted Products operation is unreachable without the capability
    # =============================================================================================
    $before = Invoke-ConfigurationPatch 'service' 'INACTIVE' '"0"'
    Add-Result 'mutation denied before the capability is assigned' 403 $before.Status
    Add-Result 'denial uses ACCESS_DENIED' 'ACCESS_DENIED' ([string]$before.Body.code)
    Add-Result 'denied mutation wrote no configuration state' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides"))

    # =============================================================================================
    # D. createAccessRole may grant products.configure
    # =============================================================================================
    $created = Invoke-CreateRole (New-RoleBody 'Product Configuration Administrators' @('products.configure'))
    Add-Result 'createAccessRole accepts products.configure' 200 $created.Status
    $configureRoleId = [string]$created.Body.aggregateId
    Assert-True 'created role ID format' ($configureRoleId -cmatch '^role_[0-9a-f]{32}$')
    Add-Result 'capability persisted exactly' 'products.configure' (Get-RoleCapabilities $configureRoleId)
    Add-Result 'created role is Workspace-scoped' $script:WorkspaceId ([string](Get-Scalar "SELECT WorkspaceId FROM access.Roles WHERE RoleId=N'$configureRoleId'"))
    Add-Result 'created role version' 0 (Get-RoleVersion $configureRoleId)
    # Assignable alongside other admitted capabilities, not only in isolation.
    $mixed = Invoke-CreateRole (New-RoleBody 'Mixed Product Role' @('products.configure', 'products.read', 'studio.read'))
    Add-Result 'products.configure combines with other admitted capabilities' 200 $mixed.Status
    Add-Result 'mixed role capabilities persisted' 'products.configure,products.read,studio.read' (Get-RoleCapabilities ([string]$mixed.Body.aggregateId))

    # =============================================================================================
    # E. Non-assignable capabilities are still rejected, by both operations
    # =============================================================================================
    $rejections = @(
        @{ Name = 'blocked capability'; Value = 'contacts.create' },
        @{ Name = 'authority-gap capability'; Value = 'identity.account.recover' },
        @{ Name = 'reconciliation-required capability'; Value = 'studio.configure' },
        @{ Name = 'no-operation-authority capability'; Value = 'products.export' },
        @{ Name = 'retired UI capability name'; Value = 'products.update' },
        @{ Name = 'unknown capability'; Value = 'products.configure.extra' },
        @{ Name = 'wrong-case capability'; Value = 'Products.Configure' },
        @{ Name = 'upper-case capability'; Value = 'PRODUCTS.CONFIGURE' },
        @{ Name = 'non-Workspace capability'; Value = 'identity.account.register' }
    )
    foreach ($case in $rejections) {
        $result = Invoke-CreateRole (New-RoleBody ('Rejected ' + $case.Value) @($case.Value))
        Add-Result "createAccessRole rejects $($case.Name)" 422 $result.Status
        Add-Result "createAccessRole $($case.Name) uses VALIDATION_FAILED" 'VALIDATION_FAILED' ([string]$result.Body.code)
        Add-Result "createAccessRole $($case.Name) indexes the offending position" 1 (@($result.Body.fieldErrors.PSObject.Properties.Name | Where-Object { $_ -ceq 'capabilities[0]' }).Count)
    }
    # A whitespace-padded value is trimmed to the canonical spelling and stays assignable; a padded
    # non-assignable value is still rejected after trimming.
    $padded = Invoke-CreateRole ('{"name":"Padded Capability","capabilities":["  products.configure  "],"dataScopes":[],"fieldSecurity":[]}')
    Add-Result 'padded canonical capability is trimmed and accepted' 200 $padded.Status
    Add-Result 'padded capability persisted canonically' 'products.configure' (Get-RoleCapabilities ([string]$padded.Body.aggregateId))
    $duplicate = Invoke-CreateRole (New-RoleBody 'Duplicate Configure' @('products.configure', 'products.configure'))
    Add-Result 'duplicate products.configure rejected' 422 $duplicate.Status

    # =============================================================================================
    # F. replaceAccessRole may add and remove it, and cannot bypass create-time rules
    # =============================================================================================
    $addBack = Invoke-CreateRole (New-RoleBody 'Replaceable Product Role' @('products.read'))
    Add-Result 'replace fixture created' 200 $addBack.Status
    $replaceRoleId = [string]$addBack.Body.aggregateId
    $added = Invoke-ReplaceRole $replaceRoleId (New-ReplaceBody 'Replaceable Product Role' @('products.configure', 'products.read')) '"0"'
    Add-Result 'replaceAccessRole adds products.configure' 200 $added.Status
    Add-Result 'replacement persisted the added capability' 'products.configure,products.read' (Get-RoleCapabilities $replaceRoleId)
    Add-Result 'replacement advanced the role version' 1 (Get-RoleVersion $replaceRoleId)
    $removed = Invoke-ReplaceRole $replaceRoleId (New-ReplaceBody 'Replaceable Product Role' @('products.read')) '"1"'
    Add-Result 'replaceAccessRole removes products.configure' 200 $removed.Status
    Add-Result 'removal persisted' 'products.read' (Get-RoleCapabilities $replaceRoleId)
    $replaceBlocked = Invoke-ReplaceRole $replaceRoleId (New-ReplaceBody 'Replaceable Product Role' @('contacts.create')) '"2"'
    Add-Result 'replaceAccessRole cannot bypass create-time capability rules' 422 $replaceBlocked.Status
    Add-Result 'replacement rejection uses VALIDATION_FAILED' 'VALIDATION_FAILED' ([string]$replaceBlocked.Body.code)
    Add-Result 'rejected replacement changed nothing' 'products.read' (Get-RoleCapabilities $replaceRoleId)
    $replaceWrongCase = Invoke-ReplaceRole $replaceRoleId (New-ReplaceBody 'Replaceable Product Role' @('Products.Configure')) '"2"'
    Add-Result 'replaceAccessRole rejects wrong-case products.configure' 422 $replaceWrongCase.Status

    # =============================================================================================
    # G. Reachability through an ordinarily assigned role
    # =============================================================================================
    $memberVersion = Get-MemberAccessVersion $membershipId
    $assigned = Invoke-MemberAccess $membershipId @($bootstrapRoleId, $configureRoleId) ('"' + $memberVersion + '"')
    Add-Result 'replaceWorkspaceMemberAccess assigns the role' 200 $assigned.Status
    Add-Result 'assignment persisted' 2 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId=N'$script:WorkspaceId' AND MembershipId=N'$membershipId'"))
    $reachable = Invoke-ConfigurationPatch 'service' 'INACTIVE' '"0"'
    Add-Result 'mutation reachable with an ordinarily assigned role' 200 $reachable.Status
    Add-Result 'mutation committed the effective status' 'INACTIVE' ([string](@($reachable.Body.result.data.types | Where-Object { $_.code -ceq 'service' })[0].status))
    Add-Result 'mutation advanced the configuration revision' 1 (Get-ConfigurationRevision)
    Add-Result 'mutation persisted the override' 'INACTIVE' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
    Add-Result 'mutation recorded immutable command audit' 1 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.AuditRecords WHERE Operation=N'updateProductConfigurationType'"))

    # Removing the capability from the assigned role revokes reachability again.
    $revoke = Invoke-ReplaceRole $configureRoleId (New-ReplaceBody 'Product Configuration Administrators' @('products.read')) '"0"'
    Add-Result 'capability removed from the assigned role' 200 $revoke.Status
    $revoked = Invoke-ConfigurationPatch 'service' 'ACTIVE' '"1"'
    Add-Result 'mutation denied again after the capability is removed' 403 $revoked.Status
    Add-Result 'revoked mutation advanced no revision' 1 (Get-ConfigurationRevision)
    $restore = Invoke-ReplaceRole $configureRoleId (New-ReplaceBody 'Product Configuration Administrators' @('products.configure')) '"1"'
    Add-Result 'capability restored on the assigned role' 200 $restore.Status
    $restored = Invoke-ConfigurationPatch 'service' 'ACTIVE' '"1"'
    Add-Result 'mutation reachable again after the capability is restored' 200 $restored.Status
    Add-Result 'restored mutation advanced the revision' 2 (Get-ConfigurationRevision)

    # =============================================================================================
    # H. Workspace isolation
    # =============================================================================================
    Add-Result 'the granting role exists only in its own Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.Roles WHERE RoleId=N'$configureRoleId' AND WorkspaceId=N'$foreignWorkspace'"))
    Add-Result 'no capability grant leaked into the foreign Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.RoleCapabilities c JOIN access.Roles r ON r.RoleId=c.RoleId WHERE r.WorkspaceId=N'$foreignWorkspace' AND c.Capability=N'products.configure'"))
    Add-Result 'no role assignment leaked into the foreign Workspace' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM access.MembershipRoleAssignments WHERE WorkspaceId=N'$foreignWorkspace' AND RoleId=N'$configureRoleId'"))
    $foreignPatch = Invoke-ConfigurationPatch 'service' 'INACTIVE' '"0"' $foreignWorkspace
    Add-Result 'the capability does not reach a foreign Workspace' 403 $foreignPatch.Status
    Add-Result 'foreign Workspace configuration untouched' 0 ([long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$foreignWorkspace'"))
    $foreignRole = Invoke-CreateRole (New-RoleBody 'Foreign Configure Role' @('products.configure')) $null $foreignWorkspace
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
if ($script:Failed -ne 0) { throw "products.configure assignability verification failed: $script:Failed check(s)." }
