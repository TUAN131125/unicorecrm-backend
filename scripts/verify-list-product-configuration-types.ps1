<#
.SYNOPSIS
    Verifies GET /products/configuration/types against an isolated SQL database and the real ApiHost.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5607,
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
$script:SecondHostProcess = $null
$script:SecondBaseUrl = "http://127.0.0.1:$($Port + 1)"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost')).Path
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$password = 'Product-Config-Verify!2026'
$email = 'admin@unicorecrm.local'
$canonicalOrder = 'physical_product,service,subscription,package,implementation,support_sla,addon,license,maintenance'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-product-configuration-' + [Guid]::NewGuid().ToString('N'))
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

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $RequestId = $null,
    [string] $CorrelationId = $null
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:BaseUrl$Path")
    if ($RequestId -ne 'omit') {
        if ([string]::IsNullOrEmpty($RequestId)) { $RequestId = 'req-product-config-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', $RequestId)
    }
    if ($CorrelationId -ne 'omit') {
        if ([string]::IsNullOrEmpty($CorrelationId)) { $CorrelationId = 'corr-product-config-' + $script:Counter.ToString('d6') }
        $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', $CorrelationId)
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) { try { $payload = $raw | ConvertFrom-Json } catch { } }
        $etag = $null
        if ($null -ne $response.Headers.ETag) { $etag = $response.Headers.ETag.ToString() }
        return [pscustomobject]@{ Status=[int]$response.StatusCode; Raw=$raw; Body=$payload; ETag=$etag }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function Invoke-Configuration(
    [string] $Workspace = $script:WorkspaceId,
    [string] $Token = $script:Token
) {
    if ($Token -eq 'omit') { $Token = $null }
    return Invoke-Api 'GET' '/products/configuration/types' $Token $Workspace $null $null
}

function Get-ReadEvidenceCount {
    return [long](Get-Scalar "SELECT COUNT_BIG(*) FROM products.AuditRecords WHERE Operation=N'listProductConfigurationTypes'")
}

function Get-ConfigurationRowSnapshot {
    return [string]::Join('|', @(
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.ProductConfigurationDocuments'),
        (Get-Scalar 'SELECT COUNT_BIG(*) FROM products.ProductConfigurationTypeOverrides'),
        (Get-Scalar 'SELECT COALESCE(SUM(Revision),0) FROM products.ProductConfigurationDocuments')
    ))
}

function Get-Codes([object] $Body) {
    return [string]::Join(',', @($Body.data.types | ForEach-Object { [string]$_.code }))
}

function Get-Statuses([object] $Body) {
    return [string]::Join(',', @($Body.data.types | ForEach-Object { [string]$_.status }))
}

function Get-StatusOf([object] $Body, [string] $Code) {
    return [string](@($Body.data.types | Where-Object { $_.code -ceq $Code })[0].status)
}

function Set-Revision([string] $Workspace, [long] $Revision) {
    Invoke-SqlNonQuery "IF EXISTS(SELECT 1 FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace') UPDATE products.ProductConfigurationDocuments SET Revision=$Revision WHERE WorkspaceId=N'$Workspace' ELSE INSERT INTO products.ProductConfigurationDocuments(WorkspaceId,Revision) VALUES(N'$Workspace',$Revision);"
}

function Set-Override([string] $Workspace, [string] $Code, [string] $Status) {
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTypeOverrides(WorkspaceId,ProductTypeCode,Status) VALUES(N'$Workspace',N'$Code',N'$Status');"
}

function Start-ApiHost {
    $stdout = Join-Path $logRoot ('host.out.' + [Guid]::NewGuid().ToString('N') + '.log')
    $stderr = Join-Path $logRoot ('host.err.' + [Guid]::NewGuid().ToString('N') + '.log')
    $script:HostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:HostProcess.HasExited) { throw "ApiHost exited during startup: $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null 'omit' 'omit'
            if ($probe.Status -eq 401) { $ready=$true; break }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "ApiHost did not become ready. Logs: $stdout $stderr" }
}

# A genuinely separate ApiHost process on its own port against the same database. Correctness that
# depended on process memory would not survive competing requests split across the two.
function Start-SecondApiHost {
    $stdout = Join-Path $logRoot ('host2.out.' + [Guid]::NewGuid().ToString('N') + '.log')
    $stderr = Join-Path $logRoot ('host2.err.' + [Guid]::NewGuid().ToString('N') + '.log')
    $previousUrls = $env:ASPNETCORE_URLS
    $env:ASPNETCORE_URLS = $script:SecondBaseUrl
    try {
        $script:SecondHostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    }
    finally { $env:ASPNETCORE_URLS = $previousUrls }
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($script:SecondHostProcess.HasExited) { throw "Second ApiHost exited: $(Get-Content -Raw $stderr)" }
        try {
            $probeRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$script:SecondBaseUrl/auth/session")
            $probeClient = [System.Net.Http.HttpClient]::new()
            $probeClient.Timeout = [TimeSpan]::FromSeconds(20)
            $probeResponse = $probeClient.SendAsync($probeRequest).GetAwaiter().GetResult()
            $probeStatus = [int]$probeResponse.StatusCode
            $probeResponse.Dispose(); $probeRequest.Dispose(); $probeClient.Dispose()
            if ($probeStatus -eq 401) { return }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.AggregateException] { }
        Start-Sleep -Milliseconds 500
    }
    throw "Second ApiHost did not become ready. Logs: $stdout $stderr"
}

function Stop-SecondApiHost {
    if ($null -ne $script:SecondHostProcess -and -not $script:SecondHostProcess.HasExited) {
        Stop-Process -Id $script:SecondHostProcess.Id -Force
        $script:SecondHostProcess.WaitForExit()
    }
    $script:SecondHostProcess = $null
}

function Stop-ApiHost {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
    $script:HostProcess = $null
}

# Fires $Count GET requests that are genuinely in flight together, optionally spread across several
# hosts, and returns their statuses. Sequential calls cannot exercise a race; these overlap.
function Invoke-ConcurrentConfiguration([int] $Count, [string[]] $BaseUrls = @($script:BaseUrl)) {
    $clients = [System.Collections.Generic.List[object]]::new()
    $requests = [System.Collections.Generic.List[object]]::new()
    $tasks = [System.Collections.Generic.List[object]]::new()
    try {
        for ($index = 0; $index -lt $Count; $index++) {
            $baseUrl = $BaseUrls[$index % $BaseUrls.Count]
            $request = [System.Net.Http.HttpRequestMessage]::new(
                [System.Net.Http.HttpMethod]::Get, "$baseUrl/products/configuration/types")
            $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-pc-conc-' + [Guid]::NewGuid().ToString('N'))
            $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-pc-conc-' + [Guid]::NewGuid().ToString('N'))
            $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:Token")
            $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $script:WorkspaceId)
            $handler = [System.Net.Http.HttpClientHandler]::new()
            $handler.UseProxy = $false
            $client = [System.Net.Http.HttpClient]::new($handler, $true)
            $client.Timeout = [TimeSpan]::FromSeconds(90)
            $clients.Add($client)
            $requests.Add($request)
            # Not awaited here: every request is started before any is observed.
            $tasks.Add($client.SendAsync($request))
        }
        [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
        $statuses = [System.Collections.Generic.List[int]]::new()
        foreach ($task in $tasks) { $statuses.Add([int]$task.Result.StatusCode); $task.Result.Dispose() }
        return $statuses.ToArray()
    }
    finally {
        foreach ($request in $requests) { $request.Dispose() }
        foreach ($client in $clients) { $client.Dispose() }
    }
}

# Runs the production monotonic upsert from an independent SQL session, so two sessions can be held
# overlapping deliberately. The shape is asserted against the production source below, so this cannot
# silently drift into testing a different statement than the runtime executes.
function New-TrustMergeCommand([System.Data.SqlClient.SqlConnection] $Connection, [string] $Workspace, [long] $Revision) {
    $command = $Connection.CreateCommand()
    $command.CommandText = @'
MERGE [products].[ProductConfigurationTrustedRevisions] WITH (HOLDLOCK) AS target
USING (SELECT @ws AS WorkspaceId, @rev AS GreatestTrustedRevision) AS source
ON target.[WorkspaceId] = source.[WorkspaceId]
WHEN MATCHED AND target.[GreatestTrustedRevision] < source.[GreatestTrustedRevision]
    THEN UPDATE SET target.[GreatestTrustedRevision] = source.[GreatestTrustedRevision]
WHEN NOT MATCHED AND source.[GreatestTrustedRevision] > 0
    THEN INSERT ([WorkspaceId], [GreatestTrustedRevision])
         VALUES (source.[WorkspaceId], source.[GreatestTrustedRevision]);
'@
    $null = $command.Parameters.AddWithValue('@ws', $Workspace)
    $null = $command.Parameters.AddWithValue('@rev', $Revision)
    $command.CommandTimeout = 60
    return $command
}

# Holds $FirstRevision in an open transaction so $SecondRevision genuinely blocks on its lock, then
# releases the first and lets the second proceed. This is real overlap, not two sequential writes.
function Invoke-OverlappingTrustMerge([string] $Workspace, [long] $FirstRevision, [long] $SecondRevision) {
    $first = New-Connection
    $second = New-Connection
    try {
        $firstTransaction = $first.BeginTransaction()
        $firstCommand = New-TrustMergeCommand $first $Workspace $FirstRevision
        $firstCommand.Transaction = $firstTransaction
        $null = $firstCommand.ExecuteNonQuery()

        $secondTransaction = $second.BeginTransaction()
        $secondCommand = New-TrustMergeCommand $second $Workspace $SecondRevision
        $secondCommand.Transaction = $secondTransaction
        # Started while the first transaction still holds its lock, so it is blocked, not serialised
        # by the test harness.
        $pending = $secondCommand.BeginExecuteNonQuery()
        Start-Sleep -Milliseconds 250
        $blocked = -not $pending.IsCompleted
        $firstTransaction.Commit()
        $null = $secondCommand.EndExecuteNonQuery($pending)
        $secondTransaction.Commit()
        return $blocked
    }
    finally { $first.Dispose(); $second.Dispose() }
}

function Get-TrustedRevision([string] $Workspace) {
    $value = Get-Scalar "SELECT GreatestTrustedRevision FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace'"
    if ($null -eq $value) { return [long]0 }
    return [long]$value
}

# Resets the Workspace to a pristine state. Trusted history is cleared too, because a Workspace that
# has served revision 5 legitimately refuses to serve a lower one - that is the rollback guard, and a
# fixture that lowered the anchor without resetting trust would be testing the guard, not the case it
# intends. The rollback test below deliberately does NOT use this between its two reads.
function Clear-Configuration([string] $Workspace) {
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$Workspace'; DELETE FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$Workspace';"
}

try {
    Invoke-SqlNonQuery "IF DB_ID(N'$DatabaseName') IS NOT NULL THROW 50001, 'Verification database already exists.', 1; CREATE DATABASE [$DatabaseName];" 'master'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $script:BaseUrl
    $env:ConnectionStrings__UnicoreCRM = $connectionString
    $env:Development__ApplyMigrations = 'true'
    $env:UNICORE_DEV_SEED_ENABLED = 'true'
    $env:UNICORE_DEV_SEED_EMAIL = $email
    $env:UNICORE_DEV_SEED_PASSWORD = $password
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'studio.read'
    # Ordinary Product capabilities are granted so the regression below exercises a real Product
    # mutation rather than an authorization failure that would prove nothing about revision isolation.
    $env:AccessControl__DevelopmentBootstrap__Capabilities__1 = 'products.create'
    $env:AccessControl__DevelopmentBootstrap__Capabilities__2 = 'products.read'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    Start-ApiHost

    $signInRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/auth/sessions")
    $signInRequest.Headers.TryAddWithoutValidation('X-Request-Id','req-product-config-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-product-config-signin') | Out-Null
    $signInRequest.Headers.TryAddWithoutValidation('Idempotency-Key','idem-product-config-signin') | Out-Null
    $signInRequest.Content = [System.Net.Http.StringContent]::new((@{email=$email;password=$password}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json')
    $signInClient = [System.Net.Http.HttpClient]::new()
    $signInResponse = $signInClient.SendAsync($signInRequest).GetAwaiter().GetResult()
    $signInBody = $signInResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    Add-Result 'authentication fixture sign-in' 200 ([int]$signInResponse.StatusCode)
    $script:Token = [string]$signInBody.accessToken
    $signInRequest.Dispose();$signInResponse.Dispose();$signInClient.Dispose()

    $script:WorkspaceId = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'")
    $foreignWorkspace = [string](Get-Scalar "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo-isolated'")
    $accountId = [string](Get-Scalar "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=N'$($email.ToUpperInvariant())'")
    $membershipId = [string](Get-Scalar "SELECT MembershipId FROM workspace.Memberships WHERE WorkspaceId=N'$script:WorkspaceId' AND AccountId=N'$accountId'")
    $roleId = [string](Get-Scalar "SELECT TOP 1 a.RoleId FROM access.MembershipRoleAssignments a JOIN access.RoleCapabilities c ON c.RoleId=a.RoleId WHERE a.WorkspaceId=N'$script:WorkspaceId' AND a.MembershipId=N'$membershipId' AND c.Capability=N'studio.read'")
    Assert-True 'trusted studio.read fixture exists' (-not [string]::IsNullOrWhiteSpace($roleId))
    Assert-True 'foreign Workspace fixture exists' (-not [string]::IsNullOrWhiteSpace($foreignWorkspace))

    # -- 35.12 / 35.13 authorization and Workspace trust ------------------------------------------
    $unauthenticated = Invoke-Configuration $script:WorkspaceId 'omit'
    Add-Result 'unauthenticated rejected' 401 $unauthenticated.Status
    $unknownWorkspace = Invoke-Configuration 'ws_unknown_product_configuration'
    Add-Result 'unknown Workspace rejected' 403 $unknownWorkspace.Status
    $foreign = Invoke-Configuration $foreignWorkspace
    Add-Result 'non-member Workspace rejected' 403 $foreign.Status
    $missingWorkspaceHeader = Invoke-Api 'GET' '/products/configuration/types' $script:Token $null $null $null
    Assert-True 'missing Workspace context denied' ($missingWorkspaceHeader.Status -eq 400 -or $missingWorkspaceHeader.Status -eq 403)

    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Suspended' WHERE MembershipId=N'$membershipId'"
    $suspended = Invoke-Configuration
    Add-Result 'suspended membership rejected' 403 $suspended.Status
    Invoke-SqlNonQuery "UPDATE workspace.Memberships SET Status=N'Active' WHERE MembershipId=N'$membershipId'"

    Invoke-SqlNonQuery "DELETE FROM access.RoleCapabilities WHERE Capability=N'studio.read'"
    $withoutCapability = Invoke-Configuration
    Add-Result 'missing studio.read rejected' 403 $withoutCapability.Status
    Add-Result 'missing studio.read emits no ETag' '' ([string]$withoutCapability.ETag)
    Invoke-SqlNonQuery "INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES(N'$roleId',N'studio.read')"
    $restored = Invoke-Configuration
    Add-Result 'capability restored' 200 $restored.Status

    # -- 35.1 sparse empty state ------------------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    $rowsBefore = Get-ConfigurationRowSnapshot
    $evidenceBefore = Get-ReadEvidenceCount
    $empty = Invoke-Configuration
    Add-Result 'no persisted configuration succeeds' 200 $empty.Status
    Add-Result 'no persisted configuration revision' 0 ([long]$empty.Body.revision)
    Add-Result 'no persisted configuration ETag' '"0"' ([string]$empty.ETag)
    Add-Result 'no persisted configuration entry count' 9 (@($empty.Body.data.types).Count)
    Add-Result 'no persisted configuration canonical order' $canonicalOrder (Get-Codes $empty.Body)
    Add-Result 'no persisted configuration all ACTIVE' 'ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE' (Get-Statuses $empty.Body)
    Add-Result 'GET writes no configuration rows' $rowsBefore (Get-ConfigurationRowSnapshot)
    Add-Result 'GET records read evidence' ($evidenceBefore + 1) (Get-ReadEvidenceCount)
    Add-Result 'data carries only the types key' 'types' (($empty.Body.data.PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'row carries only code and status' 'code,status' (((@($empty.Body.data.types)[0]).PSObject.Properties.Name | Sort-Object) -join ',')
    Add-Result 'envelope carries only revision and data' 'data,revision' (($empty.Body.PSObject.Properties.Name | Sort-Object) -join ',')
    $repeat = Invoke-Configuration
    Add-Result 'unchanged document repeats the same ETag' ([string]$empty.ETag) ([string]$repeat.ETag)
    Add-Result 'unchanged document repeats the same body' ($empty.Body | ConvertTo-Json -Compress -Depth 10) ($repeat.Body | ConvertTo-Json -Compress -Depth 10)

    # -- 35.2 explicit INACTIVE override ----------------------------------------------------------
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    Set-Revision $script:WorkspaceId 1
    $inactive = Invoke-Configuration
    Add-Result 'INACTIVE override succeeds' 200 $inactive.Status
    Add-Result 'INACTIVE override entry count' 9 (@($inactive.Body.data.types).Count)
    Add-Result 'INACTIVE override canonical order preserved' $canonicalOrder (Get-Codes $inactive.Body)
    Add-Result 'INACTIVE override applied' 'INACTIVE' (Get-StatusOf $inactive.Body 'service')
    Add-Result 'INACTIVE override leaves others ACTIVE' 8 (@($inactive.Body.data.types | Where-Object { $_.status -ceq 'ACTIVE' }).Count)
    Add-Result 'INACTIVE override revision' 1 ([long]$inactive.Body.revision)
    Add-Result 'INACTIVE override ETag' '"1"' ([string]$inactive.ETag)

    # -- 35.3 explicit ACTIVE override is representationally identical to a missing override -------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'ACTIVE'
    $explicitActive = Invoke-Configuration
    Add-Result 'explicit ACTIVE override succeeds' 200 $explicitActive.Status
    Add-Result 'explicit ACTIVE override entry count' 9 (@($explicitActive.Body.data.types).Count)
    Add-Result 'explicit ACTIVE matches missing override representation' ($empty.Body | ConvertTo-Json -Compress -Depth 10) ($explicitActive.Body | ConvertTo-Json -Compress -Depth 10)
    Add-Result 'explicit ACTIVE emits no duplicate entry' 1 (@($explicitActive.Body.data.types | Where-Object { $_.code -ceq 'service' }).Count)

    # -- 35.4 multiple sparse overrides -----------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    Set-Override $script:WorkspaceId 'license' 'INACTIVE'
    Set-Override $script:WorkspaceId 'addon' 'ACTIVE'
    Set-Revision $script:WorkspaceId 3
    $merged = Invoke-Configuration
    Add-Result 'sparse merge succeeds' 200 $merged.Status
    Add-Result 'sparse merge canonical order preserved' $canonicalOrder (Get-Codes $merged.Body)
    Add-Result 'sparse merge statuses' 'ACTIVE,INACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,INACTIVE,ACTIVE' (Get-Statuses $merged.Body)
    Add-Result 'sparse merge revision' 3 ([long]$merged.Body.revision)
    Add-Result 'sparse merge ETag' '"3"' ([string]$merged.ETag)
    Add-Result 'revision and ETag describe one snapshot' ('"' + [string]$merged.Body.revision + '"') ([string]$merged.ETag)

    # -- 25 revision survives deletion of every override ------------------------------------------
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId'"
    $afterDelete = Invoke-Configuration
    Add-Result 'revision survives zero override rows' 3 ([long]$afterDelete.Body.revision)
    Add-Result 'zero override rows report all ACTIVE' 'ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE,ACTIVE' (Get-Statuses $afterDelete.Body)
    Add-Result 'revision does not reset to zero' '"3"' ([string]$afterDelete.ETag)

    # -- 35.5 cross-Workspace isolation -----------------------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Clear-Configuration $foreignWorkspace
    Set-Override $foreignWorkspace 'service' 'INACTIVE'
    Set-Revision $foreignWorkspace 7
    $isolated = Invoke-Configuration
    Add-Result 'foreign override does not leak status' 'ACTIVE' (Get-StatusOf $isolated.Body 'service')
    Add-Result 'foreign revision does not leak' 0 ([long]$isolated.Body.revision)
    Add-Result 'foreign revision does not leak into ETag' '"0"' ([string]$isolated.ETag)
    Clear-Configuration $foreignWorkspace

    # -- 18 ordinary Product mutations do not touch configuration revision ------------------------
    Set-Revision $script:WorkspaceId 4
    $revisionBeforeProduct = [long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'")
    $createRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:BaseUrl/products")
    $createRequest.Headers.TryAddWithoutValidation('X-Request-Id','req-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('X-Correlation-Id','corr-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('Idempotency-Key','idem-product-config-createproduct') | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('Authorization',"Bearer $script:Token") | Out-Null
    $createRequest.Headers.TryAddWithoutValidation('X-Workspace-Id',$script:WorkspaceId) | Out-Null
    $createBody = @{ sku='PC-VERIFY-0001'; name='Product Configuration Verification'; type='service'; status='ACTIVE'; category='Software License'; unit='item'; unitPrice=@{amount='10.00';currency='USD'}; taxRate='0'; taxMode='none'; billingCycle='one_time'; isSubscription=$false; isRenewable=$false; tags=@() }
    $createRequest.Content = [System.Net.Http.StringContent]::new(($createBody|ConvertTo-Json -Compress -Depth 6),[Text.Encoding]::UTF8,'application/json')
    $createClient = [System.Net.Http.HttpClient]::new()
    $createResponse = $createClient.SendAsync($createRequest).GetAwaiter().GetResult()
    $createStatus = [int]$createResponse.StatusCode
    $createRequest.Dispose();$createResponse.Dispose();$createClient.Dispose()
    Add-Result 'ordinary Product create still succeeds' 201 $createStatus
    Add-Result 'ordinary Product create is unaffected by an INACTIVE overlay' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.Products WHERE NormalizedSku=N'PC-VERIFY-0001'"))
    Add-Result 'ordinary Product mutation leaves configuration revision unchanged' $revisionBeforeProduct ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    $afterProduct = Invoke-Configuration
    Add-Result 'ordinary Product mutation leaves configuration ETag unchanged' '"4"' ([string]$afterProduct.ETag)

    # -- 35.6 / 35.7 / 35.8 corrupt state fails closed --------------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'my_custom_type' 'ACTIVE'
    $evidenceBeforeCorrupt = Get-ReadEvidenceCount
    $rowsBeforeCorrupt = Get-ConfigurationRowSnapshot
    $unknownCode = Invoke-Configuration
    Add-Result 'unknown ProductType code fails closed' 500 $unknownCode.Status
    Add-Result 'unknown ProductType code returns INTERNAL_ERROR' 'INTERNAL_ERROR' ([string]$unknownCode.Body.code)
    Add-Result 'unknown ProductType code returns no document' '' ([string]$unknownCode.Body.data)
    Add-Result 'unknown ProductType code emits no ETag' '' ([string]$unknownCode.ETag)
    Add-Result 'corrupt read writes no success evidence' $evidenceBeforeCorrupt (Get-ReadEvidenceCount)
    Add-Result 'corrupt read repairs nothing' $rowsBeforeCorrupt (Get-ConfigurationRowSnapshot)
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'service' 'DISABLED'
    $invalidStatus = Invoke-Configuration
    Add-Result 'invalid status fails closed' 500 $invalidStatus.Status
    Add-Result 'invalid status never falls back to ACTIVE' '' ([string]$invalidStatus.Body.data)
    Add-Result 'invalid status row is not rewritten' 'DISABLED' ([string](Get-Scalar "SELECT Status FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
    Clear-Configuration $script:WorkspaceId

    Set-Override $script:WorkspaceId 'Service' 'INACTIVE'
    $caseVariant = Invoke-Configuration
    Add-Result 'case-variant ProductType code fails closed' 500 $caseVariant.Status
    Add-Result 'case-variant code is not normalised' 'Service' ([string](Get-Scalar "SELECT ProductTypeCode FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Add-Result 'case-variant code count' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'Service' COLLATE Latin1_General_100_BIN2"))
    Clear-Configuration $script:WorkspaceId

    Set-Revision $script:WorkspaceId 2
    Invoke-SqlNonQuery "ALTER TABLE products.ProductConfigurationDocuments NOCHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision; UPDATE products.ProductConfigurationDocuments SET Revision=-4 WHERE WorkspaceId=N'$script:WorkspaceId';"
    $negativeRevision = Invoke-Configuration
    Add-Result 'negative revision fails closed' 500 $negativeRevision.Status
    Add-Result 'negative revision emits no ETag' '' ([string]$negativeRevision.ETag)
    Add-Result 'negative revision is not repaired' -4 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'; ALTER TABLE products.ProductConfigurationDocuments WITH CHECK CHECK CONSTRAINT CK_ProductConfigurationDocuments_Revision;"

    # -- monotonic revision rollback: served 5, durable anchor later becomes 3 ---------------------
    Clear-Configuration $script:WorkspaceId
    Set-Revision $script:WorkspaceId 5
    $served5 = Invoke-Configuration
    Add-Result 'rollback fixture first read succeeds' 200 $served5.Status
    Add-Result 'rollback fixture first served revision' 5 ([long]$served5.Body.revision)
    Add-Result 'rollback fixture first ETag' '"5"' ([string]$served5.ETag)
    Add-Result 'successful read establishes trusted revision' 5 (Get-TrustedRevision $script:WorkspaceId)
    # Corrupt only the current anchor. Trusted history is untouched, exactly as a rollback would look.
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=3 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $evidenceBeforeRollback = Get-ReadEvidenceCount
    $rolledBack = Invoke-Configuration
    Add-Result 'rollback below trusted revision fails closed' 500 $rolledBack.Status
    Add-Result 'rollback returns INTERNAL_ERROR' 'INTERNAL_ERROR' ([string]$rolledBack.Body.code)
    Add-Result 'rollback returns no document' '' ([string]$rolledBack.Body.data)
    Add-Result 'rollback returns no revision' '' ([string]$rolledBack.Body.revision)
    Add-Result 'rollback emits no ETag' '' ([string]$rolledBack.ETag)
    Add-Result 'rollback writes no success evidence' $evidenceBeforeRollback (Get-ReadEvidenceCount)
    Add-Result 'rollback performs no repair' 3 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Add-Result 'rollback leaves trusted revision intact' 5 (Get-TrustedRevision $script:WorkspaceId)

    # -- rollback detection survives a process restart --------------------------------------------
    Stop-ApiHost
    Start-ApiHost
    $afterRestart = Invoke-Configuration
    Add-Result 'rollback still fails closed after host restart' 500 $afterRestart.Status
    Add-Result 'rollback after restart emits no ETag' '' ([string]$afterRestart.ETag)
    Add-Result 'trusted revision survives host restart' 5 (Get-TrustedRevision $script:WorkspaceId)

    # -- a revision above trusted history is legitimate, not corrupt ------------------------------
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=7 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $ahead = Invoke-Configuration
    Add-Result 'revision above trusted history is served' 200 $ahead.Status
    Add-Result 'revision above trusted history reports its revision' 7 ([long]$ahead.Body.revision)
    Add-Result 'revision above trusted history ETag' '"7"' ([string]$ahead.ETag)
    Add-Result 'trusted history advances to the served revision' 7 (Get-TrustedRevision $script:WorkspaceId)
    $repeatAhead = Invoke-Configuration
    Add-Result 'trusted history is not lowered by a repeat read' 7 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'repeat read at equal revision still succeeds' 200 $repeatAhead.Status

    # -- trusted history is Workspace-scoped ------------------------------------------------------
    Clear-Configuration $foreignWorkspace
    Set-Revision $foreignWorkspace 3
    Add-Result 'Workspace A trusted revision precondition' 7 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'Workspace B has no trusted history' 0 (Get-TrustedRevision $foreignWorkspace)
    $foreignMember = Invoke-Configuration $foreignWorkspace
    Add-Result 'Workspace B lower revision is not judged by Workspace A history' 403 $foreignMember.Status
    Add-Result 'Workspace A trusted history unchanged by Workspace B' 7 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'Workspace B trusted history not created by a denied read' 0 (Get-TrustedRevision $foreignWorkspace)
    # The decisive non-leakage direction: a foreign Workspace carrying a HIGHER trusted watermark must
    # not make this Workspace's lower - but locally legitimate - revision look like a rollback. A
    # global or shared watermark would fail this read; a Workspace-keyed one serves it.
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTrustedRevisions(WorkspaceId,GreatestTrustedRevision) VALUES(N'$foreignWorkspace',9)"
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=8 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $notPoisoned = Invoke-Configuration
    Add-Result 'foreign higher trusted watermark does not poison this Workspace' 200 $notPoisoned.Status
    Add-Result 'foreign higher watermark leaves this revision served' 8 ([long]$notPoisoned.Body.revision)
    Add-Result 'foreign higher watermark leaves this ETag intact' '"8"' ([string]$notPoisoned.ETag)
    Add-Result 'foreign trusted watermark unchanged by this read' 9 (Get-TrustedRevision $foreignWorkspace)
    Add-Result 'this Workspace trusted watermark advanced independently' 8 (Get-TrustedRevision $script:WorkspaceId)
    Invoke-SqlNonQuery "DELETE FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$foreignWorkspace'"
    Clear-Configuration $foreignWorkspace

    # -- success evidence must be atomic: no 200 when trust evidence cannot be committed -----------
    Clear-Configuration $script:WorkspaceId
    Set-Revision $script:WorkspaceId 2
    Invoke-SqlNonQuery "CREATE TRIGGER products.TR_VerifyTrustedRevisionFailure ON products.ProductConfigurationTrustedRevisions INSTEAD OF INSERT AS THROW 51000, 'forced trusted revision failure', 1"
    try {
        $evidenceBeforeTrustFailure = Get-ReadEvidenceCount
        $trustFailure = Invoke-Configuration
        Add-Result 'trusted revision persistence failure returns no success' 500 $trustFailure.Status
        Add-Result 'trusted revision persistence failure emits no ETag' '' ([string]$trustFailure.ETag)
        Add-Result 'trusted revision persistence failure leaves no read evidence' $evidenceBeforeTrustFailure (Get-ReadEvidenceCount)
        Add-Result 'trusted revision persistence failure records no trust' 0 (Get-TrustedRevision $script:WorkspaceId)
    }
    finally { Invoke-SqlNonQuery 'DROP TRIGGER products.TR_VerifyTrustedRevisionFailure' }
    Clear-Configuration $script:WorkspaceId

    # =============================================================================================
    # Concurrent monotonic trust update. Sequential tests cannot prove any of this.
    # =============================================================================================

    # The test statement must be the statement the runtime runs, so assert the production guards.
    $persistenceSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Infrastructure/Persistence/EfProductsPersistence.cs')
    Assert-True 'trust upsert is a single MERGE holding a range lock' ($persistenceSource -match 'MERGE \[products\]\.\[ProductConfigurationTrustedRevisions\] WITH \(HOLDLOCK\)')
    Assert-True 'trust update is guarded against writing downward' ($persistenceSource -match 'WHEN MATCHED AND target\.\[GreatestTrustedRevision\] < source\.\[GreatestTrustedRevision\]')
    Assert-True 'trust insert only creates a positive first watermark' ($persistenceSource -match 'WHEN NOT MATCHED AND source\.\[GreatestTrustedRevision\] > 0')
    # Every assignment to the watermark must sit behind the "only if strictly greater" guard: the count
    # of assignments and the count of guards must match, so no unguarded write path can exist.
    $trustAssignments = [regex]::Matches($persistenceSource, 'UPDATE SET target\.\[GreatestTrustedRevision\]').Count
    $trustGuards = [regex]::Matches($persistenceSource, 'WHEN MATCHED AND target\.\[GreatestTrustedRevision\] < source\.\[GreatestTrustedRevision\]').Count
    Add-Result 'every trust assignment is guarded' "$trustAssignments/$trustAssignments" "$trustAssignments/$trustGuards"
    Assert-True 'exactly one trust assignment path exists' ($trustAssignments -eq 1)
    Assert-True 'trust correctness uses no process-local state' ($persistenceSource -notmatch 'ConcurrentDictionary|SemaphoreSlim|lock \(|static readonly object|MemoryCache')

    # -- Race A: overlapping writes on an existing row, lower first -------------------------------
    Clear-Configuration $script:WorkspaceId
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTrustedRevisions(WorkspaceId,GreatestTrustedRevision) VALUES(N'$script:WorkspaceId',3)"
    $blockedLowFirst = Invoke-OverlappingTrustMerge $script:WorkspaceId 4 5
    Assert-True 'overlapping trust writes actually contend (lower first)' $blockedLowFirst
    Add-Result 'existing-row concurrent max keeps the highest revision' 5 (Get-TrustedRevision $script:WorkspaceId)

    # -- Race B: reverse completion order, the lost-update case -----------------------------------
    Clear-Configuration $script:WorkspaceId
    Invoke-SqlNonQuery "INSERT INTO products.ProductConfigurationTrustedRevisions(WorkspaceId,GreatestTrustedRevision) VALUES(N'$script:WorkspaceId',3)"
    $blockedHighFirst = Invoke-OverlappingTrustMerge $script:WorkspaceId 5 4
    Assert-True 'overlapping trust writes actually contend (higher first)' $blockedHighFirst
    Add-Result 'reverse completion order does not lower the watermark' 5 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'reverse completion leaves exactly one trust row' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$script:WorkspaceId'"))

    # -- Race C: first-row establishment through the real endpoint --------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Revision $script:WorkspaceId 4
    Add-Result 'first-row race starts with no trust row' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$script:WorkspaceId'"))
    $firstRowStatuses = Invoke-ConcurrentConfiguration 8
    Add-Result 'first-row race requests all succeed' 8 (@($firstRowStatuses | Where-Object { $_ -eq 200 }).Count)
    Add-Result 'first-row race leaks no unique-key failure' 0 (@($firstRowStatuses | Where-Object { $_ -ne 200 }).Count)
    Add-Result 'first-row race creates exactly one trust row' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Add-Result 'first-row race establishes the served revision' 4 (Get-TrustedRevision $script:WorkspaceId)

    # -- Race D: same revision concurrently -------------------------------------------------------
    Set-Revision $script:WorkspaceId 5
    $sameStatuses = Invoke-ConcurrentConfiguration 8
    Add-Result 'same-revision concurrency all succeed' 8 (@($sameStatuses | Where-Object { $_ -eq 200 }).Count)
    Add-Result 'same-revision concurrency settles at that revision' 5 (Get-TrustedRevision $script:WorkspaceId)
    $sameStatusesAgain = Invoke-ConcurrentConfiguration 8
    Add-Result 'repeated same-revision concurrency still all succeed' 8 (@($sameStatusesAgain | Where-Object { $_ -eq 200 }).Count)
    Add-Result 'repeated same-revision concurrency does not churn the watermark' 5 (Get-TrustedRevision $script:WorkspaceId)

    # -- Race E: rollback detection stays deterministic beside healthy concurrent reads ------------
    Add-Result 'rollback-under-load trusted precondition' 5 (Get-TrustedRevision $script:WorkspaceId)
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=4 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $corruptUnderLoad = Invoke-ConcurrentConfiguration 8
    Add-Result 'every concurrent read of a rolled-back revision fails closed' 8 (@($corruptUnderLoad | Where-Object { $_ -eq 500 }).Count)
    Add-Result 'no concurrent read of a rolled-back revision succeeds' 0 (@($corruptUnderLoad | Where-Object { $_ -eq 200 }).Count)
    Add-Result 'rollback under load leaves the watermark intact' 5 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'rollback under load repairs nothing' 4 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=6 WHERE WorkspaceId=N'$script:WorkspaceId'"
    $healthyAfterCorrupt = Invoke-ConcurrentConfiguration 8
    Add-Result 'healthy concurrent reads resume after the anchor is ahead again' 8 (@($healthyAfterCorrupt | Where-Object { $_ -eq 200 }).Count)
    Add-Result 'watermark advances to the higher served revision' 6 (Get-TrustedRevision $script:WorkspaceId)

    # -- Race F: two Workspaces concurrently, no cross-tenant interference ------------------------
    Clear-Configuration $script:WorkspaceId
    Clear-Configuration $foreignWorkspace
    $blockedWorkspaceA = Invoke-OverlappingTrustMerge $script:WorkspaceId 7 4
    $blockedWorkspaceB = Invoke-OverlappingTrustMerge $foreignWorkspace 3 5
    Assert-True 'Workspace A overlapping writes contend' $blockedWorkspaceA
    Assert-True 'Workspace B overlapping writes contend' $blockedWorkspaceB
    Add-Result 'Workspace A settles at its own maximum' 7 (Get-TrustedRevision $script:WorkspaceId)
    Add-Result 'Workspace B settles at its own maximum' 5 (Get-TrustedRevision $foreignWorkspace)
    Add-Result 'concurrent Workspaces keep separate trust rows' 2 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId IN (N'$script:WorkspaceId',N'$foreignWorkspace')"))
    Clear-Configuration $foreignWorkspace

    # -- Multi-instance: two ApiHost processes, one database --------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Revision $script:WorkspaceId 9
    Start-SecondApiHost
    try {
        $twoHostStatuses = Invoke-ConcurrentConfiguration 12 @($script:BaseUrl, $script:SecondBaseUrl)
        Add-Result 'two-host concurrent reads all succeed' 12 (@($twoHostStatuses | Where-Object { $_ -eq 200 }).Count)
        Add-Result 'two-host concurrency establishes the served revision' 9 (Get-TrustedRevision $script:WorkspaceId)
        Add-Result 'two-host concurrency creates exactly one trust row' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTrustedRevisions WHERE WorkspaceId=N'$script:WorkspaceId'"))
        # A watermark held in process memory would be invisible to the other host; a rollback seen by
        # both hosts proves the watermark is read from the database on every request.
        Invoke-SqlNonQuery "UPDATE products.ProductConfigurationDocuments SET Revision=8 WHERE WorkspaceId=N'$script:WorkspaceId'"
        $twoHostRollback = Invoke-ConcurrentConfiguration 12 @($script:BaseUrl, $script:SecondBaseUrl)
        Add-Result 'both hosts reject a rolled-back revision' 12 (@($twoHostRollback | Where-Object { $_ -eq 500 }).Count)
        Add-Result 'two-host rollback leaves the watermark intact' 9 (Get-TrustedRevision $script:WorkspaceId)
    }
    finally { Stop-SecondApiHost }
    Clear-Configuration $script:WorkspaceId

    # -- Failure atomicity while concurrent readers are in flight ---------------------------------
    Set-Revision $script:WorkspaceId 2
    Invoke-SqlNonQuery "CREATE TRIGGER products.TR_VerifyConcurrentTrustFailure ON products.ProductConfigurationTrustedRevisions INSTEAD OF INSERT AS THROW 51000, 'forced concurrent trusted revision failure', 1"
    try {
        $evidenceBeforeConcurrentFailure = Get-ReadEvidenceCount
        $concurrentFailure = Invoke-ConcurrentConfiguration 6
        Add-Result 'concurrent trust failure yields no successful response' 0 (@($concurrentFailure | Where-Object { $_ -eq 200 }).Count)
        Add-Result 'concurrent trust failure returns server error' 6 (@($concurrentFailure | Where-Object { $_ -eq 500 }).Count)
        Add-Result 'concurrent trust failure writes no success evidence' $evidenceBeforeConcurrentFailure (Get-ReadEvidenceCount)
        Add-Result 'concurrent trust failure records no watermark' 0 (Get-TrustedRevision $script:WorkspaceId)
        Add-Result 'concurrent trust failure repairs no configuration' 2 ([long](Get-Scalar "SELECT Revision FROM products.ProductConfigurationDocuments WHERE WorkspaceId=N'$script:WorkspaceId'"))
    }
    finally { Invoke-SqlNonQuery 'DROP TRIGGER products.TR_VerifyConcurrentTrustFailure' }
    Clear-Configuration $script:WorkspaceId

    # -- 35.9 duplicate override is structurally impossible ---------------------------------------
    Clear-Configuration $script:WorkspaceId
    Set-Override $script:WorkspaceId 'service' 'INACTIVE'
    $duplicateRejected = $false
    try { Set-Override $script:WorkspaceId 'service' 'ACTIVE' }
    catch { $duplicateRejected = $true }
    Assert-True 'duplicate override rejected by the database' $duplicateRejected
    Add-Result 'duplicate override left exactly one row' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides WHERE WorkspaceId=N'$script:WorkspaceId' AND ProductTypeCode=N'service'"))
    Clear-Configuration $script:WorkspaceId

    # -- structural / scope assertions -------------------------------------------------------------
    Add-Result 'revision check constraint present' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_ProductConfigurationDocuments_Revision'"))
    Add-Result 'override primary key is Workspace plus code' 'ProductTypeCode,WorkspaceId' ((@(Invoke-Sql "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'products.ProductConfigurationTypeOverrides') AND i.is_primary_key=1") | ForEach-Object { [string]$_.name } | Sort-Object) -join ',')
    Add-Result 'configuration tables have no foreign key' 0 ([int](Get-Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID(N'products.ProductConfigurationDocuments'),OBJECT_ID(N'products.ProductConfigurationTypeOverrides'),OBJECT_ID(N'products.ProductConfigurationTrustedRevisions'))"))
    Add-Result 'trusted revision check constraint present' 1 ([int](Get-Scalar "SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_ProductConfigurationTrustedRevisions_Revision'"))
    Add-Result 'trusted revision table is Workspace-keyed' 'WorkspaceId' ((@(Invoke-Sql "SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=i.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'products.ProductConfigurationTrustedRevisions') AND i.is_primary_key=1") | ForEach-Object { [string]$_.name } | Sort-Object) -join ',')
    Add-Result 'migration seeds no override rows' 0 ([int](Get-Scalar 'SELECT COUNT(*) FROM products.ProductConfigurationTypeOverrides'))

    $handlerSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/ListProductConfigurationTypes/Handler.cs')
    $catalogSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/Common/ProductConfigurationCatalog.cs')
    $persistenceSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Infrastructure/Persistence/EfProductsPersistence.cs')
    $endpointSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Contracts/ProductsEndpoints.cs')
    $validationSource = Get-Content -Raw (Join-Path $repositoryRoot 'src/UnicoreCRM.Sales/Products/Application/Common/ProductValidation.cs')
    Assert-True 'no foreign DbContext in the configuration read' (($handlerSource+$catalogSource+$persistenceSource) -notmatch '\b(Workspace|AccessControl|IdentityAuth)DbContext\b')
    Assert-True 'no cross-owner SQL in Products persistence' ($persistenceSource -notmatch '\[(workspace|iam|access)\]|\b(workspace|iam|access)\.')
    # Prose in the doc comments names the header, so assert on what the code can actually touch:
    # neither type can reach transport state at all.
    Assert-True 'configuration read cannot reach transport state' (($handlerSource+$catalogSource) -notmatch 'HttpContext|Request\.Headers|Headers\[')
    Assert-True 'trusted Workspace comes from the authorization context' ($handlerSource -match 'context\.WorkspaceId')
    Assert-True 'configuration read writes no idempotency or outbox state' ($handlerSource -notmatch 'Idempotency|Outbox|AddProduct')
    Assert-True 'ProductValidation is unchanged by this operation' ($validationSource -notmatch 'ProductConfiguration')
    Add-Result 'exactly one configuration read route' 1 ([regex]::Matches($endpointSource,'"/products/configuration/types"').Count)
    # createProductConfigurationType and deleteProductConfigurationType remain BLOCKED. The admitted
    # updateProductConfigurationType PATCH is the only configuration mutation route that may exist.
    Add-Result 'no configuration create or delete route' 0 ([regex]::Matches($endpointSource,'MapPost\(endpoints, "/products/configuration|MapDelete').Count)
    Add-Result 'exactly one configuration mutation route' 1 ([regex]::Matches($endpointSource,'MapPatch\(endpoints, "/products/configuration/types/\{typeId\}"').Count)
    # One ETag write for the read, one for the admitted mutation - both the same strong encoding.
    Add-Result 'strong quoted ETag encoding in the endpoint' 2 ([regex]::Matches($endpointSource,'Headers\.ETag').Count)
}
finally {
    Stop-SecondApiHost
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        Stop-Process -Id $script:HostProcess.Id -Force
        $script:HostProcess.WaitForExit()
    }
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
if ($script:Failed -ne 0) { throw "listProductConfigurationTypes verification failed: $script:Failed check(s)." }
