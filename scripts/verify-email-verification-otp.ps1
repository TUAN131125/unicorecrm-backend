param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseName
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$server = '(localdb)\MSSQLLocalDB'
$connection = "Server=$server;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$baseUrl = 'http://127.0.0.1:5091'
$password = 'Email-Verification-Smoke!234'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$accountA = 'otp.primary@example.test'
$accountB = 'otp.restart@example.test'
$accountC = 'otp.failclosed@example.test'
$unknownEmail = 'otp.nobody@example.test'
$accountD = 'otp.undeliverable@example.test'
$accountE = 'otp.superseded@example.test'
$accountF = 'otp.echo@example.test'
$accountG = 'otp.dispatchergate@example.test'
$batchAccounts = 1..5 | ForEach-Object { "otp.batch$_@example.test" }
$accountH = 'otp.concurrency@example.test'
# Long enough to hold a delivery attempt open across a resend, and long enough that five
# sequential sends run well past the duration of any single claim.
$slowSendMilliseconds = 9000
$batchLeaseSeconds = 30
$batchSenderTimeoutSeconds = 1
# The dispatcher raises a configured lease to senderTimeout + a 30s safety margin.
$expectedEffectiveLease = [Math]::Max($batchLeaseSeconds, $batchSenderTimeoutSeconds + 30)
# The credential the simulated provider echoes back inside its error text, alongside the recipient,
# the subject and the live code.
$echoMarker = 'smoke-echo-user-' + [Guid]::NewGuid().ToString('N')
# A recognisable fake credential, so the harness can prove it never reaches a log or a stored reason.
$undeliverableSecret = 'smoke-app-password-' + [Guid]::NewGuid().ToString('N')
$maxAttempts = 3
# The complete set of values IdentityAuth may persist in iam.EmailOutboxMessages.LastError. Anything
# outside it would mean provider-authored text reached durable state.
$boundedReasons = @(
    'EMAIL_SENDER_UNAVAILABLE', 'SMTP_AUTH_FAILED', 'SMTP_CONNECT_FAILED', 'SMTP_TIMEOUT',
    'SMTP_PROTOCOL_ERROR', 'SMTP_COMMAND_FAILED', 'SMTP_RECIPIENT_REJECTED', 'SMTP_PROVIDER_UNAVAILABLE',
    'UNKNOWN_DELIVERY_FAILURE', 'PAYLOAD_UNREADABLE', 'CODE_EXPIRED_BEFORE_DELIVERY',
    'CHALLENGE_SUPERSEDED', 'CHALLENGE_CONSUMED', 'CHALLENGE_EXPIRED', 'CHALLENGE_NOT_DELIVERABLE')
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-email-otp-' + [Guid]::NewGuid().ToString('N')))
# Stands in for the simulated provider's own transcript: the harness reads the exact recipient,
# subject and code that provider echoed, and then proves none of them reached the outbox or the log.
$echoTranscript = Join-Path $temporaryDirectory 'simulated-provider-transcript.log'
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()
$script:hostOut = $null
$script:hostErr = $null
$script:extraEnvironmentNames = @()

function Invoke-SqlScalar([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return (($value | Where-Object { $_.Trim().Length -gt 0 }) -join '').Trim()
}

function Invoke-SqlCommand([string] $query) {
    & sqlcmd -S $server -d $DatabaseName -b -Q "SET NOCOUNT ON; $query" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
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

function Start-ApiHost([string] $senderKind = 'DevelopmentLog', [string] $environment = 'Development', [hashtable] $extraEnvironment = $null) {
    # Extras from a previous start must not leak into this one.
    foreach ($name in $script:extraEnvironmentNames) {
        Remove-Item "Env:\$name" -ErrorAction SilentlyContinue
    }
    $script:extraEnvironmentNames = @()

    $env:ASPNETCORE_ENVIRONMENT = $environment
    $env:DOTNET_ENVIRONMENT = $environment
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__EmailVerification__Sender__Kind = $senderKind
    $env:IdentityAuth__EmailVerification__ExpiryMinutes = '5'
    $env:IdentityAuth__EmailVerification__MaxAttempts = "$maxAttempts"
    # Deliberately long, so every cooldown outcome under test is decided by the persisted
    # ResendAvailableAt this harness controls rather than by how fast the run happens to be.
    $env:IdentityAuth__EmailVerification__ResendIntervalSeconds = '300'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    # This harness owns the exact schema state under test, so the one-click Development
    # migration pass must stay off.
    $env:Development__ApplyMigrations = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'

    # This harness isolates persisted OTP challenge semantics. Keep the separate PLAT-SEC-01
    # request boundary above the number of calls made here; its own verifier proves those limits.
    foreach ($operation in @('Registration', 'VerificationRequest', 'VerificationSubmission', 'PasswordSignIn', 'SessionRefresh')) {
        Set-Item "Env:IdentityAuth__AbuseProtection__$($operation)__OriginPermitLimit" '10000'
        Set-Item "Env:IdentityAuth__AbuseProtection__$($operation)__SubjectPermitLimit" '10000'
        Set-Item "Env:IdentityAuth__AbuseProtection__$($operation)__WindowSeconds" '60'
    }

    # Applied last, so a section can override one of the defaults above rather than only add to them.
    if ($null -ne $extraEnvironment) {
        foreach ($entry in $extraEnvironment.GetEnumerator()) {
            Set-Item "Env:\$($entry.Key)" $entry.Value
            $script:extraEnvironmentNames += $entry.Key
        }
    }

    $standardOut = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $script:hostOut = $standardOut
    $script:hostErr = $standardError
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

function Send-Json([string] $method, [string] $path, [string] $body, [hashtable] $headers) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($method), "$baseUrl$path")
    if (-not [string]::IsNullOrEmpty($body)) {
        $message.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    }
    foreach ($entry in $headers.GetEnumerator()) {
        $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
    }
    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $message.Dispose()
    return [pscustomobject] @{ Status = [int] $response.StatusCode; Body = $text }
}

function New-Headers([string] $prefix) {
    $id = [Guid]::NewGuid().ToString('N')
    return @{
        'X-Request-Id' = "req-$prefix-$id"
        'X-Correlation-Id' = "corr-$prefix-$id"
        'Idempotency-Key' = "idem-$prefix-$id"
    }
}

function Assert-Status($response, [int] $expected, [string] $name) {
    if ($response.Status -ne $expected) {
        throw "$name expected HTTP $expected but got $($response.Status): $($response.Body)"
    }
    $checks.Add("$name=$expected")
}

function Assert-Code($response, [int] $expectedStatus, [string] $expectedCode, [string] $name) {
    Assert-Status $response $expectedStatus $name
    $actual = ($response.Body | ConvertFrom-Json).code
    if ($actual -ne $expectedCode) { throw "$name expected error code $expectedCode but got $actual" }
    $checks.Add("$name.code=$expectedCode")
}

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "$name failed." }
    $checks.Add("$name=PASS")
}

function Register-Account([string] $email, [string] $displayName) {
    return Send-Json 'POST' '/auth/accounts' (@{
        email = $email
        password = $password
        displayName = $displayName
    } | ConvertTo-Json -Compress) (New-Headers 'register')
}

function Request-Verification([string] $email, [hashtable] $headers = $null) {
    if ($null -eq $headers) { $headers = New-Headers 'evr' }
    return Send-Json 'POST' '/auth/email-verification-requests' (@{ email = $email } | ConvertTo-Json -Compress) $headers
}

function Submit-Code([string] $email, [string] $code) {
    return Send-Json 'POST' '/auth/email-verifications' (@{ email = $email; code = $code } | ConvertTo-Json -Compress) (New-Headers 'evc')
}

function Sign-In([string] $email) {
    return Send-Json 'POST' '/auth/sessions' (@{
        email = $email
        password = $password
        deviceLabel = 'Email verification smoke'
    } | ConvertTo-Json -Compress) (New-Headers 'signin')
}

# Reads the code the Development sender wrote to the backend console. The harness never reads a
# code from persistence, because only the digest is stored there.
function Get-EmittedCode([string] $email, [string] $notEqualTo = $null) {
    $pattern = 'DEVELOPMENT EMAIL VERIFICATION \| to=' + [regex]::Escape($email) + ' \| code=(\d{6})'
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (Test-Path -LiteralPath $script:hostOut) {
            $found = @(Select-String -LiteralPath $script:hostOut -Pattern $pattern -AllMatches)
            if ($found.Count -gt 0) {
                $lastMatch = $found[$found.Count - 1].Matches
                $code = $lastMatch[$lastMatch.Count - 1].Groups[1].Value
                if ([string]::IsNullOrEmpty($notEqualTo) -or $code -ne $notEqualTo) { return $code }
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "No development verification code was emitted for $email."
}

function Get-AccountScalar([string] $accountId, [string] $column) {
    return Invoke-SqlScalar "SELECT CONVERT(nvarchar(64), $column) FROM iam.Accounts WHERE AccountId='$accountId';"
}

function Get-ChallengeCount([string] $accountId) {
    return [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId='$accountId';")
}

function Get-OutstandingCount([string] $accountId) {
    return [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId='$accountId' AND ConsumedAt IS NULL AND SupersededAt IS NULL;")
}

function Get-OutboxCount([string] $accountId, [string] $status = $null) {
    $filter = if ([string]::IsNullOrEmpty($status)) { '' } else { " AND Status='$status'" }
    return [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountId'$filter;")
}

# A claim increments AttemptCount before the network call, so the recorded failure lands slightly
# after the attempt itself.
function Wait-OutboxFailure([string] $accountId) {
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $value = Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountId' AND LastError IS NOT NULL;"
        if ([int]$value -ge 1) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Wait-OutboxAttempts([string] $accountId, [int] $expectedAttempts) {
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $value = Invoke-SqlScalar "SELECT ISNULL(MAX(AttemptCount), 0) FROM iam.EmailOutboxMessages WHERE AccountId='$accountId';"
        if ([int]$value -ge $expectedAttempts) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# The dispatcher runs after the issuing transaction commits, so outbox state settles a moment after
# the HTTP response.
function Wait-OutboxSettled([string] $accountId, [int] $expectedSent) {
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        if ((Get-OutboxCount $accountId 'Sent') -ge $expectedSent) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# Clears an in-flight retry backoff so a recovery step is decided by the dispatcher's behaviour
# rather than by how much of the exponential delay happens to be left.
function Clear-OutboxBackoff([string] $accountId) {
    Invoke-SqlCommand "UPDATE iam.EmailOutboxMessages SET NextAttemptAt = SYSDATETIMEOFFSET() WHERE AccountId='$accountId' AND Status='Pending';"
}

function Invoke-SqlLines([string] $query) {
    $value = & sqlcmd -S $server -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $query" }
    return @($value | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
}

# A claim exists only while its own send is running - MarkSent clears it - so the only way to observe
# per-message claiming is to sample the rows while the batch is being delivered.
function Watch-Claims([string] $accountFilter, [int] $seconds, [scriptblock] $onFirstClaim = $null) {
    $observed = @{}
    $firstClaimHandled = $false
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $rows = Invoke-SqlLines "SELECT MessageId + '~' + CONVERT(nvarchar(40), LastAttemptAt, 127) + '~' + CONVERT(nvarchar(40), LeasedUntil, 127) + '~' + CONVERT(nvarchar(12), DATEDIFF(second, LastAttemptAt, LeasedUntil)) + '~' + CONVERT(nvarchar(12), DATEDIFF(second, SYSDATETIMEOFFSET(), LeasedUntil)) + '~' + CONVERT(nvarchar(64), AccountId) FROM iam.EmailOutboxMessages WHERE LeasedUntil IS NOT NULL AND AccountId IN ($accountFilter);"
        foreach ($row in $rows) {
            $parts = $row.Split('~')
            if ($parts.Count -lt 6) { continue }
            if (-not $observed.ContainsKey($parts[0])) {
                $observed[$parts[0]] = [pscustomobject] @{
                    MessageId = $parts[0]
                    ClaimedAt = $parts[1]
                    LeasedUntil = $parts[2]
                    LeaseSeconds = [int] $parts[3]
                    RemainingAtSample = [int] $parts[4]
                    AccountId = $parts[5]
                }
                if (-not $firstClaimHandled -and $null -ne $onFirstClaim) {
                    $firstClaimHandled = $true
                    & $onFirstClaim $observed[$parts[0]]
                }
            }
        }

        if ((Get-Date) -gt $deadline) { break }
        Start-Sleep -Milliseconds 400
    }

    return $observed
}

function Get-MessageScalar([string] $messageId, [string] $column) {
    return Invoke-SqlScalar "SELECT ISNULL(CONVERT(nvarchar(200), $column), '') FROM iam.EmailOutboxMessages WHERE MessageId='$messageId';"
}

function Get-FirstMessageId([string] $accountId) {
    return Invoke-SqlScalar "SELECT TOP 1 MessageId FROM iam.EmailOutboxMessages WHERE AccountId='$accountId' ORDER BY CreatedAt;"
}

function Get-FirstChallengeId([string] $accountId) {
    return Invoke-SqlScalar "SELECT TOP 1 ChallengeId FROM iam.EmailVerificationChallenges WHERE AccountId='$accountId' ORDER BY CreatedAt;"
}

# Presents a message as claimed by a delivery attempt that has not resolved. The dispatcher stamps
# exactly this state before it calls the provider, so it is what an issuing transaction sees when a
# code is already on its way. NextAttemptAt is pushed out with it so a live dispatcher pass cannot
# re-claim the row and overwrite the state under test; the write is confirmed rather than assumed.
function Set-MessageInFlight([string] $messageId) {
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Invoke-SqlCommand "UPDATE iam.EmailOutboxMessages SET LeasedUntil = DATEADD(minute, 5, SYSDATETIMEOFFSET()), NextAttemptAt = DATEADD(minute, 5, SYSDATETIMEOFFSET()) WHERE MessageId='$messageId' AND Status='Pending';"
        Start-Sleep -Milliseconds 400
        $leased = Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE MessageId='$messageId' AND Status='Pending' AND LeasedUntil > SYSDATETIMEOFFSET();"
        if ([int]$leased -eq 1) { return $true }
    }
    return $false
}

# Releases the simulated claim while keeping the message out of the dispatcher's due set, so the
# supersession under test is decided by the resend rather than by a racing delivery pass.
function Clear-MessageInFlight([string] $messageId) {
    Invoke-SqlCommand "UPDATE iam.EmailOutboxMessages SET LeasedUntil = NULL, NextAttemptAt = DATEADD(minute, 5, SYSDATETIMEOFFSET()) WHERE MessageId='$messageId';"
}

function Get-ChallengeScalar([string] $challengeId, [string] $column) {
    return Invoke-SqlScalar "SELECT ISNULL(CONVERT(nvarchar(64), $column), '') FROM iam.EmailVerificationChallenges WHERE ChallengeId='$challengeId';"
}

# Every host log this run has produced, not just the current one: a code that was never delivered
# must be absent from all of them.
function Get-HostLogFiles([string] $filter = 'host-*.log') {
    return @(Get-ChildItem -LiteralPath $temporaryDirectory.FullName -Filter $filter -ErrorAction SilentlyContinue)
}

function Test-AnyHostLogMatches([string] $pattern) {
    foreach ($log in Get-HostLogFiles) {
        if (Select-String -LiteralPath $log.FullName -Pattern $pattern -Quiet) { return $true }
    }
    return $false
}

function Test-CurrentHostLogMatches([string] $pattern) {
    foreach ($file in @($script:hostOut, $script:hostErr)) {
        if ($null -ne $file -and (Test-Path -LiteralPath $file) -and (Select-String -LiteralPath $file -Pattern $pattern -Quiet)) {
            return $true
        }
    }
    return $false
}

function Get-EmittedCodeCount([string] $email) {
    $pattern = 'DEVELOPMENT EMAIL VERIFICATION \| to=' + [regex]::Escape($email) + ' \| code=(\d{6})'
    $total = 0
    foreach ($log in Get-HostLogFiles 'host-*.out.log') {
        foreach ($hit in @(Select-String -LiteralPath $log.FullName -Pattern $pattern -AllMatches)) {
            $total += $hit.Matches.Count
        }
    }
    return $total
}

function Wait-MessageStatus([string] $messageId, [string] $status) {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ((Get-MessageScalar $messageId 'Status') -eq $status) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Wait-TranscriptLine {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if (Test-Path -LiteralPath $echoTranscript) {
            $lines = @(Get-Content -LiteralPath $echoTranscript | Where-Object { $_.Trim().Length -gt 0 })
            if ($lines.Count -gt 0) { return $lines[0] }
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'The simulated provider wrote no transcript line.'
}

function Clear-Cooldown([string] $accountId) {
    Invoke-SqlCommand "UPDATE iam.EmailVerificationChallenges SET ResendAvailableAt = DATEADD(minute, -1, SYSDATETIMEOFFSET()) WHERE AccountId='$accountId' AND ConsumedAt IS NULL AND SupersededAt IS NULL;"
}

$hostProcess = $null
try {
    Initialize-Database
    $hostProcess = Start-ApiHost

    # A: registration creates a PendingVerification account and one dispatched challenge.
    $registered = Register-Account $accountA 'OTP Primary'
    Assert-Status $registered 201 'A: register account'
    $registeredDocument = $registered.Body | ConvertFrom-Json
    $accountAId = $registeredDocument.accountId
    Assert-True ($registeredDocument.status -eq 'PENDING_VERIFICATION') 'A: new account is PendingVerification'
    Assert-True ($null -eq $registeredDocument.emailVerifiedAt) 'A: new account has no verification stamp'
    Assert-True ((Get-ChallengeCount $accountAId) -eq 1) 'A: registration created exactly one challenge'
    $code1 = Get-EmittedCode $accountA
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId' AND CodeHash LIKE '%$code1%';") -eq 0) 'A: plaintext code is not persisted'

    # A-outbox: the issuing transaction staged exactly one durable message, the dispatcher delivered
    # it after the commit, and no plaintext code was ever written to the outbox either.
    Assert-True ((Get-OutboxCount $accountAId) -eq 1) 'A: registration staged exactly one outbox message'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountAId' AND ProtectedCode LIKE '%$code1%';") -eq 0) 'A: plaintext code is not in the outbox payload'
    Assert-True ((Wait-OutboxSettled $accountAId 1)) 'A: the dispatcher delivered the message after commit'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountAId' AND Status='Sent' AND ProtectedCode IS NULL AND SentAt IS NOT NULL;") -eq 1) 'A: a delivered message clears its payload'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountAId' AND AttemptCount=1;") -eq 1) 'A: delivery took exactly one attempt'
    Assert-True ([int](Invoke-SqlScalar "SELECT LEN(CodeHash) FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId';") -eq 64) 'A: challenge stores a 64-character digest'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.SecurityEvents WHERE AccountId='$accountAId' AND EventType='IDENTITY_EMAIL_VERIFICATION_ISSUED';") -eq 1) 'A: issuance recorded a security event'

    # B: sign-in is refused until the address is proved.
    Assert-Code (Sign-In $accountA) 403 'EMAIL_NOT_VERIFIED' 'B: sign-in before verification'

    # C: contract validation.
    Assert-Code (Submit-Code $accountA '12345') 422 'VALIDATION_FAILED' 'C: five-digit code'
    Assert-Code (Submit-Code $accountA '12345a') 422 'VALIDATION_FAILED' 'C: non-numeric code'
    Assert-Code (Submit-Code 'not-an-email' '123456') 422 'VALIDATION_FAILED' 'C: malformed email'

    # D: a wrong code is rejected and durably spends one attempt.
    $wrong = if ($code1 -eq '000000') { '111111' } else { '000000' }
    Assert-Code (Submit-Code $accountA $wrong) 401 'TOKEN_INVALID' 'D: wrong code'
    Assert-True ([int](Invoke-SqlScalar "SELECT AttemptCount FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId';") -eq 1) 'D: failed attempt was committed'

    # E: a resend inside the cooldown is accepted uniformly but issues nothing.
    $hashBefore = Invoke-SqlScalar "SELECT CodeHash FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId';"
    Assert-Status (Request-Verification $accountA) 202 'E: resend inside cooldown'
    Assert-True ((Get-ChallengeCount $accountAId) -eq 1) 'E: cooldown issued no second challenge'
    Assert-True ((Invoke-SqlScalar "SELECT CodeHash FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId';") -eq $hashBefore) 'E: cooldown left the usable code intact'

    # F: once the cooldown has elapsed a resend supersedes the previous code. The first message had
    # already been delivered before the resend - that is how the holder knows $code1 - so there is no
    # undelivered message to retire here, and supersession must not rewrite delivered history either.
    # The stale-message case, where the first message is still queued when it is superseded, is
    # section R.
    $messageOneA = Get-FirstMessageId $accountAId
    Assert-True ((Get-MessageScalar $messageOneA 'Status') -eq 'Sent') 'F: the first message was delivered before the resend'
    Clear-Cooldown $accountAId
    Assert-Status (Request-Verification $accountA) 202 'F: resend after cooldown'
    Assert-True ((Get-ChallengeCount $accountAId) -eq 2) 'F: resend issued a second challenge'
    Assert-True ((Get-OutstandingCount $accountAId) -eq 1) 'F: exactly one challenge stays outstanding'
    Assert-True ((Get-OutboxCount $accountAId) -eq 2) 'F: the resend staged a second outbox message'
    Assert-True ((Get-MessageScalar $messageOneA 'Status') -eq 'Sent') 'F: supersession leaves an already delivered message delivered'
    Assert-True ((Wait-OutboxSettled $accountAId 2)) 'F: the newly issued message was delivered'
    Assert-True ((Get-OutboxCount $accountAId 'Cancelled') -eq 0) 'F: nothing was cancelled, because nothing was still queued'
    $code2 = Get-EmittedCode $accountA $code1
    Assert-Code (Submit-Code $accountA $code1) 401 'TOKEN_INVALID' 'F: superseded code is refused'

    # G: the attempt ceiling is enforced and survives further guesses. Submitting the superseded
    # code above was itself a wrong guess against the new challenge, so the ceiling is counted from
    # the attempts the challenge has already recorded rather than from zero.
    $spent = [int](Invoke-SqlScalar "SELECT AttemptCount FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId' AND ConsumedAt IS NULL AND SupersededAt IS NULL;")
    Assert-True ($spent -eq 1) 'G: the superseded-code guess spent one attempt'
    for ($attempt = $spent + 1; $attempt -le $maxAttempts; $attempt++) {
        Assert-Code (Submit-Code $accountA $wrong) 401 'TOKEN_INVALID' "G: wrong code attempt $attempt"
    }
    Assert-Code (Submit-Code $accountA $wrong) 429 'RATE_LIMITED' 'G: attempts exhausted'
    Assert-Code (Submit-Code $accountA $code2) 429 'RATE_LIMITED' 'G: correct code after exhaustion'

    # H: spending the attempt ceiling does not buy a fresh code inside the cooldown, so the ceiling
    # cannot be turned into an unthrottled guessing loop.
    $challengesBeforeExhaustedResend = Get-ChallengeCount $accountAId
    Assert-Status (Request-Verification $accountA) 202 'H: resend inside cooldown after exhaustion'
    Assert-True ((Get-ChallengeCount $accountAId) -eq $challengesBeforeExhaustedResend) 'H: exhausted challenge buys no code inside the cooldown'

    # H: an expired code is refused as expired.
    Clear-Cooldown $accountAId
    Assert-Status (Request-Verification $accountA) 202 'H: resend after exhaustion'
    $code3 = Get-EmittedCode $accountA $code2
    Invoke-SqlCommand "UPDATE iam.EmailVerificationChallenges SET ExpiresAt = DATEADD(minute, -1, SYSDATETIMEOFFSET()) WHERE AccountId='$accountAId' AND ConsumedAt IS NULL AND SupersededAt IS NULL;"
    Assert-Code (Submit-Code $accountA $code3) 401 'TOKEN_EXPIRED' 'H: expired code'

    # I: the correct code activates the account exactly once.
    Clear-Cooldown $accountAId
    Assert-Status (Request-Verification $accountA) 202 'I: final resend'
    $code4 = Get-EmittedCode $accountA $code3
    $verified = Submit-Code $accountA $code4
    Assert-Status $verified 200 'I: correct code'
    $verifiedDocument = $verified.Body | ConvertFrom-Json
    Assert-True ($verifiedDocument.status -eq 'ACTIVE') 'I: account is Active'
    Assert-True ($null -ne $verifiedDocument.emailVerifiedAt) 'I: verification stamp is set'
    Assert-True ((Get-AccountScalar $accountAId 'Status') -eq 'Active') 'I: persisted status is Active'
    Assert-True ((Get-OutstandingCount $accountAId) -eq 0) 'I: no challenge is left outstanding'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId='$accountAId' AND ConsumedAt IS NOT NULL;") -eq 1) 'I: exactly one challenge was consumed'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.SecurityEvents WHERE AccountId='$accountAId' AND EventType='IDENTITY_EMAIL_VERIFIED';") -eq 1) 'I: verification recorded a security event'

    # J: the consumed code cannot be reused and a later request issues nothing.
    Assert-Code (Submit-Code $accountA $code4) 401 'TOKEN_INVALID' 'J: consumed code reuse'
    $challengesAfterVerification = Get-ChallengeCount $accountAId
    Assert-Status (Request-Verification $accountA) 202 'J: request for an already active account'
    Assert-True ((Get-ChallengeCount $accountAId) -eq $challengesAfterVerification) 'J: active account was issued no code'

    # K: sign-in succeeds after verification.
    Assert-Status (Sign-In $accountA) 200 'K: sign-in after verification'

    # L: an unknown address is answered uniformly and creates nothing.
    $totalChallenges = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges;')
    Assert-Status (Request-Verification $unknownEmail) 202 'L: request for an unknown address'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges;') -eq $totalChallenges) 'L: unknown address created no challenge'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Email='$unknownEmail';") -eq 0) 'L: unknown address created no account'
    Assert-Code (Submit-Code $unknownEmail '123456') 401 'TOKEN_INVALID' 'L: verification for an unknown address'

    # M: header idempotency on the request endpoint, proved on an address with no account.
    $replayHeaders = New-Headers 'replay'
    $first = Request-Verification $unknownEmail $replayHeaders
    Assert-Status $first 202 'M: first request'
    $replay = Request-Verification $unknownEmail $replayHeaders
    Assert-Status $replay 202 'M: replayed request'
    Assert-True ((($first.Body | ConvertFrom-Json).requestId) -eq (($replay.Body | ConvertFrom-Json).requestId)) 'M: replay returns the same acceptance'
    Assert-Code (Request-Verification $accountA $replayHeaders) 409 'IDEMPOTENCY_KEY_REUSED' 'M: reused key with a different address'

    # N: persisted verification state survives a host restart.
    $registeredB = Register-Account $accountB 'OTP Restart'
    Assert-Status $registeredB 201 'N: register restart account'
    $accountBId = ($registeredB.Body | ConvertFrom-Json).accountId
    $codeB = Get-EmittedCode $accountB
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost
    Assert-True ((Get-OutstandingCount $accountBId) -eq 1) 'N: challenge survived the restart'
    Assert-Code (Sign-In $accountB) 403 'EMAIL_NOT_VERIFIED' 'N: sign-in still refused after restart'
    Assert-Status (Submit-Code $accountB $codeB) 200 'N: pre-restart code verifies after restart'
    Assert-Status (Sign-In $accountB) 200 'N: sign-in after restart verification'

    # O: without a configured sender the flow fails closed and creates nothing.
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'Unavailable'
    Assert-Code (Register-Account $accountC 'OTP Fail Closed') 503 'INTEGRATION_UNAVAILABLE' 'O: registration without a sender'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Email='$accountC';") -eq 0) 'O: failed registration created no account'
    Assert-True ([int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId NOT IN (SELECT AccountId FROM iam.Accounts);') -eq 0) 'O: no orphaned challenge remains'

    # P: a non-Development host cannot select the Development sender.
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'DevelopmentLog' 'Staging'
    Assert-Code (Register-Account $accountC 'OTP Staging') 503 'INTEGRATION_UNAVAILABLE' 'P: Development sender refused outside Development'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Email='$accountC';") -eq 0) 'P: staging registration created no account'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # Q: a delivery boundary that is configured but unreachable. Registration still succeeds,
    # because the transaction no longer waits on SMTP, and the message stays durable and retrying.
    Stop-ApiHost $hostProcess
    $unreachableSmtp = @{
        'IdentityAuth__EmailVerification__Sender__Host' = '127.0.0.1'
        'IdentityAuth__EmailVerification__Sender__Port' = '2525'
        'IdentityAuth__EmailVerification__Sender__Username' = 'smoke-user@example.test'
        'IdentityAuth__EmailVerification__Sender__AppPassword' = $undeliverableSecret
        'IdentityAuth__EmailVerification__Sender__FromAddress' = 'smoke-from@example.test'
        'IdentityAuth__EmailVerification__Sender__TimeoutSeconds' = '5'
        'IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds' = '2'
        'IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds' = '5'
        'IdentityAuth__EmailVerification__Outbox__LeaseSeconds' = '30'
    }
    $hostProcess = Start-ApiHost 'GmailSmtp' 'Development' $unreachableSmtp
    $registeredD = Register-Account $accountD 'OTP Undeliverable'
    Assert-Status $registeredD 201 'Q: registration succeeds when the provider is unreachable'
    $accountDId = ($registeredD.Body | ConvertFrom-Json).accountId
    Assert-True ((($registeredD.Body | ConvertFrom-Json).status) -eq 'PENDING_VERIFICATION') 'Q: the account is PendingVerification'
    Assert-True ((Get-OutboxCount $accountDId) -eq 1) 'Q: exactly one message was staged'
    Assert-True (Wait-OutboxFailure $accountDId) 'Q: the dispatcher attempted delivery and recorded the failure'
    Assert-True ((Get-OutboxCount $accountDId 'Sent') -eq 0) 'Q: nothing was reported as delivered'
    Assert-True ((Get-OutboxCount $accountDId 'Pending') -eq 1) 'Q: the message stays pending for retry'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountDId' AND LastError IS NOT NULL;") -eq 1) 'Q: the failure reason was recorded'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountDId' AND LastError LIKE '%$undeliverableSecret%';") -eq 0) 'Q: the recorded reason carries no credential'
    $reasonD = Invoke-SqlScalar "SELECT ISNULL(LastError, '') FROM iam.EmailOutboxMessages WHERE AccountId='$accountDId';"
    Assert-True ($boundedReasons -contains $reasonD) 'Q: the recorded reason is a bounded application-owned value'
    Assert-True (-not $reasonD.Contains($accountD)) 'Q: the recorded reason carries no recipient address'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId='$accountDId' AND ProtectedCode IS NOT NULL;") -eq 1) 'Q: the undelivered payload is retained for retry'
    Assert-Code (Sign-In $accountD) 403 'EMAIL_NOT_VERIFIED' 'Q: the account cannot sign in yet'
    $attemptsBeforeRestart = [int](Invoke-SqlScalar "SELECT AttemptCount FROM iam.EmailOutboxMessages WHERE AccountId='$accountDId';")
    Assert-True (-not (Select-String -LiteralPath $script:hostOut -Pattern ([regex]::Escape($undeliverableSecret)) -Quiet)) 'Q: no credential reached the host log'

    # K: restart with outstanding delivery work. The pending message resumes, and the restart
    # creates no second account, challenge or message.
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'GmailSmtp' 'Development' $unreachableSmtp
    Assert-True (Wait-OutboxAttempts $accountDId ($attemptsBeforeRestart + 1)) 'K: the pending message resumed after the restart'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Email='$accountD';") -eq 1) 'K: the restart created no second account'
    Assert-True ((Get-ChallengeCount $accountDId) -eq 1) 'K: the restart created no second challenge'
    Assert-True ((Get-OutboxCount $accountDId) -eq 1) 'K: the restart created no second outbox message'
    Assert-True ((Get-OutstandingCount $accountDId) -eq 1) 'K: the original challenge is still the usable one'

    # K: once the boundary works again the same durable message is delivered, and its original
    # code still verifies. Nothing was reissued.
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'DevelopmentLog' 'Development' @{
        'IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds' = '2'
        'IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds' = '5'
    }
    Clear-OutboxBackoff $accountDId
    Assert-True ((Wait-OutboxSettled $accountDId 1)) 'K: the recovered boundary delivered the original message'
    Assert-True ((Get-OutboxCount $accountDId) -eq 1) 'K: recovery created no second message'
    Assert-True ((Get-ChallengeCount $accountDId) -eq 1) 'K: recovery created no second challenge'
    $codeD = Get-EmittedCode $accountD
    Assert-Status (Submit-Code $accountD $codeD) 200 'K: the code from the recovered message verifies'
    Assert-Status (Sign-In $accountD) 200 'K: sign-in succeeds after recovered delivery'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # R: H-1 regression - a superseded code must never be delivered.
    #
    # The exact review scenario: register while the provider is down, so C1 and its message M1 are
    # left retrying; let the resend cooldown elapse; resend; then let the provider recover. Only the
    # code that is still valid may ever reach a mailbox, and the message carrying the revoked code
    # must end terminal and empty rather than be recorded as though it had been sent.
    $hostProcess = Start-ApiHost 'GmailSmtp' 'Development' $unreachableSmtp
    $registeredE = Register-Account $accountE 'OTP Superseded'
    Assert-Status $registeredE 201 'R: register while the provider is unavailable'
    $accountEId = ($registeredE.Body | ConvertFrom-Json).accountId
    $challengeOne = Get-FirstChallengeId $accountEId
    $messageOne = Get-FirstMessageId $accountEId
    Assert-True (Wait-OutboxFailure $accountEId) 'R: M1 attempted delivery and failed'
    Assert-True ((Get-OutboxCount $accountEId 'Pending') -eq 1) 'R: M1 is retrying rather than terminal'
    Assert-True ((Get-MessageScalar $messageOne 'ProtectedCode') -ne '') 'R: M1 keeps its payload while it is still deliverable'

    # R-race: while a delivery attempt is claimed and unresolved the code cannot be revoked, because
    # that send may already have reached the provider. Issuance backs off instead of creating the
    # forbidden state - old code invalid, new code active, old email still on its way.
    Assert-True (Set-MessageInFlight $messageOne) 'R: M1 presented as a claimed, unresolved delivery attempt'
    Clear-Cooldown $accountEId
    Assert-Status (Request-Verification $accountE) 202 'R: resend while a delivery attempt is in flight'
    Assert-True ((Get-ChallengeCount $accountEId) -eq 1) 'R: an in-flight delivery buys no replacement code'
    Assert-True ((Get-ChallengeScalar $challengeOne 'SupersededAt') -eq '') 'R: an in-flight code is not revoked'
    Assert-True ((Get-MessageScalar $messageOne 'Status') -eq 'Pending') 'R: the in-flight message is not cancelled'
    Assert-True ((Get-OutboxCount $accountEId) -eq 1) 'R: the deferred resend staged no message'

    # The claim resolves. The resend now supersedes, and the message still carrying the revoked code
    # becomes terminal and non-deliverable.
    Clear-MessageInFlight $messageOne
    Clear-Cooldown $accountEId
    Assert-Status (Request-Verification $accountE) 202 'R: resend once the claim has resolved'
    Assert-True ((Get-ChallengeCount $accountEId) -eq 2) 'R: the resend issued a second challenge'
    Assert-True ((Get-OutstandingCount $accountEId) -eq 1) 'R: exactly one challenge stays outstanding'
    Assert-True ((Get-ChallengeScalar $challengeOne 'SupersededAt') -ne '') 'R: C1 was superseded'
    Assert-True ((Get-MessageScalar $messageOne 'Status') -eq 'Cancelled') 'R: stale M1 reached a terminal non-deliverable state'
    Assert-True ((Get-MessageScalar $messageOne 'ProtectedCode') -eq '') 'R: stale M1 cleared its payload'
    Assert-True ((Get-MessageScalar $messageOne 'SentAt') -eq '') 'R: stale M1 was never recorded as sent'
    Assert-True ((Get-MessageScalar $messageOne 'LastError') -eq 'CHALLENGE_SUPERSEDED') 'R: stale M1 records why it was retired'
    Assert-True ((Get-OutboxCount $accountEId 'Sent') -eq 0) 'R: nothing has been delivered for this account yet'

    # R-gate: the dispatcher's own fail-closed check, proved independently of the issuing transaction.
    # A second account is staged while the provider is down, and its challenge is then revoked
    # directly in the database - leaving the outbox row exactly in the reviewed defect state, still
    # Pending and still holding its payload, with no issuing transaction having retired it. When the
    # provider comes back the dispatcher must refuse it before any network call.
    $registeredG = Register-Account $accountG 'OTP Dispatcher Gate'
    Assert-Status $registeredG 201 'R-gate: register while the provider is unavailable'
    $accountGId = ($registeredG.Body | ConvertFrom-Json).accountId
    $messageGate = Get-FirstMessageId $accountGId
    $challengeGate = Get-FirstChallengeId $accountGId
    Assert-True (Wait-OutboxFailure $accountGId) 'R-gate: the staged message attempted delivery and failed'
    Invoke-SqlCommand "UPDATE iam.EmailVerificationChallenges SET SupersededAt = SYSDATETIMEOFFSET() WHERE ChallengeId='$challengeGate';"
    Assert-True ((Get-MessageScalar $messageGate 'Status') -eq 'Pending') 'R-gate: the revoked code is still queued for delivery'
    Assert-True ((Get-MessageScalar $messageGate 'ProtectedCode') -ne '') 'R-gate: the queued message still holds a deliverable payload'

    # The provider recovers. Only the currently valid message may reach the sender.
    $messageTwo = Invoke-SqlScalar "SELECT TOP 1 MessageId FROM iam.EmailOutboxMessages WHERE AccountId='$accountEId' AND MessageId <> '$messageOne';"
    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'DevelopmentLog' 'Development' @{
        'IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds' = '2'
        'IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds' = '5'
    }
    Clear-OutboxBackoff $accountEId
    Clear-OutboxBackoff $accountGId
    Assert-True (Wait-MessageStatus $messageTwo 'Sent') 'R: the currently valid message was delivered'
    Assert-True (Wait-MessageStatus $messageGate 'Cancelled') 'R-gate: the dispatcher retired the revoked message instead of delivering it'
    Assert-True ((Get-MessageScalar $messageGate 'LastError') -eq 'CHALLENGE_SUPERSEDED') 'R-gate: the dispatcher recorded why it refused'
    Assert-True ((Get-MessageScalar $messageGate 'SentAt') -eq '') 'R-gate: the refused message was never recorded as sent'
    Assert-True ((Get-MessageScalar $messageGate 'ProtectedCode') -eq '') 'R-gate: the refused message dropped its payload'
    # The sender is the console sender on this host, so a code that reached it would be in the log.
    Assert-True ((Get-EmittedCodeCount $accountG) -eq 0) 'R-gate: the revoked code never reached the sender'
    Assert-Code (Sign-In $accountG) 403 'EMAIL_NOT_VERIFIED' 'R-gate: the account is still awaiting verification'
    Assert-True ((Get-MessageScalar $messageOne 'Status') -eq 'Cancelled') 'R: the superseded message stayed terminal once the provider recovered'
    Assert-True ((Get-OutboxCount $accountEId 'Sent') -eq 1) 'R: exactly one message was ever delivered'
    # The superseded code was never handed to a sender, so there is no stale code for the holder to
    # submit and the live challenge cannot have spent an attempt on one.
    Assert-True ((Get-EmittedCodeCount $accountE) -eq 1) 'R: exactly one code was ever delivered for this account'
    Assert-True ([int](Invoke-SqlScalar "SELECT AttemptCount FROM iam.EmailVerificationChallenges WHERE AccountId='$accountEId' AND ConsumedAt IS NULL AND SupersededAt IS NULL;") -eq 0) 'R: the live challenge burnt no attempt'
    $codeE = Get-EmittedCode $accountE
    Assert-Status (Submit-Code $accountE $codeE) 200 'R: the only delivered code verifies'
    Assert-True ((Get-AccountScalar $accountEId 'Status') -eq 'Active') 'R: the account reached Active'
    Assert-Status (Sign-In $accountE) 200 'R: sign-in succeeds after verification'
    Stop-ApiHost $hostProcess
    $hostProcess = $null

    # S: H-2 regression - provider-authored error text must never be persisted or logged.
    #
    # The simulated provider fails with an error string that echoes the whole envelope back: the
    # exact recipient, the full subject - which for this product contains the code - the live
    # six-digit code itself, and the configured SMTP username. It writes that string to its own
    # transcript, so these assertions run against the real values rather than trusting the sender.
    $hostProcess = Start-ApiHost 'DevelopmentFailing' 'Development' @{
        'IdentityAuth__EmailVerification__Sender__Username' = $echoMarker
        'IdentityAuth__EmailVerification__Sender__SimulatedFailureTranscriptPath' = $echoTranscript
        'IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds' = '2'
        'IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds' = '30'
    }
    $registeredF = Register-Account $accountF 'OTP Echo'
    Assert-Status $registeredF 201 'S: register against the echoing provider'
    $accountFId = ($registeredF.Body | ConvertFrom-Json).accountId
    Assert-True (Wait-OutboxFailure $accountFId) 'S: the echoing provider failed the delivery'
    $providerText = Wait-TranscriptLine
    if ($providerText -notmatch 'contained code (\d{6})') { throw 'S: the simulated provider transcript carried no code.' }
    $echoedCode = $matches[1]
    $echoedSubject = "$echoedCode is your UnicoreCRM verification code"
    Assert-True ($providerText.Contains($accountF)) 'S: the provider error really did echo the recipient'
    Assert-True ($providerText.Contains($echoedSubject)) 'S: the provider error really did echo the subject'
    Assert-True ($providerText.Contains($echoMarker)) 'S: the provider error really did echo the SMTP username'

    $recordedReason = Invoke-SqlScalar "SELECT ISNULL(LastError, '') FROM iam.EmailOutboxMessages WHERE AccountId='$accountFId';"
    Assert-True ($recordedReason -eq 'UNKNOWN_DELIVERY_FAILURE') 'S: a bounded classification was persisted instead'
    Assert-True ($boundedReasons -contains $recordedReason) 'S: the persisted reason is an application-owned value'
    foreach ($echoed in @(
            @{ Name = 'recipient'; Value = $accountF },
            @{ Name = 'code'; Value = $echoedCode },
            @{ Name = 'subject'; Value = $echoedSubject },
            @{ Name = 'SMTP username'; Value = $echoMarker },
            @{ Name = 'provider error text'; Value = $providerText })) {
        Assert-True (-not $recordedReason.Contains($echoed.Value)) "S: LastError carries no $($echoed.Name)"
        if ($echoed.Name -ne 'code') {
            Assert-True (-not (Test-AnyHostLogMatches ([regex]::Escape($echoed.Value)))) "S: no host log carries the $($echoed.Name)"
        }
    }

    # The bare code is checked against the logs of the host that ran the echoing provider, so a
    # different code legitimately emitted by an earlier Development host cannot be mistaken for a leak.
    Assert-True (-not (Test-CurrentHostLogMatches ('\b' + $echoedCode + '\b'))) 'S: no host log carries the verification code'
    Assert-True ((Get-OutboxCount $accountFId 'Pending') -eq 1) 'S: the failed message stays queued for retry'
    Assert-True ((Get-OutboxCount $accountFId 'Sent') -eq 0) 'S: nothing was reported as delivered'

    # Whole-run sweep: no row anywhere carries a reason IdentityAuth does not own.
    $reasonList = ($boundedReasons | ForEach-Object { "'" + $_ + "'" }) -join ','
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE LastError IS NOT NULL AND LastError NOT IN ($reasonList);") -eq 0) 'S: every persisted delivery reason in this run is a bounded application-owned value'

    Stop-ApiHost $hostProcess
    $hostProcess = $null
    # The transcript is the only place the fabricated provider text - including a live code for a
    # throwaway account - exists. It has served its purpose.
    Remove-Item -LiteralPath $echoTranscript -Force -ErrorAction SilentlyContinue

    # T: H-3 regression - a claim must never be shared across a batch.
    #
    # Messages are delivered sequentially, so one claim timestamp handed to a whole batch is already
    # stale by the time the later messages start. Five due messages are put in a single dispatcher
    # batch behind a sender that takes nine seconds each, which pushes the last send well past the
    # instant the first message's claim expires. Each message must hold its own fresh claim, granted
    # immediately before its own send.
    $slowSender = @{
        'IdentityAuth__EmailVerification__Sender__SimulatedSendDelayMilliseconds' = "$slowSendMilliseconds"
        'IdentityAuth__EmailVerification__Sender__TimeoutSeconds' = "$batchSenderTimeoutSeconds"
        'IdentityAuth__EmailVerification__Outbox__LeaseSeconds' = "$batchLeaseSeconds"
        'IdentityAuth__EmailVerification__Outbox__BatchSize' = '20'
        'IdentityAuth__EmailVerification__Outbox__DispatchIntervalSeconds' = '2'
        'IdentityAuth__EmailVerification__Outbox__MaxAttempts' = '20'
        'IdentityAuth__EmailVerification__ResendIntervalSeconds' = '30'
    }
    $stagingSmtp = $unreachableSmtp.Clone()
    $stagingSmtp['IdentityAuth__EmailVerification__Outbox__RetryBackoffSeconds'] = '600'
    $stagingSmtp['IdentityAuth__EmailVerification__Outbox__MaxAttempts'] = '20'
    $stagingSmtp['IdentityAuth__EmailVerification__ResendIntervalSeconds'] = '30'

    # Staging: register all five against an unreachable endpoint so every message ends up queued with
    # a future retry, then release them together. That is what puts them in one batch rather than
    # letting each commit signal deliver its own message on its own.
    $hostProcess = Start-ApiHost 'GmailSmtp' 'Development' $stagingSmtp
    $batchAccountIds = @()
    foreach ($email in $batchAccounts) {
        $registeredBatch = Register-Account $email 'OTP Batch'
        Assert-Status $registeredBatch 201 "T: register $email"
        $batchAccountIds += ($registeredBatch.Body | ConvertFrom-Json).accountId
    }
    foreach ($batchAccountId in $batchAccountIds) {
        Assert-True (Wait-OutboxFailure $batchAccountId) 'T: the staged message is queued and undelivered'
    }
    $batchFilter = ($batchAccountIds | ForEach-Object { "'" + $_ + "'" }) -join ','
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId IN ($batchFilter) AND Status='Pending';") -eq 5) 'T: five messages are staged and pending'

    Stop-ApiHost $hostProcess
    $hostProcess = Start-ApiHost 'DevelopmentLog' 'Development' $slowSender
    # Cleared before the batch starts, so that when a resend later issues nothing the only possible
    # reason is the in-flight claim, never an unelapsed cooldown.
    foreach ($batchAccountId in $batchAccountIds) { Clear-Cooldown $batchAccountId }
    Invoke-SqlCommand "UPDATE iam.EmailOutboxMessages SET NextAttemptAt = SYSDATETIMEOFFSET() WHERE AccountId IN ($batchFilter) AND Status='Pending';"

    # D is asserted from inside the watch loop, the moment the first claim appears, while that send
    # is still blocked in the sender.
    $script:inFlightAccount = ''
    $script:inFlightMessage = ''
    $script:inFlightChecks = @()
    $onFirstClaim = {
        param($claim)
        $script:inFlightAccount = $claim.AccountId
        $script:inFlightMessage = $claim.MessageId
        $challengesBefore = Get-ChallengeCount $claim.AccountId
        $messagesBefore = Get-OutboxCount $claim.AccountId
        $resendDuringSend = Request-Verification ($batchAccounts[[array]::IndexOf($batchAccountIds, $claim.AccountId)])
        $script:inFlightChecks = @(
            @{ Name = 'T: a resend during an in-flight send returns the uniform acceptance'; Ok = ($resendDuringSend.Status -eq 202) },
            @{ Name = 'T: a resend during an in-flight send issues no new challenge'; Ok = ((Get-ChallengeCount $claim.AccountId) -eq $challengesBefore) },
            @{ Name = 'T: a resend during an in-flight send stages no new message'; Ok = ((Get-OutboxCount $claim.AccountId) -eq $messagesBefore) },
            @{ Name = 'T: the in-flight challenge was not superseded'; Ok = ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE AccountId='$($claim.AccountId)' AND SupersededAt IS NOT NULL;") -eq 0) },
            @{ Name = 'T: the in-flight message was not cancelled'; Ok = ((Get-MessageScalar $claim.MessageId 'Status') -eq 'Pending') }
        )
    }

    $claims = Watch-Claims $batchFilter 75 $onFirstClaim
    foreach ($check in $script:inFlightChecks) { Assert-True ([bool] $check.Ok) $check.Name }
    Assert-True ($script:inFlightAccount -ne '') 'T: a claim was observed while its send was still running'

    # A: every message held a claim of its own.
    Assert-True ($claims.Count -eq 5) 'T: all five messages were individually claimed'
    $claimValues = @($claims.Values)
    Assert-True (@($claimValues | ForEach-Object { $_.LeasedUntil } | Sort-Object -Unique).Count -eq 5) 'T: each message received its own distinct claim expiry'

    # B and C: no message began its send under an expired or unbounded claim.
    foreach ($claim in $claimValues) {
        Assert-True ($claim.LeaseSeconds -eq $expectedEffectiveLease) 'T: the claim granted is exactly the effective lease'
        Assert-True ($claim.RemainingAtSample -gt 0) 'T: the send deadline was still in the future while the send was running'
    }
    Assert-True ($expectedEffectiveLease -ge ($batchSenderTimeoutSeconds + 30)) 'T: the effective lease is at least the sender timeout plus the safety margin'

    # The point of the whole section: the last message started its send after the first message's
    # claim had already expired. Under a batch-wide claim that send would have been running with no
    # live claim protecting it, which is exactly what let a resend revoke a code already on its way.
    $ordered = @($claimValues | Sort-Object { [DateTimeOffset]::Parse($_.ClaimedAt) })
    $firstClaim = $ordered[0]
    $lastClaim = $ordered[$ordered.Count - 1]
    $spread = ([DateTimeOffset]::Parse($lastClaim.ClaimedAt) - [DateTimeOffset]::Parse($firstClaim.ClaimedAt)).TotalSeconds
    Assert-True ($spread -ge (3 * $slowSendMilliseconds / 1000)) 'T: the batch really was delivered sequentially over several sends'
    Assert-True ([DateTimeOffset]::Parse($lastClaim.ClaimedAt) -gt [DateTimeOffset]::Parse($firstClaim.LeasedUntil)) 'T: the last send began after the first message claim had expired'
    Assert-True ([DateTimeOffset]::Parse($lastClaim.LeasedUntil) -gt [DateTimeOffset]::Parse($lastClaim.ClaimedAt)) 'T: the last send still held a live claim of its own'

    # E: the sends resolved normally, and a resend afterwards behaves normally again - which proves
    # the refusal above came from the in-flight claim and not from a cooldown.
    foreach ($batchAccountId in $batchAccountIds) {
        Assert-True (Wait-OutboxSettled $batchAccountId 1) 'T: the claimed message was delivered'
    }
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId IN ($batchFilter) AND Status='Sent' AND ProtectedCode IS NULL AND LastError IS NULL;") -eq 5) 'T: every delivered message recorded a clean outcome'
    Assert-True ([int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE AccountId IN ($batchFilter) AND LeasedUntil IS NOT NULL;") -eq 0) 'T: no claim outlived its send'
    $inFlightIndex = [array]::IndexOf($batchAccountIds, $script:inFlightAccount)
    $challengesBeforeSettledResend = Get-ChallengeCount $script:inFlightAccount
    Assert-Status (Request-Verification $batchAccounts[$inFlightIndex]) 202 'T: a resend after the send resolved is accepted'
    Assert-True ((Get-ChallengeCount $script:inFlightAccount) -eq ($challengesBeforeSettledResend + 1)) 'T: the same account, same cooldown state, does issue once nothing is in flight'

    # U: concurrency token - a finished send must not stamp its outcome over a newer committed state.
    #
    # The message is retired by another writer while its send is still running. When the send
    # completes, the outcome write is built on a stale row image and must fail rather than win.
    $registeredH = Register-Account $accountH 'OTP Concurrency'
    Assert-Status $registeredH 201 'U: register the concurrency account'
    $accountHId = ($registeredH.Body | ConvertFrom-Json).accountId
    $messageH = ''
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        $messageH = Invoke-SqlScalar "SELECT ISNULL(MAX(MessageId), '') FROM iam.EmailOutboxMessages WHERE AccountId='$accountHId' AND LeasedUntil > SYSDATETIMEOFFSET();"
        if ($messageH -ne '') { break }
        Start-Sleep -Milliseconds 250
    }
    Assert-True ($messageH -ne '') 'U: the message was claimed and its send is running'
    # The column is a SQL Server `timestamp`, which style-1 CONVERT renders as an empty string unless
    # it is cast to varbinary first.
    $tokenBefore = Invoke-SqlScalar "SELECT CONVERT(varchar(50), CONVERT(varbinary(8), RowVersion), 1) FROM iam.EmailOutboxMessages WHERE MessageId='$messageH';"
    # Another valid writer retires the row mid-send. SQL Server advances the rowversion.
    Invoke-SqlCommand "UPDATE iam.EmailOutboxMessages SET Status='Cancelled', ProtectedCode=NULL, LeasedUntil=NULL, LastError='CHALLENGE_SUPERSEDED' WHERE MessageId='$messageH';"
    $tokenAfter = Invoke-SqlScalar "SELECT CONVERT(varchar(50), CONVERT(varbinary(8), RowVersion), 1) FROM iam.EmailOutboxMessages WHERE MessageId='$messageH';"
    Assert-True ($tokenBefore.Length -gt 2) 'U: the concurrency token is readable'
    Assert-True ($tokenBefore -ne $tokenAfter) 'U: the concurrent write advanced the concurrency token'

    # Let the in-flight send finish and try to record its outcome against the stale image.
    Start-Sleep -Milliseconds ($slowSendMilliseconds + 6000)
    Assert-True ((Get-MessageScalar $messageH 'Status') -eq 'Cancelled') 'U: the newer terminal state was preserved'
    Assert-True ((Get-MessageScalar $messageH 'SentAt') -eq '') 'U: the finished send did not stamp itself as sent'
    Assert-True ((Get-MessageScalar $messageH 'LastError') -eq 'CHALLENGE_SUPERSEDED') 'U: the newer reason code was preserved'
    Assert-True ((Get-MessageScalar $messageH 'ProtectedCode') -eq '') 'U: the retired message holds no payload'
    Assert-True (Test-CurrentHostLogMatches ([regex]::Escape($messageH) + ' could not be recorded as sent')) 'U: the conflict was reported rather than silently swallowed'
    Assert-True (Test-CurrentHostLogMatches 'another writer committed Cancelled first') 'U: the log names the state that was preserved'
    Assert-True ((Get-OutboxCount $accountHId 'Sent') -eq 0) 'U: nothing was recorded as delivered for that account'

    Stop-ApiHost $hostProcess
    $hostProcess = $null

    $totals = @{
        Accounts = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.Accounts;')
        Challenges = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges;')
        Outstanding = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE ConsumedAt IS NULL AND SupersededAt IS NULL;')
        Consumed = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE ConsumedAt IS NOT NULL;')
        ActiveAccounts = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Status='Active';")
        Sessions = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.Sessions;')
        OutboxMessages = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailOutboxMessages;')
        OutboxSent = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE Status='Sent';")
        OutboxCancelled = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE Status='Cancelled';")
        OutboxCancelledRetainingPayload = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE Status='Cancelled' AND ProtectedCode IS NOT NULL;")
        OutboxClaimsOutliving = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE Status <> 'Pending' AND LeasedUntil IS NOT NULL;")
        OutboxRetainingPayload = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailOutboxMessages WHERE ProtectedCode IS NOT NULL;')
    }

    [pscustomobject] @{
        Status = 'PASS'
        Database = $DatabaseName
        PrimaryAccount = $accountAId
        RestartAccount = $accountBId
        Totals = $totals
        Checks = $checks
    } | ConvertTo-Json -Depth 6
}
finally {
    Stop-ApiHost $hostProcess
    $client.Dispose()
    Remove-Item -LiteralPath $echoTranscript -Force -ErrorAction SilentlyContinue
}
