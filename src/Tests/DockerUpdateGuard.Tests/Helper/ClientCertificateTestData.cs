using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Generates PKCS#12 bundles for client certificate tests so that no certificate is checked in
/// </summary>
internal static class ClientCertificateTestData
{
    #region Static methods

    /// <summary>
    /// Create a bundle with a private key
    /// </summary>
    /// <param name="password">Bundle password, <c>null</c> for none</param>
    /// <returns>PKCS#12 bytes</returns>
    public static byte[] CreateBundleWithPrivateKey(string? password)
    {
        using (var certificate = CreateSelfSignedCertificate())
        {
            return password is null
                       ? certificate.Export(X509ContentType.Pkcs12)
                       : certificate.Export(X509ContentType.Pkcs12, password);
        }
    }

    /// <summary>
    /// Create a bundle that holds a certificate without a private key
    /// </summary>
    /// <returns>PKCS#12 bytes</returns>
    public static byte[] CreateBundleWithoutPrivateKey()
    {
        using (var certificate = CreateSelfSignedCertificate())
            using (var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData))
            {
                return publicCertificate.Export(X509ContentType.Pkcs12);
            }
    }

    /// <summary>
    /// Create a self-signed certificate with a private key
    /// </summary>
    /// <returns>Certificate</returns>
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using (var rsa = RSA.Create(2048))
        {
            var certificateRequest = new CertificateRequest("CN=DockerUpdateGuardClientCertificateTest",
                                                            rsa,
                                                            HashAlgorithmName.SHA256,
                                                            RSASignaturePadding.Pkcs1);

            return certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        }
    }

    #endregion // Static methods
}