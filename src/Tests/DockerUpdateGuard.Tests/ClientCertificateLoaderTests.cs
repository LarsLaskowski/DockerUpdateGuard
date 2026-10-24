using DockerUpdateGuard.Configuration;
using DockerUpdateGuard.Tests.Helper;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ClientCertificateLoader"/>
/// </summary>
[TestClass]
public class ClientCertificateLoaderTests
{
    #region Methods

    /// <summary>
    /// Verify a path without a PKCS#12 extension is rejected before the file is read
    /// </summary>
    /// <param name="path">Certificate path</param>
    [TestMethod]
    [DataRow("/certs/client.pem")]
    [DataRow("/certs/client.crt")]
    [DataRow("/certs/client.cer")]
    [DataRow("/certs/client.key")]
    [DataRow("/certs/client")]
    public void ClientCertificateLoaderTryLoadWithUnsupportedExtensionReturnsUnsupportedFormatWithoutReading(string path)
    {
        var readCount = 0;
        var loader = new ClientCertificateLoader(_ =>
                                                 {
                                                     readCount++;

                                                     return [];
                                                 });

        var status = loader.TryLoad(path, out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.UnsupportedFormat,
                        status,
                        "A file that is not .pfx or .p12 must be reported as an unsupported format");
        Assert.IsNull(certificate, "No certificate may be returned for an unsupported format");
        Assert.AreEqual(0, readCount, "A file with an unsupported extension must not be read");
    }

    /// <summary>
    /// Verify a missing file or directory is reported as not found
    /// </summary>
    /// <param name="exceptionType">Exception thrown by the reader</param>
    [TestMethod]
    [DataRow(typeof(FileNotFoundException))]
    [DataRow(typeof(DirectoryNotFoundException))]
    public void ClientCertificateLoaderTryLoadWithMissingFileReturnsFileNotFound(Type exceptionType)
    {
        var loader = new ClientCertificateLoader(_ => throw (Exception)Activator.CreateInstance(exceptionType)!);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.FileNotFound,
                        status,
                        "A missing file or directory must be reported as not found");
        Assert.IsNull(certificate, "No certificate may be returned for a missing file");
    }

    /// <summary>
    /// Verify a file that cannot be read is reported as unreadable
    /// </summary>
    /// <param name="exceptionType">Exception thrown by the reader</param>
    [TestMethod]
    [DataRow(typeof(UnauthorizedAccessException))]
    [DataRow(typeof(IOException))]
    public void ClientCertificateLoaderTryLoadWithUnreadableFileReturnsUnreadable(Type exceptionType)
    {
        var loader = new ClientCertificateLoader(_ => throw (Exception)Activator.CreateInstance(exceptionType)!);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.Unreadable,
                        status,
                        "A file that cannot be read must be reported as unreadable");
        Assert.IsNull(certificate, "No certificate may be returned for an unreadable file");
    }

    /// <summary>
    /// Verify a password-protected bundle is reported as unreadable
    /// </summary>
    [TestMethod]
    public void ClientCertificateLoaderTryLoadWithPasswordProtectedPfxReturnsUnreadable()
    {
        var bytes = ClientCertificateTestData.CreateBundleWithPrivateKey("secret");
        var loader = new ClientCertificateLoader(_ => bytes);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.Unreadable,
                        status,
                        "A password-protected bundle must be reported as unreadable instead of throwing");
        Assert.IsNull(certificate, "No certificate may be returned for a password-protected bundle");
    }

    /// <summary>
    /// Verify corrupt bytes are reported as unreadable
    /// </summary>
    [TestMethod]
    public void ClientCertificateLoaderTryLoadWithCorruptPfxReturnsUnreadable()
    {
        var loader = new ClientCertificateLoader(_ => [0x01, 0x02, 0x03, 0x04]);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.Unreadable,
                        status,
                        "Corrupt bundle bytes must be reported as unreadable instead of throwing");
        Assert.IsNull(certificate, "No certificate may be returned for corrupt bytes");
    }

    /// <summary>
    /// Verify a bundle without a private key is reported as such
    /// </summary>
    [TestMethod]
    public void ClientCertificateLoaderTryLoadWithPfxWithoutPrivateKeyReturnsNoPrivateKey()
    {
        var bytes = ClientCertificateTestData.CreateBundleWithoutPrivateKey();
        var loader = new ClientCertificateLoader(_ => bytes);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.NoPrivateKey,
                        status,
                        "A bundle whose certificate has no private key must be reported as such");
        Assert.IsNull(certificate, "No certificate may be returned for a bundle without private key");
    }

    /// <summary>
    /// Verify a password-less bundle with a private key is loaded
    /// </summary>
    /// <param name="path">Certificate path</param>
    [TestMethod]
    [DataRow("/certs/client.pfx")]
    [DataRow("/certs/client.P12")]
    public void ClientCertificateLoaderTryLoadWithValidPfxReturnsLoaded(string path)
    {
        var bytes = ClientCertificateTestData.CreateBundleWithPrivateKey(null);
        var loader = new ClientCertificateLoader(_ => bytes);

        var status = loader.TryLoad(path, out var certificate);

        using (certificate)
        {
            Assert.AreEqual(ClientCertificateLoadStatus.Loaded,
                            status,
                            "A password-less bundle with a private key must be loaded");
            Assert.IsNotNull(certificate, "A loaded status must return the certificate");
            Assert.IsTrue(certificate.HasPrivateKey, "The loaded certificate must carry its private key");
        }
    }

    /// <summary>
    /// Verify a bundle exported with the empty password is loaded
    /// </summary>
    [TestMethod]
    public void ClientCertificateLoaderTryLoadWithEmptyPasswordPfxReturnsLoaded()
    {
        var bytes = ClientCertificateTestData.CreateBundleWithPrivateKey(string.Empty);
        var loader = new ClientCertificateLoader(_ => bytes);

        var status = loader.TryLoad("/certs/client.pfx", out var certificate);

        using (certificate)
        {
            Assert.AreEqual(ClientCertificateLoadStatus.Loaded,
                            status,
                            "A bundle exported with the empty password must be loaded like a password-less one");
            Assert.IsNotNull(certificate, "A loaded status must return the certificate");
            Assert.IsTrue(certificate.HasPrivateKey, "The loaded certificate must carry its private key");
        }
    }

    /// <summary>
    /// Verify a blank path is reported as not set without reading
    /// </summary>
    /// <param name="path">Certificate path</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ClientCertificateLoaderTryLoadWithBlankPathReturnsNotSet(string? path)
    {
        var readCount = 0;
        var loader = new ClientCertificateLoader(_ =>
                                                 {
                                                     readCount++;

                                                     return [];
                                                 });

        var status = loader.TryLoad(path, out var certificate);

        Assert.AreEqual(ClientCertificateLoadStatus.NotSet,
                        status,
                        "A blank path must be reported as not set");
        Assert.IsNull(certificate, "No certificate may be returned for a blank path");
        Assert.AreEqual(0, readCount, "A blank path must not be read");
    }

    #endregion // Methods
}