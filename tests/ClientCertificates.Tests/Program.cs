using ImmichDrive.Services;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string clientAuth = "1.3.6.1.5.5.7.3.2";
const string serverAuth = "1.3.6.1.5.5.7.3.1";
const string anyUsage = "2.5.29.37.0";
int failures = 0;

Check("Client Authentication", [clientAuth], expected: true);
Check("Any Extended Key Usage alone", [anyUsage], expected: false);
Check("Any usage plus Client Authentication", [anyUsage, clientAuth], expected: true);
Check("Server Authentication alone", [serverAuth], expected: false);
Check("No EKU extension", null, expected: true);
Check("Empty EKU extension", [], expected: false);
Check("No digital signature usage", [clientAuth], expected: false,
    usage: X509KeyUsageFlags.KeyEncipherment);
Check("Expired certificate", [clientAuth], expected: false, expired: true);
Check("Public certificate without private key", [clientAuth], expected: false, privateKey: false);
return failures == 0 ? 0 : 1;

void Check(string name, string[]? eku, bool expected,
    X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature,
    bool expired = false, bool privateKey = true)
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=ImmichDrive certificate filter test", key,
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
    if (eku != null)
    {
        var oids = new OidCollection();
        foreach (string oid in eku) oids.Add(new Oid(oid));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
    }
    DateTimeOffset now = DateTimeOffset.UtcNow;
    using var certificate = request.CreateSelfSigned(now.AddDays(-2), now.AddDays(expired ? -1 : 1));
    using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
    bool actual = ClientCertificates.IsUsable(privateKey ? certificate : publicOnly);
    if (actual != expected) failures++;
    Console.WriteLine($"{(actual == expected ? "PASS" : "FAIL")} {name}: expected {expected}, got {actual}");
}
