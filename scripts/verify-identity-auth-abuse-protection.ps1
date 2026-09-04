<#
.SYNOPSIS
    Verifies origin- and subject-based throttling for the externally reachable IdentityAuth surface.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_]{1,128}$')]
    [string] $DatabaseName,

    [string] $SqlServer = '(localdb)\MSSQLLocalDB',

    [int] $Port = 5391,

    [switch] $KeepDatabase
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostDll = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll')).Path
$contentRoot = (Resolve-Path (Join-Path $solutionRoot 'src/UnicoreCRM.ApiHost')).Path
$baseUrl = "http://127.0.0.1:$Port"
$connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True"
$bootstrapEmail = 'rate-limit.active@example.test'
$bootstrapPassword = 'Rate-Limit-Active!2026'
$registrationEmail = 'rate-limit.registration@example.test'
$pendingEmail = 'rate-limit.pending@example.test'
$unknownEmail = 'rate-limit.unknown@example.test'
$invalidPassword = 'Definitely-Not-The-Password!2026'
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$pepper = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$temporaryDirectory = New-Item -ItemType Directory -Path ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'unicore-identity-abuse-' + [Guid]::NewGuid().ToString('N')))
$standardOutput = Join-Path $temporaryDirectory 'host.out.log'
$standardError = Join-Path $temporaryDirectory 'host.err.log'
$clientHandler = [System.Net.Http.HttpClientHandler]::new()
$clientHandler.UseCookies = $false
$client = [System.Net.Http.HttpClient]::new($clientHandler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$checks = [System.Collections.Generic.List[string]]::new()
$hostProcess = $null

function Assert-True([bool] $condition, [string] $name) {
    if (-not $condition) { throw "Assertion failed: $name" }
    $checks.Add("$name=PASS")
}

function New-Headers {
    return @{
        'X-Request-Id' = 'req-' + [Guid]::NewGuid().ToString('N')
        'X-Correlation-Id' = 'cor-' + [Guid]::NewGuid().ToString('N')
        'Idempotency-Key' = 'idem-' + [Guid]::NewGuid().ToString('N')
    }
}

function Invoke-Api(
    [string] $path,
    [object] $body,
    [hashtable] $headers = $null,
    [string] $cookie = $null) {
    $message = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$baseUrl$path")
    $message.Content = [System.Net.Http.StringContent]::new(($body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json')
    if ($null -ne $headers) {
        foreach ($entry in $headers.GetEnumerator()) {
            $null = $message.Headers.TryAddWithoutValidation([string] $entry.Key, [string] $entry.Value)
        }
    }
    if (-not [string]::IsNullOrEmpty($cookie)) {
        $null = $message.Headers.TryAddWithoutValidation('Cookie', $cookie)
    }

    $response = $client.SendAsync($message).GetAwaiter().GetResult()
    try {
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $retryAfter = $null
        $setCookie = $null
        $values = $null
        if ($response.Headers.TryGetValues('Retry-After', [ref] $values)) {
            $retryAfter = @($values)[0]
        }
        $values = $null
        if ($response.Headers.TryGetValues('Set-Cookie', [ref] $values)) {
            $setCookie = @($values)[0]
        }
        return [pscustomobject]@{
            Status = [int] $response.StatusCode
            Body = $text
            ContentType = $response.Content.Headers.ContentType.MediaType
            RetryAfter = $retryAfter
            SetCookie = $setCookie
        }
    }
    finally {
        $response.Dispose()
        $message.Dispose()
    }
}

function Assert-RateLimited([object] $response, [string] $name, [string[]] $secrets = @()) {
    Assert-True ($response.Status -eq 429) "$name status is 429"
    Assert-True ($response.ContentType -eq 'application/problem+json') "$name content type is problem+json"
    $problem = $response.Body | ConvertFrom-Json
    Assert-True ($problem.code -eq 'RATE_LIMITED') "$name uses RATE_LIMITED"
    Assert-True ($problem.status -eq 429) "$name problem status is 429"
    Assert-True ($problem.retryable -eq $true) "$name is retryable"
    $retrySeconds = 0
    Assert-True ([int]::TryParse([string] $response.RetryAfter, [ref] $retrySeconds) -and $retrySeconds -gt 0) "$name has usable Retry-After"
    foreach ($secret in $secrets) {
        Assert-True (-not $response.Body.Contains($secret, [StringComparison]::Ordinal)) "$name does not echo sensitive input"
    }
    return $problem
}

function Assert-SameThrottleShape([object] $left, [object] $right, [string] $name) {
    foreach ($property in @('type', 'title', 'status', 'code', 'retryable')) {
        Assert-True ($left.$property -eq $right.$property) "$name same $property"
    }
}

function Initialize-Database {
    & sqlcmd -S $SqlServer -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated verification database.' }
    $env:ConnectionStrings__UnicoreCRM = $connection
    & dotnet ef database update --project (Join-Path $solutionRoot 'src/UnicoreCRM.Platform') --context IdentityAuthDbContext --no-build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not apply IdentityAuth migrations.' }
    $checks.Add('Isolated IdentityAuth database migrated=PASS')
}

function Start-Host {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__UnicoreCRM = $connection
    $env:Development__ApplyMigrations = 'false'
    $env:UNICORE_DEV_SEED_ENABLED = 'false'
    $env:IdentityAuth__Jwt__SigningKey = $jwtKey
    $env:IdentityAuth__RefreshTokenPepper = $pepper
    $env:IdentityAuth__EmailVerification__Sender__Kind = 'DevelopmentLog'
    $env:IdentityAuth__DevelopmentBootstrap__Enabled = 'true'
    $env:IdentityAuth__DevelopmentBootstrap__ApplyMigrations = 'false'
    $env:IdentityAuth__DevelopmentBootstrap__Email = $bootstrapEmail
    $env:IdentityAuth__DevelopmentBootstrap__Password = $bootstrapPassword
    $env:IdentityAuth__DevelopmentBootstrap__DisplayName = 'Rate Limit Active Fixture'
    $env:Workspace__DevelopmentBootstrap__Enabled = 'false'
    $env:AccessControl__DevelopmentBootstrap__Enabled = 'false'
    $env:Integrations__DevelopmentBootstrap__Enabled = 'false'
    $env:Workflows__InitialWorkspaceProvisioning__ResumeEnabled = 'false'
    $env:AI__Provider__Kind = 'DevelopmentDeterministic'

    foreach ($name in @('Registration', 'VerificationRequest', 'VerificationSubmission', 'PasswordSignIn', 'SessionRefresh')) {
        Set-Item "Env:IdentityAuth__AbuseProtection__$($name)__OriginPermitLimit" '100'
        Set-Item "Env:IdentityAuth__AbuseProtection__$($name)__WindowSeconds" '60'
    }
    $env:IdentityAuth__AbuseProtection__Registration__OriginPermitLimit = '4'
    $env:IdentityAuth__AbuseProtection__Registration__SubjectPermitLimit = '2'
    $env:IdentityAuth__AbuseProtection__VerificationRequest__SubjectPermitLimit = '1'
    $env:IdentityAuth__AbuseProtection__VerificationSubmission__SubjectPermitLimit = '1'
    $env:IdentityAuth__AbuseProtection__PasswordSignIn__SubjectPermitLimit = '2'
    $env:IdentityAuth__AbuseProtection__SessionRefresh__SubjectPermitLimit = '1'
    $env:IdentityAuth__AbuseProtection__SessionRefresh__WindowSeconds = '2'

    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError -PassThru
    for ($attempt = 0; $attempt -lt 180; $attempt++) {
        if ($process.HasExited) {
            throw "ApiHost exited during startup: $((Get-Content -LiteralPath $standardError -Raw)) $((Get-Content -LiteralPath $standardOutput -Raw))"
        }
        try {
            $probe = $client.GetAsync("$baseUrl/auth/session").GetAwaiter().GetResult()
            try {
                if ([int] $probe.StatusCode -eq 401) { return $process }
            }
            finally { $probe.Dispose() }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    throw 'ApiHost did not become ready.'
}

try {
    Initialize-Database
    $hostProcess = Start-Host

    $nullEmail = Invoke-Api '/auth/sessions' @{ email = $null; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    Assert-True ($nullEmail.Status -eq 422) 'Null email keeps validation behavior instead of failing in the limiter'

    $registration = Invoke-Api '/auth/accounts' @{ email = $registrationEmail; password = 'Registration-Test!2026'; displayName = 'Registration Test' } (New-Headers)
    Assert-True ($registration.Status -eq 201) 'Low-rate registration succeeds'
    $duplicateRegistration = Invoke-Api '/auth/accounts' @{ email = $registrationEmail; password = 'Registration-Test!2026'; displayName = 'Registration Test' } (New-Headers)
    Assert-True ($duplicateRegistration.Status -eq 409) 'Existing registration contract remains intact before limit'
    $registrationLimited = Invoke-Api '/auth/accounts' @{ email = $registrationEmail; password = 'Registration-Test!2026'; displayName = 'Registration Test' } (New-Headers)
    $null = Assert-RateLimited $registrationLimited 'Registration subject limit' @($registrationEmail, 'Registration-Test!2026')

    $secondRegistration = Invoke-Api '/auth/accounts' @{ email = $pendingEmail; password = 'Pending-Test!2026'; displayName = 'Pending Test' } (New-Headers)
    Assert-True ($secondRegistration.Status -eq 201) 'Distinct low-rate registration succeeds'
    $originLimited = Invoke-Api '/auth/accounts' @{ email = 'rate-limit.origin@example.test'; password = 'Origin-Test!2026'; displayName = 'Origin Test' } (New-Headers)
    $null = Assert-RateLimited $originLimited 'Registration origin limit' @('rate-limit.origin@example.test', 'Origin-Test!2026')
    $spoofedOriginHeaders = New-Headers
    $spoofedOriginHeaders['X-Forwarded-For'] = '203.0.113.77'
    $spoofedOriginLimited = Invoke-Api '/auth/accounts' @{ email = 'rate-limit.spoofed-origin@example.test'; password = 'Origin-Test!2026'; displayName = 'Origin Test' } $spoofedOriginHeaders
    $null = Assert-RateLimited $spoofedOriginLimited 'Caller-supplied forwarding header cannot bypass origin limit' @('rate-limit.spoofed-origin@example.test', 'Origin-Test!2026')

    $knownVerificationRequest = Invoke-Api '/auth/email-verification-requests' @{ email = $bootstrapEmail } (New-Headers)
    $unknownVerificationRequest = Invoke-Api '/auth/email-verification-requests' @{ email = $unknownEmail } (New-Headers)
    Assert-True ($knownVerificationRequest.Status -eq 202) 'Known-account verification request remains uniformly accepted'
    Assert-True ($unknownVerificationRequest.Status -eq 202) 'Unknown-account verification request remains uniformly accepted'
    $knownVerificationRequestLimited = Invoke-Api '/auth/email-verification-requests' @{ email = $bootstrapEmail } (New-Headers)
    $unknownVerificationRequestLimited = Invoke-Api '/auth/email-verification-requests' @{ email = $unknownEmail } (New-Headers)
    $knownRequestProblem = Assert-RateLimited $knownVerificationRequestLimited 'Known verification-request subject limit' @($bootstrapEmail)
    $unknownRequestProblem = Assert-RateLimited $unknownVerificationRequestLimited 'Unknown verification-request subject limit' @($unknownEmail)
    Assert-SameThrottleShape $knownRequestProblem $unknownRequestProblem 'Verification-request enumeration resistance'
    $unknownUpperCaseLimited = Invoke-Api '/auth/email-verification-requests' @{ email = $unknownEmail.ToUpperInvariant() } (New-Headers)
    $null = Assert-RateLimited $unknownUpperCaseLimited 'Email casing cannot bypass subject limit' @($unknownEmail.ToUpperInvariant())

    $knownVerification = Invoke-Api '/auth/email-verifications' @{ email = $pendingEmail; code = '000000' } (New-Headers)
    $unknownVerification = Invoke-Api '/auth/email-verifications' @{ email = $unknownEmail; code = '000000' } (New-Headers)
    Assert-True ($knownVerification.Status -eq 401) 'Wrong OTP remains rejected'
    Assert-True ($unknownVerification.Status -eq 401) 'Unknown-account OTP remains rejected'
    Assert-True (($knownVerification.Body | ConvertFrom-Json).code -eq 'TOKEN_INVALID') 'Wrong OTP keeps generic TOKEN_INVALID'
    Assert-True (($unknownVerification.Body | ConvertFrom-Json).code -eq 'TOKEN_INVALID') 'Unknown-account OTP keeps generic TOKEN_INVALID'
    $knownVerificationLimited = Invoke-Api '/auth/email-verifications' @{ email = $pendingEmail; code = '000000' } (New-Headers)
    $unknownVerificationLimited = Invoke-Api '/auth/email-verifications' @{ email = $unknownEmail; code = '000000' } (New-Headers)
    $knownVerificationProblem = Assert-RateLimited $knownVerificationLimited 'Known OTP-verification subject limit' @($pendingEmail, '000000')
    $unknownVerificationProblem = Assert-RateLimited $unknownVerificationLimited 'Unknown OTP-verification subject limit' @($unknownEmail, '000000')
    Assert-SameThrottleShape $knownVerificationProblem $unknownVerificationProblem 'OTP-verification enumeration resistance'

    $signIn = Invoke-Api '/auth/sessions' @{ email = $bootstrapEmail; password = $bootstrapPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    Assert-True ($signIn.Status -eq 200) 'Low-rate password sign-in succeeds'
    Assert-True (-not [string]::IsNullOrEmpty($signIn.SetCookie)) 'Successful sign-in rotates refresh cookie'
    $refreshCookie = ($signIn.SetCookie -split ';', 2)[0]

    $knownInvalidSignIn = Invoke-Api '/auth/sessions' @{ email = $bootstrapEmail; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    $unknownInvalidSignIn = Invoke-Api '/auth/sessions' @{ email = $unknownEmail; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    Assert-True ($knownInvalidSignIn.Status -eq 401) 'Known-account invalid password remains rejected'
    Assert-True ($unknownInvalidSignIn.Status -eq 401) 'Unknown-account invalid password remains rejected'
    Assert-True (($knownInvalidSignIn.Body | ConvertFrom-Json).code -eq 'INVALID_CREDENTIALS') 'Known-account failure remains generic'
    Assert-True (($unknownInvalidSignIn.Body | ConvertFrom-Json).code -eq 'INVALID_CREDENTIALS') 'Unknown-account failure remains generic'
    $unknownInvalidSignInSecond = Invoke-Api '/auth/sessions' @{ email = $unknownEmail; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    Assert-True ($unknownInvalidSignInSecond.Status -eq 401) 'Unknown-account second low-rate failure remains generic'
    $knownSignInLimited = Invoke-Api '/auth/sessions' @{ email = $bootstrapEmail; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    $unknownSignInLimited = Invoke-Api '/auth/sessions' @{ email = $unknownEmail; password = $invalidPassword; deviceLabel = 'Abuse verifier' } (New-Headers)
    $knownSignInProblem = Assert-RateLimited $knownSignInLimited 'Known sign-in subject limit' @($bootstrapEmail, $invalidPassword)
    $unknownSignInProblem = Assert-RateLimited $unknownSignInLimited 'Unknown sign-in subject limit' @($unknownEmail, $invalidPassword)
    Assert-SameThrottleShape $knownSignInProblem $unknownSignInProblem 'Password sign-in enumeration resistance'

    $refresh = Invoke-Api '/auth/session/refresh' @{} (New-Headers) $refreshCookie
    Assert-True ($refresh.Status -eq 200) 'Low-rate session refresh succeeds'
    Assert-True (-not [string]::IsNullOrEmpty($refresh.SetCookie)) 'Successful refresh rotates refresh cookie'
    $rotatedRefreshCookie = ($refresh.SetCookie -split ';', 2)[0]
    $refreshLimited = Invoke-Api '/auth/session/refresh' @{} (New-Headers) $rotatedRefreshCookie
    $null = Assert-RateLimited $refreshLimited 'Session refresh subject limit' @($rotatedRefreshCookie)
    Start-Sleep -Seconds ([int] $refreshLimited.RetryAfter + 1)
    $refreshAfterWindow = Invoke-Api '/auth/session/refresh' @{} (New-Headers) $rotatedRefreshCookie
    Assert-True ($refreshAfterWindow.Status -eq 200) 'Retry-After delay allows a valid refresh after the window'

    $invalidRefreshCookie = '__Host-unicore-refresh=ses_invalid.invalid-secret-value'
    $invalidRefresh = Invoke-Api '/auth/session/refresh' @{} (New-Headers) $invalidRefreshCookie
    Assert-True ($invalidRefresh.Status -eq 401) 'Invalid refresh token remains safely rejected'
    Assert-True (($invalidRefresh.Body | ConvertFrom-Json).code -eq 'TOKEN_INVALID') 'Invalid refresh token keeps TOKEN_INVALID'
    $invalidRefreshLimited = Invoke-Api '/auth/session/refresh' @{} (New-Headers) $invalidRefreshCookie
    $null = Assert-RateLimited $invalidRefreshLimited 'Invalid refresh subject limit' @('invalid-secret-value')

    $logs = (Get-Content -LiteralPath $standardOutput -Raw) + (Get-Content -LiteralPath $standardError -Raw)
    foreach ($secret in @($bootstrapPassword, $invalidPassword, 'Registration-Test!2026', 'Pending-Test!2026', 'invalid-secret-value')) {
        Assert-True (-not $logs.Contains($secret, [StringComparison]::Ordinal)) 'Host logs contain no tested password, OTP, or refresh secret'
    }

    Stop-Process -Id $hostProcess.Id -Force
    $hostProcess.WaitForExit()
    $hostProcess = $null
    $env:IdentityAuth__AbuseProtection__Registration__OriginPermitLimit = '0'
    $invalidOutput = Join-Path $temporaryDirectory 'invalid-config.out.log'
    $invalidError = Join-Path $temporaryDirectory 'invalid-config.err.log'
    $hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @($hostDll) -WorkingDirectory $contentRoot -WindowStyle Hidden -RedirectStandardOutput $invalidOutput -RedirectStandardError $invalidError -PassThru
    $invalidHostExited = $hostProcess.WaitForExit(10000)
    Assert-True $invalidHostExited 'Invalid abuse-protection configuration fails host startup'
    if ($invalidHostExited) {
        $invalidLogs = (Get-Content -LiteralPath $invalidOutput -Raw) + (Get-Content -LiteralPath $invalidError -Raw)
        Assert-True ($invalidLogs.Contains('Every IdentityAuth abuse-protection limit and window must be within its supported range.', [StringComparison]::Ordinal)) 'Invalid configuration reports the bounded validation error'
        $hostProcess = $null
    }

    Write-Output "IdentityAuth abuse-protection verification passed: $($checks.Count) checks."
    $checks
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit()
    }
    $client.Dispose()
    $clientHandler.Dispose()

    foreach ($name in @(
        'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ASPNETCORE_URLS','ConnectionStrings__UnicoreCRM',
        'Development__ApplyMigrations','UNICORE_DEV_SEED_ENABLED','IdentityAuth__Jwt__SigningKey',
        'IdentityAuth__RefreshTokenPepper','IdentityAuth__EmailVerification__Sender__Kind',
        'IdentityAuth__DevelopmentBootstrap__Enabled','IdentityAuth__DevelopmentBootstrap__ApplyMigrations',
        'IdentityAuth__DevelopmentBootstrap__Email','IdentityAuth__DevelopmentBootstrap__Password',
        'IdentityAuth__DevelopmentBootstrap__DisplayName','Workspace__DevelopmentBootstrap__Enabled',
        'AccessControl__DevelopmentBootstrap__Enabled','Integrations__DevelopmentBootstrap__Enabled',
        'Workflows__InitialWorkspaceProvisioning__ResumeEnabled','AI__Provider__Kind')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    foreach ($operation in @('Registration', 'VerificationRequest', 'VerificationSubmission', 'PasswordSignIn', 'SessionRefresh')) {
        foreach ($setting in @('OriginPermitLimit', 'SubjectPermitLimit', 'WindowSeconds')) {
            Remove-Item "Env:IdentityAuth__AbuseProtection__$($operation)__$($setting)" -ErrorAction SilentlyContinue
        }
    }

    if (-not $KeepDatabase) {
        & sqlcmd -S $SqlServer -d master -b -Q "IF DB_ID('$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END;" | Out-Null
    }
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
