<#
.SYNOPSIS
    Verifies createAccessRole UTF-16 storage boundaries and the exact legacy normalization upgrade.
#>
[CmdletBinding()]
param(
    [string] $FreshDatabaseName = 'UnicoreVerify_CreateRoleUnicode_Fresh',
    [string] $HistoricalDatabaseName = 'UnicoreVerify_CreateRoleUnicode_Historical',
    [string] $CollisionDatabaseName = 'UnicoreVerify_CreateRoleUnicode_Collision',
    [string] $SqlServer = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5491,
    [switch] $KeepDatabases
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$script:Passed = 0
$script:Failed = 0
$script:Results = [System.Collections.Generic.List[string]]::new()
$script:HostProcess = $null
$script:Counter = 0
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $repositoryRoot 'src/UnicoreCRM.ApiHost')).Path
$platformProject = Join-Path $repositoryRoot 'src/UnicoreCRM.Platform'
$preCorrectionMigration = '20260901003814_CreateAccessRole'
$email = 'admin@unicorecrm.local'
$password = 'Create-Access-Role-Unicode-Verify!2026'
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('unicore-create-role-unicode-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $logRoot

foreach ($databaseName in @($FreshDatabaseName, $HistoricalDatabaseName, $CollisionDatabaseName)) {
    if ($databaseName -notmatch '^[A-Za-z0-9_]+$') { throw "Unsafe verification database name '$databaseName'." }
}

function Add-Result([string] $Name, [object] $Expected, [object] $Actual) {
    $expectedText = [string] $Expected
    $actualText = [string] $Actual
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

function Get-ConnectionString([string] $DatabaseName) {
    return "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
}

function New-Connection([string] $DatabaseName) {
    $connection = [System.Data.SqlClient.SqlConnection]::new((Get-ConnectionString $DatabaseName))
    $connection.Open()
    return $connection
}

function Invoke-Sql(
    [string] $DatabaseName,
    [string] $Query,
    [hashtable] $Parameters = @{},
    [switch] $Scalar,
    [switch] $NonQuery
) {
    $connection = New-Connection $DatabaseName
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        foreach ($entry in $Parameters.GetEnumerator()) {
            $parameter = $command.Parameters.AddWithValue($entry.Key, $(if ($null -eq $entry.Value) { [DBNull]::Value } else { $entry.Value }))
            if ($entry.Value -is [string]) { $parameter.SqlDbType = [System.Data.SqlDbType]::NVarChar }
        }
        if ($NonQuery) { return $command.ExecuteNonQuery() }
        if ($Scalar) { return $command.ExecuteScalar() }
        $reader = $command.ExecuteReader()
        $rows = [System.Collections.Generic.List[object]]::new()
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($index = 0; $index -lt $reader.FieldCount; $index++) {
                $row[$reader.GetName($index)] = $reader.GetValue($index)
            }
            $rows.Add([pscustomobject] $row)
        }
        $reader.Close()
        return $rows
    }
    finally { $connection.Dispose() }
}

function New-Database([string] $DatabaseName) {
    $null = Invoke-Sql 'master' "IF DB_ID(N'$DatabaseName') IS NOT NULL THROW 50001, 'Verification database already exists.', 1; CREATE DATABASE [$DatabaseName];" -NonQuery
}

function Remove-Database([string] $DatabaseName) {
    $null = Invoke-Sql 'master' "IF DB_ID(N'$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END" -NonQuery
}

function Update-AccessControlDatabase([string] $DatabaseName, [string] $Target = $null) {
    $arguments = @('ef', 'database', 'update')
    if (-not [string]::IsNullOrWhiteSpace($Target)) { $arguments += $Target }
    $arguments += @('--project', $platformProject, '--context', 'AccessControlDbContext', '--no-build', '--connection', (Get-ConnectionString $DatabaseName))
    $output = & dotnet @arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "AccessControl migration failed for '$DatabaseName': $output" }
}

function Set-HostEnvironment([string] $DatabaseName, [int] $HostPort, [bool] $SeedEnabled) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$HostPort"
    $env:ConnectionStrings__UnicoreCRM = Get-ConnectionString $DatabaseName
    $env:Development__ApplyMigrations = 'true'
    $env:UNICORE_DEV_SEED_ENABLED = $SeedEnabled.ToString().ToLowerInvariant()
    $env:UNICORE_DEV_SEED_EMAIL = $email
    $env:UNICORE_DEV_SEED_PASSWORD = $password
    $env:AccessControl__DevelopmentBootstrap__Capabilities__0 = 'access.configure'
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
}

function Start-Host([string] $DatabaseName, [int] $HostPort, [bool] $SeedEnabled = $true) {
    Set-HostEnvironment $DatabaseName $HostPort $SeedEnabled
    $stdout = Join-Path $logRoot "$DatabaseName.out.log"
    $stderr = Join-Path $logRoot "$DatabaseName.err.log"
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    for ($attempt = 0; $attempt -lt 240; $attempt++) {
        if ($process.HasExited) { throw "ApiHost exited during startup for '$DatabaseName': $(Get-Content -Raw $stderr) $(Get-Content -Raw $stdout)" }
        try {
            $probe = Invoke-Api 'GET' '/auth/session' $null $null $null $null $HostPort
            if ($probe.Status -eq 401) { return [pscustomobject]@{ Process = $process; StdOut = $stdout; StdErr = $stderr } }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    throw "ApiHost did not become ready for '$DatabaseName'. Logs: $stdout $stderr"
}

function Stop-Host([object] $HostState) {
    if ($null -ne $HostState -and $null -ne $HostState.Process -and -not $HostState.Process.HasExited) {
        Stop-Process -Id $HostState.Process.Id -Force
        $HostState.Process.WaitForExit()
    }
}

function Invoke-Api(
    [string] $Method,
    [string] $Path,
    [string] $Body,
    [string] $Token,
    [string] $WorkspaceId,
    [string] $IdempotencyKey,
    [int] $HostPort
) {
    $script:Counter++
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "http://127.0.0.1:$HostPort$Path")
    $null = $request.Headers.TryAddWithoutValidation('X-Request-Id', 'req-unicode-' + $script:Counter.ToString('d6'))
    $null = $request.Headers.TryAddWithoutValidation('X-Correlation-Id', 'corr-unicode-' + $script:Counter.ToString('d6'))
    if (-not [string]::IsNullOrWhiteSpace($Token)) { $null = $request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token") }
    if (-not [string]::IsNullOrWhiteSpace($WorkspaceId)) { $null = $request.Headers.TryAddWithoutValidation('X-Workspace-Id', $WorkspaceId) }
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) { $null = $request.Headers.TryAddWithoutValidation('Idempotency-Key', $IdempotencyKey) }
    # Windows PowerShell binds $null to a [string] parameter as the empty string, so a
    # body-less GET would still be given a content body and rejected with "Cannot send a
    # content-body with this verb-type" before it ever reached the host.
    if (-not [string]::IsNullOrEmpty($Body)) { $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
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
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $payload; Raw = $raw }
    }
    finally { $request.Dispose(); $client.Dispose() }
}

function New-Body(
    [string] $Name,
    [string] $Description = $null,
    [string] $SourceTemplateId = $null,
    [object[]] $DataScopes = @(),
    [object[]] $FieldSecurity = @()
) {
    $body = [ordered]@{ name = $Name; capabilities = @('tasks.read'); dataScopes = $DataScopes; fieldSecurity = $FieldSecurity }
    if ($null -ne $Description) { $body.description = $Description }
    if ($null -ne $SourceTemplateId) { $body.sourceTemplateId = $SourceTemplateId }
    return $body | ConvertTo-Json -Compress -Depth 12
}

function Invoke-Create([int] $HostPort, [string] $Token, [string] $WorkspaceId, [string] $Body, [string] $Key) {
    return Invoke-Api 'POST' '/access/roles' $Body $Token $WorkspaceId $Key $HostPort
}

function Sign-In([int] $HostPort) {
    $response = Invoke-Api 'POST' '/auth/sessions' (@{ email = $email; password = $password } | ConvertTo-Json -Compress) $null $null 'idem-unicode-signin' $HostPort
    Add-Result "authentication fixture on port $HostPort" 200 $response.Status
    return [string]$response.Body.accessToken
}

function Assert-ValidationFailure([string] $Name, [object] $Response) {
    Add-Result "$Name status" 422 $Response.Status
    Add-Result "$Name code" 'VALIDATION_FAILED' $Response.Body.code
}

function Seed-HistoricalRole(
    [string] $DatabaseName,
    [string] $RoleId,
    [string] $WorkspaceId,
    [string] $Name,
    [string] $Description = $null
) {
    $query =
        @"
        INSERT INTO access.Roles
            (RoleId, WorkspaceId, Name, Description, SourceTemplateId, IsActive, Version, CreatedAt, UpdatedAt, NormalizedName)
        VALUES
            (@RoleId, @WorkspaceId, @Name, @Description, NULL, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), UPPER(@Name));
"@
    $null = Invoke-Sql $DatabaseName $query @{ '@RoleId' = $RoleId; '@WorkspaceId' = $WorkspaceId; '@Name' = $Name; '@Description' = $Description } -NonQuery
}

$freshHost = $null
$historicalHost = $null
$collisionProcess = $null
try {
    # Fresh database: apply the complete chain through the real host and exercise SQL persistence.
    New-Database $FreshDatabaseName
    $freshHost = Start-Host $FreshDatabaseName $Port $true
    $freshToken = Sign-In $Port
    $freshWorkspaceId = [string](Invoke-Sql $FreshDatabaseName "SELECT WorkspaceId FROM workspace.Workspaces WHERE [Key]=N'unicore-demo'" -Scalar)
    Assert-True 'fresh trusted Workspace fixture' (-not [string]::IsNullOrWhiteSpace($freshWorkspaceId))

    $columnWidthSql =
        @"
        SELECT t.[name] AS TableName, c.[name] AS ColumnName, c.[max_length] AS MaxBytes
        FROM sys.columns c
        JOIN sys.tables t ON t.[object_id]=c.[object_id]
        JOIN sys.schemas s ON s.[schema_id]=t.[schema_id]
        WHERE s.[name]=N'access' AND
          ((t.[name]=N'Roles' AND c.[name] IN (N'Name',N'NormalizedName',N'Description',N'SourceTemplateId')) OR
           (t.[name]=N'RoleDataScopes' AND c.[name]=N'ResourceKey') OR
           (t.[name]=N'RoleFieldSecurity' AND c.[name] IN (N'ResourceKey',N'FieldKey')))
        ORDER BY t.[name], c.[name];
"@
    $columnWidths = @(Invoke-Sql $FreshDatabaseName $columnWidthSql)
    $widthMap = @{}
    foreach ($row in $columnWidths) { $widthMap["$($row.TableName).$($row.ColumnName)"] = [int]$row.MaxBytes }
    Add-Result 'Role.Name SQL bytes' 640 $widthMap['Roles.Name']
    Add-Result 'Role.NormalizedName SQL bytes' 640 $widthMap['Roles.NormalizedName']
    Add-Result 'Role.Description SQL bytes' 2000 $widthMap['Roles.Description']
    Add-Result 'Role.SourceTemplateId SQL bytes' 640 $widthMap['Roles.SourceTemplateId']
    Add-Result 'RoleDataScope.ResourceKey SQL bytes' 640 $widthMap['RoleDataScopes.ResourceKey']
    Add-Result 'RoleFieldSecurity.ResourceKey SQL bytes' 640 $widthMap['RoleFieldSecurity.ResourceKey']
    Add-Result 'RoleFieldSecurity.FieldKey SQL bytes' 640 $widthMap['RoleFieldSecurity.FieldKey']

    $supplementary = [char]::ConvertFromUtf32(0x1F600)
    $name160 = $supplementary * 160
    $name161 = $supplementary * 161
    $nameSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body $name160) 'idem-unicode-name-160'
    Add-Result '160 supplementary name status' 200 $nameSuccess.Status
    if ($nameSuccess.Status -eq 200) {
        $nameRow = @(Invoke-Sql $FreshDatabaseName 'SELECT Name,NormalizedName FROM access.Roles WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$nameSuccess.Body.aggregateId })[0]
        Add-Result '160 supplementary persisted name exact' $name160 ([string]$nameRow.Name)
        Add-Result '160 supplementary normalized exact' $name160.ToUpperInvariant() ([string]$nameRow.NormalizedName)
    }
    Assert-ValidationFailure '161 supplementary name' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body $name161) 'idem-unicode-name-161')

    $description500 = $supplementary * 500
    $description501 = $supplementary * 501
    $descriptionSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Description Boundary' $description500) 'idem-unicode-description-500'
    Add-Result '500 supplementary description status' 200 $descriptionSuccess.Status
    if ($descriptionSuccess.Status -eq 200) {
        $persisted = [string](Invoke-Sql $FreshDatabaseName 'SELECT Description FROM access.Roles WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$descriptionSuccess.Body.aggregateId } -Scalar)
        Add-Result '500 supplementary description persisted exact' $description500 $persisted
    }
    Assert-ValidationFailure '501 supplementary description' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Description Overflow' $description501) 'idem-unicode-description-501')

    $key160 = $supplementary * 160
    $key161 = $supplementary * 161
    $templateSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Template Boundary' $null $key160) 'idem-unicode-template-160'
    Add-Result '160 supplementary sourceTemplateId status' 200 $templateSuccess.Status
    if ($templateSuccess.Status -eq 200) {
        Add-Result 'sourceTemplateId persisted exact' $key160 ([string](Invoke-Sql $FreshDatabaseName 'SELECT SourceTemplateId FROM access.Roles WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$templateSuccess.Body.aggregateId } -Scalar))
    }
    Assert-ValidationFailure '161 supplementary sourceTemplateId' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Template Overflow' $null $key161) 'idem-unicode-template-161')

    $scopeSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Scope Boundary' $null $null @(@{ resourceKey=$key160; scope='CUSTOM'; allowedOwnerIds=@() })) 'idem-unicode-scope-160'
    Add-Result '160 supplementary dataScope resource status' 200 $scopeSuccess.Status
    if ($scopeSuccess.Status -eq 200) {
        Add-Result 'dataScope resource persisted exact' $key160.ToLowerInvariant() ([string](Invoke-Sql $FreshDatabaseName 'SELECT ResourceKey FROM access.RoleDataScopes WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$scopeSuccess.Body.aggregateId } -Scalar))
    }
    Assert-ValidationFailure '161 supplementary dataScope resource' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Scope Overflow' $null $null @(@{ resourceKey=$key161; scope='CUSTOM'; allowedOwnerIds=@() })) 'idem-unicode-scope-161')

    $fieldResourceSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Field Resource Boundary' $null $null @() @(@{ resourceKey=$key160; fieldKey='email'; access='READ_ONLY' })) 'idem-unicode-field-resource-160'
    Add-Result '160 supplementary fieldSecurity resource status' 200 $fieldResourceSuccess.Status
    if ($fieldResourceSuccess.Status -eq 200) {
        Add-Result 'fieldSecurity resource persisted exact' $key160.ToLowerInvariant() ([string](Invoke-Sql $FreshDatabaseName 'SELECT ResourceKey FROM access.RoleFieldSecurity WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$fieldResourceSuccess.Body.aggregateId } -Scalar))
    }
    Assert-ValidationFailure '161 supplementary fieldSecurity resource' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Field Resource Overflow' $null $null @() @(@{ resourceKey=$key161; fieldKey='email'; access='READ_ONLY' })) 'idem-unicode-field-resource-161')

    $fieldSuccess = Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Field Boundary' $null $null @() @(@{ resourceKey='contacts'; fieldKey=$key160; access='READ_ONLY' })) 'idem-unicode-field-160'
    Add-Result '160 supplementary fieldKey status' 200 $fieldSuccess.Status
    if ($fieldSuccess.Status -eq 200) {
        Add-Result 'fieldKey persisted exact' $key160.ToLowerInvariant() ([string](Invoke-Sql $FreshDatabaseName 'SELECT FieldKey FROM access.RoleFieldSecurity WHERE RoleId=@RoleId' @{ '@RoleId'=[string]$fieldSuccess.Body.aggregateId } -Scalar))
    }
    Assert-ValidationFailure '161 supplementary fieldKey' (Invoke-Create $Port $freshToken $freshWorkspaceId (New-Body 'Unicode Field Overflow' $null $null @() @(@{ resourceKey='contacts'; fieldKey=$key161; access='READ_ONLY' })) 'idem-unicode-field-161')
    Stop-Host $freshHost
    $freshHost = $null

    # Historical database: migrate only to the committed pre-correction state, seed, then upgrade.
    New-Database $HistoricalDatabaseName
    Update-AccessControlDatabase $HistoricalDatabaseName $preCorrectionMigration
    $legacyWorkspaceId = 'ws_legacy_normalization'
    $asciiName = ' Admin '
    $unicodeName = "$([char]0x2003)Manager$([char]0x2003)"
    $nonAsciiName = ([char]::ConvertFromUtf32(0x10428)) + 'role'
    Seed-HistoricalRole $HistoricalDatabaseName 'role_legacy_ascii' $legacyWorkspaceId $asciiName 'ASCII fixture'
    Seed-HistoricalRole $HistoricalDatabaseName 'role_legacy_unicode' $legacyWorkspaceId $unicodeName 'Unicode whitespace fixture'
    Seed-HistoricalRole $HistoricalDatabaseName 'role_legacy_casing' $legacyWorkspaceId $nonAsciiName 'Non-ASCII casing fixture'
    Seed-HistoricalRole $HistoricalDatabaseName 'role_legacy_preserved' $legacyWorkspaceId 'Observer' 'Preservation fixture'
    $historicalRelatedDataSql =
        @"
        INSERT INTO access.RoleCapabilities(RoleId,Capability) VALUES(N'role_legacy_ascii',N'access.configure');
        INSERT INTO access.RoleDataScopes(PolicyId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson,WorkspaceId)
            VALUES(N'scope_legacy_preserved',N'role_legacy_preserved',N'contacts',N'CUSTOM',N'[`"owner_legacy`"]',@WorkspaceId);
        INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
            VALUES(N'assign_legacy_preserved',@WorkspaceId,N'wsm_legacy_preserved',N'role_legacy_preserved',SYSUTCDATETIME());
"@
    $null = Invoke-Sql $HistoricalDatabaseName $historicalRelatedDataSql @{ '@WorkspaceId'=$legacyWorkspaceId } -NonQuery
    $preservedDataSql =
        @"
        SELECT r.Name,r.Description,s.ResourceKey,s.Scope,s.AllowedOwnerIdsJson,a.MembershipId
        FROM access.Roles r
        JOIN access.RoleDataScopes s ON s.RoleId=r.RoleId
        JOIN access.MembershipRoleAssignments a ON a.RoleId=r.RoleId
        WHERE r.RoleId=N'role_legacy_preserved';
"@
    $preservedBefore = @(Invoke-Sql $HistoricalDatabaseName $preservedDataSql)[0] | ConvertTo-Json -Compress
    Update-AccessControlDatabase $HistoricalDatabaseName
    $historicalHost = Start-Host $HistoricalDatabaseName ($Port + 1) $true

    Add-Result 'legacy ASCII whitespace repaired' $asciiName.Trim().ToUpperInvariant() ([string](Invoke-Sql $HistoricalDatabaseName "SELECT NormalizedName FROM access.Roles WHERE RoleId=N'role_legacy_ascii'" -Scalar))
    Add-Result 'legacy Unicode whitespace repaired' $unicodeName.Trim().ToUpperInvariant() ([string](Invoke-Sql $HistoricalDatabaseName "SELECT NormalizedName FROM access.Roles WHERE RoleId=N'role_legacy_unicode'" -Scalar))
    Add-Result 'legacy non-ASCII casing parity' $nonAsciiName.Trim().ToUpperInvariant() ([string](Invoke-Sql $HistoricalDatabaseName "SELECT NormalizedName FROM access.Roles WHERE RoleId=N'role_legacy_casing'" -Scalar))
    $preservedAfter = @(Invoke-Sql $HistoricalDatabaseName $preservedDataSql)[0] | ConvertTo-Json -Compress
    Add-Result 'unrelated historical role policy assignment intact' $preservedBefore $preservedAfter

    $historicalToken = Sign-In ($Port + 1)
    $accountId = [string](Invoke-Sql $HistoricalDatabaseName "SELECT AccountId FROM iam.Accounts WHERE NormalizedEmail=@Email" @{ '@Email'=$email.ToUpperInvariant() } -Scalar)
    $membershipId = 'wsm_legacy_runtime'
    $memberId = [string](Invoke-Sql $HistoricalDatabaseName 'SELECT MemberId FROM iam.Accounts WHERE AccountId=@AccountId' @{ '@AccountId'=$accountId } -Scalar)
    $workspaceKey = 'legacy-normalization'
    $trustedWorkspaceSql =
        @"
        INSERT INTO workspace.Workspaces(WorkspaceId,[Key],Name,LogoText,CreatedAt)
            VALUES(@WorkspaceId,@WorkspaceKey,N'Legacy Normalization',N'LN',SYSUTCDATETIME());
        INSERT INTO workspace.Memberships(MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt)
            VALUES(@MembershipId,@WorkspaceId,@AccountId,@MemberId,N'Active',SYSUTCDATETIME());
        INSERT INTO access.MembershipRoleAssignments(AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
            VALUES(N'assign_legacy_runtime',@WorkspaceId,@MembershipId,N'role_legacy_ascii',SYSUTCDATETIME());
"@
    $null = Invoke-Sql $HistoricalDatabaseName $trustedWorkspaceSql @{ '@WorkspaceId'=$legacyWorkspaceId; '@WorkspaceKey'=$workspaceKey; '@MembershipId'=$membershipId; '@AccountId'=$accountId; '@MemberId'=$memberId } -NonQuery
    $legacyConflict = Invoke-Create ($Port + 1) $historicalToken $legacyWorkspaceId (New-Body 'admin') 'idem-legacy-conflict'
    Add-Result 'runtime create conflicts with repaired legacy role status' 409 $legacyConflict.Status
    Add-Result 'runtime create conflicts with repaired legacy role code' 'ROLE_NAME_CONFLICT' $legacyConflict.Body.code
    Stop-Host $historicalHost
    $historicalHost = $null

    # Collision database: the exact correction fails host startup before changing either role.
    New-Database $CollisionDatabaseName
    Update-AccessControlDatabase $CollisionDatabaseName $preCorrectionMigration
    $collisionWorkspaceId = 'ws_legacy_collision'
    Seed-HistoricalRole $CollisionDatabaseName 'role_collision_a' $collisionWorkspaceId ' Admin' 'collision A'
    Seed-HistoricalRole $CollisionDatabaseName 'role_collision_b' $collisionWorkspaceId 'admin ' 'collision B'
    Update-AccessControlDatabase $CollisionDatabaseName
    $collisionBefore = @(Invoke-Sql $CollisionDatabaseName "SELECT RoleId,Name,NormalizedName,Description FROM access.Roles ORDER BY RoleId") | ConvertTo-Json -Compress
    Set-HostEnvironment $CollisionDatabaseName ($Port + 2) $false
    $collisionStdOut = Join-Path $logRoot 'collision.out.log'
    $collisionStdErr = Join-Path $logRoot 'collision.err.log'
    $collisionProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $collisionStdOut -RedirectStandardError $collisionStdErr -PassThru
    $collisionProcess.WaitForExit(60000) | Out-Null
    if (-not $collisionProcess.HasExited) { Stop-Process -Id $collisionProcess.Id -Force; $collisionProcess.WaitForExit() }
    $collisionLogs = ((Get-Content -Raw $collisionStdErr) + "`n" + (Get-Content -Raw $collisionStdOut))
    Assert-True 'legacy canonical collision fails upgrade startup' ($collisionProcess.HasExited -and $collisionProcess.ExitCode -ne 0)
    Assert-True 'legacy collision failure is actionable' ($collisionLogs -match 'legacy normalization collision' -and $collisionLogs -match 'role_collision_a' -and $collisionLogs -match 'role_collision_b')
    $collisionAfter = @(Invoke-Sql $CollisionDatabaseName "SELECT RoleId,Name,NormalizedName,Description FROM access.Roles ORDER BY RoleId") | ConvertTo-Json -Compress
    Add-Result 'legacy collision rows remain intact' $collisionBefore $collisionAfter
}
finally {
    Stop-Host $freshHost
    Stop-Host $historicalHost
    if ($null -ne $collisionProcess -and -not $collisionProcess.HasExited) { Stop-Process -Id $collisionProcess.Id -Force; $collisionProcess.WaitForExit() }
    if (-not $KeepDatabases) {
        foreach ($databaseName in @($FreshDatabaseName, $HistoricalDatabaseName, $CollisionDatabaseName)) {
            try { Remove-Database $databaseName } catch { $script:Results.Add("WARN | cleanup $databaseName | $($_.Exception.Message)") }
        }
    }
    Remove-Item -LiteralPath $logRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$script:Results | ForEach-Object { Write-Host $_ }
Write-Host "RESULT | passed=$script:Passed failed=$script:Failed"
if ($script:Failed -ne 0) { throw "createAccessRole Unicode/legacy upgrade verification failed: $script:Failed check(s)." }
