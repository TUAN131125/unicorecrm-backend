param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

# Proves the local developer secret configuration contract:
#
#   A. a missing appsettings.Development.Local.json does not break startup, and the tracked
#      configuration alone seeds no demo account, because it carries no password;
#   B. an existing local file is loaded before the demo fixture is composed and overrides the
#      tracked connection string, so one-click startup needs no environment variable;
#   C. environment variables still outrank the local file, so the isolated verification harnesses
#      cannot be silently redirected at a developer's own database.
#
# The developer's own appsettings.Development.Local.json is never read, moved, copied or deleted by
# this harness. That file holds a real SQL password and a real Gmail App Password, and a harness that
# relocated it into %TEMP% would both put those secrets somewhere they do not belong and risk
# orphaning the copy if the run were killed between the move and the restore.
#
# Instead the host is started against a synthetic content root: a temporary directory holding copies
# of the two tracked, credential-free appsettings files and nothing else. The configuration pipeline
# under test is identical - the local file is resolved relative to the content root - but every
# secret in play is one this harness invented for the run, and the developer's real file is simply
# not in the directory the host reads.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$localDatabase = $DatabaseName + '_Local'
$environmentDatabase = $DatabaseName + '_Environment'
$baseUrl = 'http://127.0.0.1:5093'
$demoPassword = 'Local-Config-Smoke!234'
$demoEmail = 'admin@unicorecrm.local'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$realContentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$developerLocalConfigPath = Join-Path $realContentRoot 'appsettings.Development.Local.json'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-local-config-' + [Guid]::NewGuid().ToString('N')))
# The synthetic content root the host actually runs against.
$contentRoot = (New-Item -ItemType Directory -Path (Join-Path $temporaryDirectory 'contentroot')).FullName
$localConfigPath = Join-Path $contentRoot 'appsettings.Development.Local.json'
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()

function Invoke-SqlScalar([string] $database, [string] $query) {
    $value = & sqlcmd -S $server -d $database -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed against ${database}: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function New-Database([string] $database) {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END; CREATE DATABASE [$database];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create $database." }
    $env:ConnectionStrings__UnicoreCRM = Get-ConnectionString $database
    $contexts = @(
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'IdentityAuthDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'WorkspaceDbContext' },
        @{ Project = 'src/UnicoreCRM.Platform'; Context = 'AccessControlDbContext' },
        @{ Project = 'src/UnicoreCRM.Operations'; Context = 'TasksDbContext' },
        @{ Project = 'src/UnicoreCRM.Crm'; Context = 'LeadsDbContext' },
        @{ Project = 'src/UnicoreCRM.Crm'; Context = 'DealsDbContext' },
        @{ Project = 'src/UnicoreCRM.Integrations'; Context = 'IntegrationsDbContext' },
        @{ Project = 'src/UnicoreCRM.PlatformOperations'; Context = 'InboxDbContext' }
    )
    foreach ($entry in $contexts) {
        & dotnet ef database update --project (Join-Path $solutionRoot $entry.Project) --context $entry.Context --no-build | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not apply migrations for $($entry.Context) to $database." }
    }
    Remove-Item Env:\ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
}

function Get-ConnectionString([string] $database) {
    return "Server=$server;Database=$database;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}

function ConvertTo-JsonStringValue([string] $value) {
    return $value.Replace('\', '\\').Replace('"', '\"')
}

# Builds the synthetic content root: the two tracked, credential-free appsettings files and nothing
# else. Copying only tracked files is what keeps the developer's real Local.json out of this run - it
# is deliberately not among them, and it is never read.
function New-SyntheticContentRoot {
    foreach ($fileName in @('appsettings.json', 'appsettings.Development.json')) {
        $source = Join-Path $realContentRoot $fileName
        if (-not (Test-Path -LiteralPath $source)) { throw "The tracked configuration file $fileName is missing." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $contentRoot $fileName) -Force
    }

    if (Test-Path -LiteralPath $localConfigPath) {
        throw 'The synthetic content root must start with no local configuration file.'
    }

    $checks.Add('Synthetic content root composed from tracked configuration only=PASS')
}

# Written with comments on purpose: the .NET JSON configuration parser skips them, and the tracked
# example file the developer copies is commented. Every value here is invented for this run.
function Write-LocalConfig([string] $database, [string] $password) {
    $content = @"
// Temporary file written by verify-development-local-configuration.ps1 into a synthetic content
// root. It never touches the developer's own appsettings.Development.Local.json.
{
  "ConnectionStrings": {
    "UnicoreCRM": "$(ConvertTo-JsonStringValue (Get-ConnectionString $database))"
  },
  // Supplies the credential the tracked configuration deliberately no longer carries.
  "DevelopmentDemoBootstrap": {
    "Password": "$(ConvertTo-JsonStringValue $password)"
  }
}
"@
    Set-Content -LiteralPath $localConfigPath -Value $content -Encoding utf8
}

function Start-ApiHost([string] $environmentConnectionString) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:Development__ApplyMigrations = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    if ([string]::IsNullOrEmpty($environmentConnectionString)) {
        Remove-Item Env:\ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
    }
    else {
        $env:ConnectionStrings__UnicoreCRM = $environmentConnectionString
    }

    $standardOut = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    # The working directory is the synthetic content root, which is where the host resolves
    # appsettings.Development.Local.json from.
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOut -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $standardError -Raw)) $((Get-Content -LiteralPath $standardOut -Raw))"
        }
        try {
            $probe = $client.GetAsync("$baseUrl/auth/session").GetAwaiter().GetResult()
            if ([int] $probe.StatusCode -eq 401) { return $process }
        }
        catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'ApiHost did not listen within the smoke timeout.'
}

function Stop-ApiHost($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
}

function Test-SignIn([string] $email, [string] $password) {
    $id = [Guid]::NewGuid().ToString('N')
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new('POST'), "$baseUrl/auth/sessions")
    $message.Content = [System.Net.Http.StringContent]::new(
        (@{ email = $email; password = $password; deviceLabel = 'Local config smoke' } | ConvertTo-Json -Compress),
        [Text.Encoding]::UTF8,
        'application/json')
    $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-signin-$id")
    $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', "idem-signin-$id")
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $message.Dispose()
    return [int] $response.StatusCode
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name failed." }
    $checks.Add("$name=PASS")
}

# Records the developer's real local file as it stands, so the run can prove it was left untouched.
function Get-DeveloperLocalConfigState {
    if (-not (Test-Path -LiteralPath $developerLocalConfigPath)) { return 'absent' }
    $item = Get-Item -LiteralPath $developerLocalConfigPath
    $hash = (Get-FileHash -LiteralPath $developerLocalConfigPath -Algorithm SHA256).Hash
    return "$hash|$($item.Length)|$($item.LastWriteTimeUtc.Ticks)"
}

$hostProcess = $null
$developerLocalConfigBefore = Get-DeveloperLocalConfigState
try {
    New-SyntheticContentRoot
    New-Database $localDatabase
    New-Database $environmentDatabase
    $checks.Add('Isolated databases migrated=PASS')

    # A: no local file at all.
    Assert-True (-not (Test-Path -LiteralPath $localConfigPath)) 'A: no local configuration file is present'
    $hostProcess = Start-ApiHost (Get-ConnectionString $environmentDatabase)
    $checks.Add('A: host started without a local configuration file=PASS')
    Assert-True ((Test-SignIn $demoEmail $demoPassword) -eq 401) 'A: tracked configuration alone seeds no demo credential'
    Assert-True ([int](Invoke-SqlScalar $environmentDatabase 'SELECT COUNT(*) FROM iam.Accounts;') -eq 0) 'A: tracked configuration alone seeds no account'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # B: the local file is loaded before the demo fixture and overrides the tracked defaults.
    Write-LocalConfig $localDatabase $demoPassword
    $hostProcess = Start-ApiHost $null
    $checks.Add('B: host started with a local configuration file and no environment override=PASS')
    Assert-True ((Test-SignIn $demoEmail $demoPassword) -eq 200) 'B: local file supplied the demo credential before the fixture was composed'
    Assert-True ([int](Invoke-SqlScalar $localDatabase 'SELECT COUNT(*) FROM iam.Accounts;') -eq 1) 'B: local file overrode the tracked connection string'
    Assert-True ([int](Invoke-SqlScalar $environmentDatabase 'SELECT COUNT(*) FROM iam.Accounts;') -eq 0) 'B: the other database was untouched'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # C: environment variables still outrank the local file.
    $hostProcess = Start-ApiHost (Get-ConnectionString $environmentDatabase)
    Assert-True ((Test-SignIn $demoEmail $demoPassword) -eq 200) 'C: local file still supplied the demo credential'
    Assert-True ([int](Invoke-SqlScalar $environmentDatabase 'SELECT COUNT(*) FROM iam.Accounts;') -eq 1) 'C: the environment connection string won over the local file'
    Assert-True ([int](Invoke-SqlScalar $localDatabase 'SELECT COUNT(*) FROM iam.Accounts;') -eq 1) 'C: the local database gained no second account'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # D: the developer's own local file was never involved. Only synthetic values were written, and
    # every one of them lives inside the temporary content root this run created.
    Assert-True ((Get-DeveloperLocalConfigState) -eq $developerLocalConfigBefore) "D: the developer's own local configuration file is byte-for-byte untouched"
    Assert-True ((Get-ChildItem -LiteralPath $contentRoot | Measure-Object).Count -eq 3) 'D: the synthetic content root holds only the files this run wrote'
    Assert-True ((Get-Content -LiteralPath $localConfigPath -Raw).Contains($demoPassword)) 'D: the only credential written to temp is the one this run invented'

    [pscustomobject] @{
        Status = 'PASS'
        LocalDatabase = $localDatabase
        EnvironmentDatabase = $environmentDatabase
        ContentRoot = $contentRoot
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
    Remove-Item Env:\ConnectionStrings__UnicoreCRM -ErrorAction SilentlyContinue
    # Everything this harness wrote lives under one temporary directory it owns, so cleanup is a
    # single deterministic delete and a hard kill can leave nothing but synthetic values behind.
    Remove-Item -LiteralPath $temporaryDirectory.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
