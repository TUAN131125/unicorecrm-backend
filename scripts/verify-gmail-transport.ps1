param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

# Proves the real Gmail transport and the delivery-failure classification against the live
# smtp.gmail.com submission service, without sending anyone an email.
#
# The host is configured with the developer's real Gmail Username from the untracked local
# configuration and a deliberately wrong App Password. MailKit therefore completes the full DNS,
# TCP and STARTTLS handshake with Gmail and gets a real `535 5.7.8 Username and Password not
# accepted` back. Nothing is ever queued for a real mailbox, and exactly one delivery attempt is
# made.
#
# What that proves for this change: the transport still works end to end after the MailKit package
# reference was made compile-private; a genuine Gmail authentication rejection classifies as
# SMTP_AUTH_FAILED rather than being quoted; and neither the username nor the password reaches
# `iam.EmailOutboxMessages.LastError` or the host log.
#
# The developer's appsettings.Development.Local.json is read for the Username only. It is never
# copied, moved or written.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5095'
$password = 'Gmail-Transport-Smoke!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$account = 'gmail.transport@example.test'
# Deliberately wrong, and recognisable, so the harness can prove it never reaches a log or a stored
# reason. It is not the developer's real App Password and never authenticates.
$wrongAppPassword = 'wrongpw' + [Guid]::NewGuid().ToString('N').Substring(0, 9)
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$localConfigPath = Join-Path $contentRoot 'appsettings.Development.Local.json'
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-gmail-transport-' + [Guid]::NewGuid().ToString('N')))
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name failed." }
    $checks.Add("$name=PASS")
}

function Get-RealSenderUsername {
    if (-not (Test-Path -LiteralPath $localConfigPath)) {
        throw 'No appsettings.Development.Local.json is present, so the real Gmail account is unknown.'
    }

    $configured = (Get-Content -LiteralPath $localConfigPath -Raw | ConvertFrom-Json).IdentityAuth.EmailVerification.Sender
    if ([string]::IsNullOrWhiteSpace($configured.Username)) {
        throw 'The local configuration carries no Gmail Username.'
    }

    return [pscustomobject] @{
        Username = $configured.Username
        FromAddress = if ([string]::IsNullOrWhiteSpace($configured.FromAddress)) { $configured.Username } else { $configured.FromAddress }
    }
}

function Initialize-Database {
    & sqlcmd -S $server -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated verification database.' }
    $env:ConnectionStrings__UnicoreCRM = $connection
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
        if ($LASTEXITCODE -ne 0) { throw "Could not apply migrations for $($entry.Context)." }
    }
    $checks.Add('Isolated database migrated=PASS')
}

function Start-ApiHost($sender) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'GmailSmtp'
    $env:IdentityAuth__EmailVerification__Sender__Host = 'smtp.gmail.com'
    $env:IdentityAuth__EmailVerification__Sender__Port = '587'
    $env:IdentityAuth__EmailVerification__Sender__UseStartTls = 'true'
    $env:IdentityAuth__EmailVerification__Sender__Username = $sender.Username
    $env:IdentityAuth__EmailVerification__Sender__AppPassword = $wrongAppPassword
    $env:IdentityAuth__EmailVerification__Sender__FromAddress = $sender.FromAddress
    $env:IdentityAuth__EmailVerification__Sender__TimeoutSeconds = '30'
    # Exactly one delivery attempt: this run authenticates against the live Gmail service, and a
    # retry loop against a wrong password is neither needed nor polite.
    $env:IdentityAuth__EmailVerification__Outbox__MaxAttempts = '1'
    $env:IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds = '2'
    $env:IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds = '30'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'false'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:Development__ApplyMigrations = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'

    $script:hostOut = Join-Path $temporaryDirectory 'host.out.log'
    $script:hostErr = Join-Path $temporaryDirectory 'host.err.log'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $script:hostOut -RedirectStandardError $script:hostErr -PassThru
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $script:hostErr -Raw))"
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

function Test-HostLogMatches([string] $pattern) {
    foreach ($file in @($script:hostOut, $script:hostErr)) {
        if ((Test-Path -LiteralPath $file) -and (Select-String -LiteralPath $file -Pattern $pattern -Quiet)) { return $true }
    }
    return $false
}

$hostProcess = $null
try {
    $sender = Get-RealSenderUsername
    Initialize-Database
    $hostProcess = Start-ApiHost $sender

    $id = [Guid]::NewGuid().ToString('N')
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new('POST'), "$baseUrl/auth/accounts")
    $message.Content = [System.Net.Http.StringContent]::new(
        (@{ email = $account; password = $password; displayName = 'Gmail Transport' } | ConvertTo-Json -Compress),
        [Text.Encoding]::UTF8,
        'application/json')
    $null = $message.Headers.TryAddWithoutValidation('X-Request-Id', "req-register-$id")
    $null = $message.Headers.TryAddWithoutValidation('X-Correlation-Id', "corr-register-$id")
    $null = $message.Headers.TryAddWithoutValidation('Idempotency-Key', "idem-register-$id")
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $message.Dispose()
    Assert-True ([int] $response.StatusCode -eq 201) 'Registration succeeded against the live Gmail boundary'
    $accountId = ($body | ConvertFrom-Json).accountId

    $reason = ''
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        $reason = Invoke-SqlScalar "SELECT ISNULL(LastError, '') FROM iam.EmailOutboxMessages WHERE AccountId='$accountId';"
        if ($reason.Length -gt 0) { break }
        Start-Sleep -Milliseconds 500
    }

    Assert-True ($reason -eq 'SMTP_AUTH_FAILED') 'A real Gmail authentication rejection classified as SMTP_AUTH_FAILED'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountId' AND AttemptCount=1;") -eq 1) 'Exactly one delivery attempt was made'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountId' AND Status='Sent';") -eq 0) 'Nothing was reported as delivered'
    foreach ($secret in @(
            @{ Name = 'Gmail username'; Value = $sender.Username },
            @{ Name = 'app password'; Value = $wrongAppPassword },
            @{ Name = 'recipient'; Value = $account })) {
        $safeValue = $secret.Value.Replace("'", "''")
        Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE LastError LIKE '%$safeValue%';") -eq 0) "LastError carries no $($secret.Name)"
        Assert-True (-not (Test-HostLogMatches ([regex]::Escape($secret.Value)))) "No host log carries the $($secret.Name)"
    }

    Assert-True (-not (Test-HostLogMatches 'Username and Password not accepted')) 'No host log carries the Gmail server response text'
    Assert-True (-not (Test-HostLogMatches '5\.7\.8')) 'No host log carries the Gmail enhanced status code'

    [pscustomobject] @{
        Status = 'PASS'
        Database = $DatabaseName
        Account = $accountId
        RecordedReason = $reason
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
    Remove-Item -LiteralPath $temporaryDirectory.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
