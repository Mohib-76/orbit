using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Orbit.Core;

/// <summary>This PC's long-lived TLS identity. The phone pins its fingerprint at pairing.</summary>
public static class Identity
{
    static X509Certificate2? _cert;

    public static X509Certificate2 Certificate => _cert ??= LoadOrCreate();
    public static string Fingerprint => FingerprintOf(Certificate);

    public static string FingerprintOf(X509Certificate cert) =>
        Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).ToLowerInvariant();

    static X509Certificate2 LoadOrCreate()
    {
        const X509KeyStorageFlags flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable;
        if (File.Exists(Paths.CertFile))
        {
            try { return new X509Certificate2(File.ReadAllBytes(Paths.CertFile), (string?)null, flags); }
            catch (Exception ex) { Log.Write("identity load failed, regenerating: " + ex.Message); }
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Orbit PC", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        var pfx = cert.Export(X509ContentType.Pfx);
        File.WriteAllBytes(Paths.CertFile, pfx);
        // Re-import from PFX: SChannel cannot use ephemeral keys for TLS.
        return new X509Certificate2(pfx, (string?)null, flags);
    }
}
