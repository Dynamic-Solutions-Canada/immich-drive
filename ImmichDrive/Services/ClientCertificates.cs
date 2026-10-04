using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ImmichDrive.Services;

/// <summary>Explicit, per-connection selection from CurrentUser\My. No private keys are exported.</summary>
public static class ClientCertificates
{
    public sealed record Choice(string Thumbprint, string Label);

    public static List<Choice> ListAvailable()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var choices = new List<Choice>();
        foreach (var certificate in store.Certificates)
        {
            using (certificate)
            {
                if (!IsUsable(certificate)) continue;
                string name = certificate.GetNameInfo(X509NameType.SimpleName, false);
                choices.Add(new(certificate.Thumbprint,
                    $"{name} (expires {certificate.NotAfter:yyyy-MM-dd}; {certificate.Thumbprint[^8..]})"));
            }
        }
        return choices.OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static X509Certificate2 Load(string thumbprint)
    {
        string normalized = NormalizeThumbprint(thumbprint);
        if (normalized.Length != 40 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Select a valid client certificate in Connection settings.");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2? selected = null;
        foreach (var certificate in store.Certificates)
        {
            using (certificate)
            {
                if (certificate.Thumbprint.Equals(normalized, StringComparison.OrdinalIgnoreCase) && IsUsable(certificate))
                    selected = new X509Certificate2(certificate);
            }
        }
        return selected ?? throw new InvalidOperationException(
            "The selected client certificate is missing, expired, or has no usable private key. " +
            "Install it in Current User > Personal, then select it in Connection settings.");
    }

    private static string NormalizeThumbprint(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    internal static bool IsUsable(X509Certificate2 certificate)
    {
        DateTime now = DateTime.Now;
        if (!certificate.HasPrivateKey || now < certificate.NotBefore || now >= certificate.NotAfter) return false;
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basic && basic.CertificateAuthority) return false;
            if (extension is X509KeyUsageExtension usage &&
                (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0) return false;
            // Match HttpClient: an EKU extension must explicitly include Client Authentication.
            if (extension is X509EnhancedKeyUsageExtension eku &&
                !eku.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.2")) return false;
        }
        return true;
    }
}
