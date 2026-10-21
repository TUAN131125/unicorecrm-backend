using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

// Dedicated provider: never changes the global provider used by AI, authentication or cursors.
// Key provisioning is explicit. An empty/lost ring must never silently acquire replacement keys.
internal sealed class PersistentTelegramCustody : ITelegramCredentialCustody, IDisposable
{
    private ServiceProvider? services;
    private X509Certificate2? certificate;
    private string? directory;
    private string? ringFingerprint;
    private string? protectionMode;
    private bool configured;
    public bool SyntheticReady => false;
    public IDataProtectionProvider? Protection => Ready ? services?.GetRequiredService<IDataProtectionProvider>() : null;
    public bool Ready
    {
        get
        {
            if (!configured || services is null || directory is null) return false;
            try
            {
                if (certificate is not null && (DateTime.UtcNow < certificate.NotBefore.ToUniversalTime()
                    || DateTime.UtcNow >= certificate.NotAfter.ToUniversalTime() || !certificate.HasPrivateKey)) return false;
                ValidateDirectory(directory);
                ValidateWrapping(directory, protectionMode, certificate);
                // DP caches keys. Refuse disk changes until restart rather than use stale/revoked keys.
                if (Fingerprint(directory) != ringFingerprint) return false;
                var keys = services.GetRequiredService<IKeyManager>().GetAllKeys();
                if (!keys.Any(k => !k.IsRevoked && k.ActivationDate <= DateTimeOffset.UtcNow && k.ExpirationDate > DateTimeOffset.UtcNow)) return false;
                // Force decryption of every persisted key, rather than testing only a new canary.
                foreach (var key in keys) if (key.CreateEncryptor() is null) return false;
                return true;
            }
            catch (Exception e) when (StorageFailure(e)) { return false; }
        }
    }

    internal static PersistentTelegramCustody Create(IConfiguration configuration)
    {
        var result = new PersistentTelegramCustody();
        var section = configuration.GetSection("Communications:Telegram:Custody");
        var mode = section["Protection"];
        if (string.IsNullOrWhiteSpace(mode) || mode == "Disabled") return result;
        try
        {
            var path = section["KeyRingPath"];
            if (path is null || !Path.IsPathFullyQualified(path)) return result;
            ValidateDirectory(path);
            var collection = new ServiceCollection();
            collection.AddLogging(); // no providers: cryptographic internals never escape to host logs
            var protection = collection.AddDataProtection()
                .SetApplicationName("UniCoreCRM.Communications.Credentials")
                .PersistKeysToFileSystem(new DirectoryInfo(path)).DisableAutomaticKeyGeneration();
            if (mode == "WindowsCurrentUserDpapi")
            {
                if (!OperatingSystem.IsWindows()) return result;
                protection.ProtectKeysWithDpapi();
            }
            else if (mode == "CertificateStore")
            {
                var thumbprint = section["CertificateThumbprint"];
                if (thumbprint is null || thumbprint.Length != 40 || thumbprint.Any(c => !Uri.IsHexDigit(c))) return result;
                if (!Enum.TryParse<StoreLocation>(section["CertificateStoreLocation"], out var location)
                    || location is not (StoreLocation.CurrentUser or StoreLocation.LocalMachine)) return result;
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                if (matches.Count != 1) return result;
                result.certificate = matches[0];
                using var privateKey = result.certificate.GetRSAPrivateKey();
                using var publicKey = result.certificate.GetRSAPublicKey();
                if (privateKey is null || publicKey is null) return result;
                var probe = RandomNumberGenerator.GetBytes(32);
                if (!CryptographicOperations.FixedTimeEquals(probe, privateKey.Decrypt(publicKey.Encrypt(probe, RSAEncryptionPadding.OaepSHA256), RSAEncryptionPadding.OaepSHA256))) return result;
                protection.ProtectKeysWithCertificate(result.certificate).UnprotectKeysWithAnyCertificate(result.certificate);
            }
            else return result;
            result.directory = path;
            ValidateWrapping(path, mode, result.certificate);
            result.protectionMode = mode;
            result.ringFingerprint = Fingerprint(path);
            result.services = collection.BuildServiceProvider();
            result.configured = true;
        }
        catch (Exception e) when (StorageFailure(e)) { result.configured = false; }
        return result;
    }

    private static bool StorageFailure(Exception e) => e is CryptographicException or IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or FormatException or System.Xml.XmlException or System.Security.SecurityException;

    private static string Fingerprint(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(path, "*.xml").Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(file)));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ValidateWrapping(string path, string? mode, X509Certificate2? certificate)
    {
        foreach (var file in Directory.GetFiles(path, "key-*.xml"))
        {
            using var reader = System.Xml.XmlReader.Create(file, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
            var xml = XElement.Load(reader);
            var encrypted = xml.Descendants().Single(e => e.Name.LocalName == "encryptedSecret");
            var decryptor = (string?)encrypted.Attribute("decryptorType");
            if (mode == "WindowsCurrentUserDpapi")
            {
                if (decryptor?.StartsWith("Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor,", StringComparison.Ordinal) != true) throw new CryptographicException();
            }
            else
            {
                // Do not let DP resolve a different certificate implicitly from the machine store.
                var embedded = encrypted.Descendants().Where(e => e.Name.LocalName == "X509Certificate").ToArray();
                if (certificate is null || embedded.Length == 0) throw new CryptographicException();
                foreach (var value in embedded)
                {
                    using var publicCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(value.Value));
                    if (publicCertificate.Thumbprint != certificate.Thumbprint) throw new CryptographicException();
                }
            }
        }
    }

    private static void ValidateDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException();
        var files = info.GetFiles("*.xml");
        if (!files.Any(f => f.Name.StartsWith("key-", StringComparison.Ordinal))) throw new IOException();
        if (OperatingSystem.IsWindows())
        {
            ValidateAcl(info.GetAccessControl());
            foreach (var file in files) ValidateAcl(file.GetAccessControl());
        }
        else throw new InvalidOperationException(); // Unix permission validation is not yet admitted.
        foreach (var file in files)
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException();
            using var reader = System.Xml.XmlReader.Create(file.FullName, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
            var xml = XElement.Load(reader);
            if (xml.Name.LocalName == "key" && !xml.Descendants().Any(e => e.Name.LocalName == "encryptedSecret")) throw new CryptographicException();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ValidateAcl(FileSystemSecurity security)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value;
        var trusted = new[] { user, "S-1-5-18", "S-1-5-32-544" };
        var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value;
        if (owner is null || !trusted.Contains(owner)) throw new UnauthorizedAccessException();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !trusted.Contains(rule.IdentityReference.Value)
                && rule.FileSystemRights != 0) throw new UnauthorizedAccessException();
    }

    public void Dispose() { services?.Dispose(); certificate?.Dispose(); }
}
