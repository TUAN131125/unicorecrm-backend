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
$maxAttempts = 3
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-email-otp-' + [Guid]::NewGuid().ToString('N')))
$hostDll = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll").Path
$contentRoot = (Resolve-Path "$PSScriptRoot/../src/UnicoreCRM.ApiHost").Path
$solutionRoot = (Resolve-Path "$PSScriptRoot/..").Path
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()
$script:hostOut = $null

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

function Start-ApiHost([string] $senderKind = 'DevelopmentLog', [string] $environment = 'Development') {
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

    $standardOut = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.out.log')
    $standardError = Join-Path $temporaryDirectory ('host-' + [Guid]::NewGuid().ToString('N') + '.err.log')
    $script:hostOut = $standardOut
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

    # F: once the cooldown has elapsed a resend supersedes the previous code.
    Clear-Cooldown $accountAId
    Assert-Status (Request-Verification $accountA) 202 'F: resend after cooldown'
    Assert-True ((Get-ChallengeCount $accountAId) -eq 2) 'F: resend issued a second challenge'
    Assert-True ((Get-OutstandingCount $accountAId) -eq 1) 'F: exactly one challenge stays outstanding'
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

    $totals = @{
        Accounts = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.Accounts;')
        Challenges = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges;')
        Outstanding = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE ConsumedAt IS NULL AND SupersededAt IS NULL;')
        Consumed = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.EmailVerificationChallenges WHERE ConsumedAt IS NOT NULL;')
        ActiveAccounts = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM iam.Accounts WHERE Status='Active';")
        Sessions = [int](Invoke-SqlScalar 'SELECT COUNT(*) FROM iam.Sessions;')
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
}
