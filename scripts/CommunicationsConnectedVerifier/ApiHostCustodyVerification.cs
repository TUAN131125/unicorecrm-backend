using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Communications.Infrastructure.Telegram;

internal static class ApiHostCustodyVerification
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static async Task Run(string root, string artifacts, string database, string connection, string signingKey,
        IReadOnlyDictionary<string, string> tokens, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        var owned = Path.GetFullPath(Path.Combine(artifacts, "apihost-custody", database));
        Directory.CreateDirectory(owned);
        Restrict(owned);
        var ring = Path.Combine(owned, "ring"); Directory.CreateDirectory(ring);
        var backup = Path.Combine(owned, "backup"); Directory.CreateDirectory(backup);
        var tlsPath = Path.Combine(owned, "tls.pfx");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var tls = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        await File.WriteAllBytesAsync(tlsPath, tls.Export(X509ContentType.Pfx, password));
        Process? process = null;
        Task<string>? output = null, errors = null;
        HttpClient? http = null;
        var ids = new List<int>();
        var logs = new StringBuilder();
        var certificates = new List<X509Certificate2>();
        const string dummy = "fixture-apihost-persistent-one";
        const string rotated = "fixture-apihost-persistent-rotated";
        ServiceProvider KeyServices(string path)
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddDataProtection().SetApplicationName("UniCoreCRM.Communications.Credentials")
                .PersistKeysToFileSystem(new DirectoryInfo(path)).ProtectKeysWithDpapi().DisableAutomaticKeyGeneration();
            return services.BuildServiceProvider();
        }
        using (var keys = KeyServices(ring))
            keys.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));
        Dictionary<string, string?> Config(string path, string mode = "WindowsCurrentUserDpapi") => new()
        {
            ["Communications:Telegram:Custody:Protection"] = mode,
            ["Communications:Telegram:Custody:KeyRingPath"] = path
        };
        PersistentTelegramCustody Custody(string path, string mode = "WindowsCurrentUserDpapi") =>
            PersistentTelegramCustody.Create(new ConfigurationBuilder().AddInMemoryCollection(Config(path, mode)).Build());
        async Task Stop()
        {
            http?.Dispose(); http = null;
            if (process is null) return;
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            logs.Append(await output!); logs.Append(await errors!);
            process.Dispose(); process = null;
        }
        async Task Start(string? path, string mode = "WindowsCurrentUserDpapi", bool migrate = false, string? thumbprint = null)
        {
            await Stop();
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = owned, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Path.Combine(root, "backend/src/UnicoreCRM.ApiHost/bin/Debug/net10.0/UnicoreCRM.ApiHost.dll"));
            if (migrate) info.ArgumentList.Add("--migrate");
            // Isolated content root and non-Development environment never load Owner's local configuration.
            foreach (var name in info.Environment.Keys.Where(n => n.StartsWith("Communications__", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("IdentityAuth__", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(name);
            foreach (var pair in new Dictionary<string, string?> {
                ["DOTNET_ENVIRONMENT"] = "SecurityVerification", ["ASPNETCORE_ENVIRONMENT"] = "SecurityVerification",
                ["ASPNETCORE_URLS"] = $"https://127.0.0.1:{port}",
                ["ConnectionStrings__UnicoreCRM"] = connection,
                ["IdentityAuth__Jwt__SigningKey"] = signingKey, ["IdentityAuth__RefreshTokenPepper"] = signingKey,
                ["IdentityAuth__Jwt__Issuer"] = "Com1Fixture", ["IdentityAuth__Jwt__Audience"] = "Com1Fixture",
                ["IdentityAuth__EmailVerification__Sender__Kind"] = "Unavailable",
                ["AI__DataProtection__KeyRingPath"] = Path.Combine(owned, "ai-ring"),
                ["Communications__Telegram__Enabled"] = "true",
                ["Communications__Telegram__Custody__Protection"] = path is null ? "Disabled" : mode,
                ["Communications__Telegram__Custody__KeyRingPath"] = path ?? "",
                ["Communications__Telegram__Custody__CertificateThumbprint"] = thumbprint ?? "",
                ["Communications__Telegram__Custody__CertificateStoreLocation"] = "CurrentUser",
                ["Kestrel__Certificates__Default__Path"] = tlsPath, ["Kestrel__Certificates__Default__Password"] = password,
                ["Logging__LogLevel__Default"] = "Warning", ["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "None"
            }) info.Environment[pair.Key] = pair.Value;
            process = Process.Start(info) ?? throw new InvalidOperationException("ApiHost start failed"); ids.Add(process.Id);
            output = process.StandardOutput.ReadToEndAsync(); errors = process.StandardError.ReadToEndAsync();
            if (migrate)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
                check(process.ExitCode == 0, "normal ApiHost maintenance applies owner migrations to task DB"); await Stop(); return;
            }
            http = new HttpClient(new HttpClientHandler { UseCookies = false,
                // Exact task TLS certificate only, never accept arbitrary certificates.
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == tls.GetCertHashString() })
                { BaseAddress = new Uri($"https://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            http.DefaultRequestHeaders.Add("X-Correlation-Id", "apihost-startup-probe");
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (process.HasExited) throw new InvalidOperationException("ApiHost exited before readiness");
                try { using var response = await http.GetAsync("/communications/channel-accounts"); if (response.StatusCode == HttpStatusCode.Unauthorized) return; logs.AppendLine("Startup probe status " + (int)response.StatusCode); }
                catch (HttpRequestException e) { if (attempt == 99) logs.AppendLine("Startup transport classification " + e.HttpRequestError); }
                await Task.Delay(100);
            }
            throw new InvalidOperationException("ApiHost startup timeout");
        }
        async Task<(int Status, JsonElement Body)> Send(string method, string path, object? body = null,
            string? actor = "admin", string workspace = "ws_com1_a", string? key = null)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path);
            if (actor is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            req.Headers.Add("X-Workspace-Id", workspace); req.Headers.Add("X-Correlation-Id", "apihost-custody-offline");
            req.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            if (body is not null) { req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"); req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N")); }
            using var res = await http!.SendAsync(req); var text = await res.Content.ReadAsStringAsync();
            check(!text.Contains(dummy) && !text.Contains(rotated) && !text.Contains("Ciphertext", StringComparison.OrdinalIgnoreCase), "normal ApiHost API contains no credential material");
            return ((int)res.StatusCode, JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement.Clone());
        }
        string PathFor(Guid id, string suffix = "connection") => $"/communications/channel-accounts/{id}/{suffix}";
        async Task<Guid> Create(string name, string actor = "admin", string workspace = "ws_com1_a")
        {
            var result = await Send("POST", "/communications/channel-accounts", new { providerKey = "telegram", displayName = name }, actor, workspace);
            check(result.Status == 201, "normal ApiHost channel creation authorized"); return result.Body.GetProperty("account").GetProperty("id").GetGuid();
        }
        try
        {
            using (var custody = Custody(ring)) check(custody.Ready && !custody.SyntheticReady, "normal DPAPI custody healthy without synthetic admission");
            await Start(null, migrate: true);
            await Start(null);
            var a1 = await Create("ApiHost A1"); var a2 = await Create("ApiHost A2"); var b1 = await Create("ApiHost B1", "a", "ws_com1_b");
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "disabled custody starts normal ApiHost with safe unavailable status");
            check((await Send("PUT", PathFor(a1, "credentials"), new { botToken = dummy, expectedVersion = 1 })).Status == 503, "missing custody denies credential writes");
            await Start(ring);
            check((await Send("PUT", PathFor(a1, "credentials"), new { botToken = dummy, expectedVersion = 1 }, key: "apihost-persist")).Status == 200, "normal ApiHost persists protected dummy through HTTPS API");
            check((await Send("PUT", PathFor(a2, "credentials"), new { botToken = "fixture-apihost-two", expectedVersion = 1 })).Status == 200, "normal ApiHost A2 independent credential");
            check((await Send("PUT", PathFor(b1, "credentials"), new { botToken = "fixture-apihost-three", expectedVersion = 1 }, "a", "ws_com1_b")).Status == 200, "normal ApiHost B1 independent credential");
            var encrypted = (string)(await scalar($"SELECT Ciphertext FROM communications.TelegramConnections WHERE AccountId='{a1}'"))!;
            check(!encrypted.Contains(dummy), "SQL persisted ciphertext excludes plaintext");
            var firstPid = process!.Id; await Start(ring);
            check(process!.Id != firstPid, "genuine ApiHost process killed and restarted with new PID");
            var resumed = await Send("GET", PathFor(a1));
            check(resumed.Status == 200 && resumed.Body.GetProperty("securityReady").GetBoolean()
                && resumed.Body.GetProperty("verificationState").GetString() == "UNVERIFIED" && !resumed.Body.GetProperty("liveAllowed").GetBoolean(), "new ApiHost decrypts persisted credential without verification or live admission");
            check((string)(await scalar($"SELECT Ciphertext FROM communications.TelegramConnections WHERE AccountId='{a1}'"))! == encrypted, "restart preserves encrypted row exactly");
            check((await Send("PUT", PathFor(a1, "credentials"), new { botToken = dummy, expectedVersion = 1 }, key: "apihost-persist")).Status == 200, "new process decrypts persisted replay key");
            check((await Send("POST", PathFor(a1, "verify"))).Status == 503, "normal public verification remains blocked");
            check((await Send("POST", PathFor(a1, "activate"))).Status == 503 && (await Send("POST", PathFor(a1, "reconnect"))).Status == 503,
                "normal activation and reconnect remain closed without environment approvals");
            check((await Send("GET", PathFor(a1), actor: "a", workspace: "ws_com1_b")).Status == 404, "normal ApiHost cross Workspace account hidden");
            check((await Send("GET", PathFor(a1), actor: "b")).Status == 403, "normal ApiHost explicit manage authorization enforced");
            check((await Send("GET", PathFor(a1), actor: null)).Status == 401, "normal ApiHost anonymous access denied");
            await Stop();
            using (var keys = KeyServices(ring)) keys.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(60));
            await Start(ring);
            check((await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "old credential decrypts after key rotation and actual restart");
            check((await Send("PUT", PathFor(a1, "credentials"), new { botToken = rotated, expectedVersion = 2 })).Status == 200, "Bot credential replacement after key rotation");
            check((await Send("GET", PathFor(a2))).Body.GetProperty("credentialGeneration").GetInt64() == 1
                && (await Send("GET", PathFor(b1), actor: "a", workspace: "ws_com1_b")).Body.GetProperty("credentialGeneration").GetInt64() == 1, "rotation A1 leaves A2 and B1 untouched");
            await Stop();
            foreach (var file in Directory.GetFiles(ring)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
            var savedCipher = (string)(await scalar($"SELECT Ciphertext FROM communications.TelegramConnections WHERE AccountId='{a1}'"))!;
            var wrong = Path.Combine(owned, "wrong"); Directory.CreateDirectory(wrong);
            using (var keys = KeyServices(wrong)) keys.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));
            await Start(wrong);
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "wrong key ring fails closed through actual ApiHost");
            var empty = Path.Combine(owned, "empty"); Directory.CreateDirectory(empty);
            await Start(empty);
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean() && !Directory.GetFiles(empty).Any(), "empty ring never silently provisions fallback keys");
            await Start(ring, "CertificateStore");
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "missing certificate starts safely without custody");
            await Stop();
            using (var keys = KeyServices(ring)) keys.GetRequiredService<IKeyManager>().RevokeAllKeys(DateTimeOffset.UtcNow.AddDays(1), "disposable revocation");
            await Start(ring);
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "revoked key fails closed after process restart");
            await Stop();
            // Restore only this generated ring; preserve the original SQL ciphertext.
            foreach (var file in Directory.GetFiles(ring)) File.Delete(file);
            foreach (var file in Directory.GetFiles(backup)) File.Copy(file, Path.Combine(ring, Path.GetFileName(file)));
            await Start(ring);
            check((await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean()
                && (string)(await scalar($"SELECT Ciphertext FROM communications.TelegramConnections WHERE AccountId='{a1}'"))! == savedCipher, "synthetic key backup restore recovers unchanged encrypted SQL data");
            await scalar($"UPDATE communications.TelegramConnections SET Ciphertext=N'corrupted-task-payload' WHERE AccountId='{a1}'; SELECT 1");
            check(!(await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "persisted corrupt SQL ciphertext fails closed in normal ApiHost");
            await scalar($"UPDATE communications.TelegramConnections SET Ciphertext=N'{savedCipher.Replace("'", "''")}' WHERE AccountId='{a1}'; SELECT 1");
            check((await Send("GET", PathFor(a1))).Body.GetProperty("securityReady").GetBoolean(), "restored synthetic encrypted SQL record decrypts with restored ring");
            check((await Send("DELETE", PathFor(a1, "credentials"), new { expectedVersion = 3 })).Status == 200
                && !(await Send("GET", PathFor(a1))).Body.GetProperty("credentialPresent").GetBoolean(), "normal ApiHost credential removal works");
            await Stop();
            var fileToCorrupt = Directory.GetFiles(ring, "key-*.xml").First(); await File.WriteAllTextAsync(fileToCorrupt, "invalid XML");
            using (var invalid = Custody(ring)) check(!invalid.Ready, "corrupt ring fails without auto repair");
            var denyRule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory | FileSystemRights.ReadAttributes, AccessControlType.Deny);
            try
            {
                var deniedAcl = new DirectoryInfo(wrong).GetAccessControl();
                deniedAcl.AddAccessRule(denyRule);
                new DirectoryInfo(wrong).SetAccessControl(deniedAcl);
                using var denied = Custody(wrong);
                check(!denied.Ready && denied.Protection is null, "OS-denied ring access fails closed without fallback");
            }
            finally
            {
                var restoredAcl = new DirectoryInfo(wrong).GetAccessControl();
                restoredAcl.RemoveAccessRuleSpecific(denyRule);
                new DirectoryInfo(wrong).SetAccessControl(restoredAcl);
            }
            var certRing = Path.Combine(owned, "certificate-ring"); Directory.CreateDirectory(certRing);
            X509Certificate2 StoreCertificate(bool expired = false, bool publicOnly = false)
            {
                using var pair = RSA.Create(2048);
                var req = new CertificateRequest("CN=UniCoreCRM-disposable-" + Guid.NewGuid().ToString("N"), pair, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var generated = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), expired ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(2));
                var certificate = publicOnly ? X509CertificateLoader.LoadCertificate(generated.Export(X509ContentType.Cert))
                    : X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx, password), password, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite); store.Add(certificate);
                certificates.Add(certificate); return certificate;
            }
            PersistentTelegramCustody CertificateCustody(X509Certificate2 cert)
            {
                var configuration = Config(certRing, "CertificateStore");
                configuration["Communications:Telegram:Custody:CertificateStoreLocation"] = "CurrentUser";
                configuration["Communications:Telegram:Custody:CertificateThumbprint"] = cert.Thumbprint;
                return PersistentTelegramCustody.Create(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
            }
            var stored = StoreCertificate();
            var certificateServices = new ServiceCollection(); certificateServices.AddLogging();
            certificateServices.AddDataProtection().SetApplicationName("UniCoreCRM.Communications.Credentials")
                .PersistKeysToFileSystem(new DirectoryInfo(certRing)).ProtectKeysWithCertificate(stored).DisableAutomaticKeyGeneration();
            using (var keys = certificateServices.BuildServiceProvider())
                keys.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));
            using (var positive = CertificateCustody(stored)) check(positive.Ready, "configured certificate-store custody validates private key and encrypted ring");
            using (var wrongCertificate = CertificateCustody(StoreCertificate())) check(!wrongCertificate.Ready, "wrong configured certificate refused even while correct certificate remains in store");
            using (var expired = CertificateCustody(StoreCertificate(expired: true))) check(!expired.Ready, "expired certificate unavailable");
            using (var publicOnly = CertificateCustody(StoreCertificate(publicOnly: true))) check(!publicOnly.Ready, "certificate missing private key unavailable");
            await Start(certRing, "CertificateStore", thumbprint: stored.Thumbprint);
            var certAccount = await Create("ApiHost Certificate A1");
            check((await Send("PUT", PathFor(certAccount, "credentials"), new { botToken = dummy, expectedVersion = 1 })).Status == 200, "normal ApiHost certificate custody stores protected dummy");
            var certPid = process!.Id; await Start(certRing, "CertificateStore", thumbprint: stored.Thumbprint);
            check(process!.Id != certPid && (await Send("GET", PathFor(certAccount))).Body.GetProperty("securityReady").GetBoolean(), "certificate custody decrypts across genuine ApiHost process restart");
            await Stop();
            using (var cached = CertificateCustody(stored))
            {
                check(cached.Ready, "custody initially healthy before external ring mutation");
                using (var keys = certificateServices.BuildServiceProvider()) keys.GetRequiredService<IKeyManager>().RevokeAllKeys(DateTimeOffset.UtcNow.AddDays(1), "task-only revocation");
                check(!cached.Ready && cached.Protection is null, "same-process ring revocation retires cached provider immediately");
            }
            check(!logs.ToString().Contains(dummy) && !logs.ToString().Contains(rotated), "actual ApiHost logs omit synthetic tokens");
            await File.WriteAllTextAsync(Path.Combine(artifacts, "apihost-process.log"), logs.ToString());
            await File.WriteAllTextAsync(Path.Combine(artifacts, "apihost-process-ids.json"), JsonSerializer.Serialize(ids));
        }
        finally
        {
            await Stop();
            await File.WriteAllTextAsync(Path.Combine(artifacts, "apihost-process.log"), logs.ToString());
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);
                var thumbprints = certificates.Select(c => c.Thumbprint).ToArray();
                foreach (var cert in certificates)
                {
                    store.Remove(cert);
                    using var privateKey = cert.GetRSAPrivateKey();
                    if (privateKey is RSACng cng && !cng.Key.IsEphemeral) cng.Key.Delete();
                    cert.Dispose();
                }
                check(thumbprints.All(t => store.Certificates.Find(X509FindType.FindByThumbprint, t, false).Count == 0), "exact task certificate-store entries removed");
            }
            if (!owned.StartsWith(Path.GetFullPath(artifacts) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(owned) != database || !database.StartsWith("UnicoreCRM_COM2C_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup target");
            Directory.Delete(owned, true);
            check(!Directory.Exists(owned), "task ApiHost rings TLS certificate and content root cleaned");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Restrict(string path)
    {
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-32-544") })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
}
