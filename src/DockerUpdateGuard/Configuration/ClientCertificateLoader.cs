using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Loads a configured client certificate from a password-less PKCS#12 file
/// </summary>
public sealed class ClientCertificateLoader
{
    #region Fields

    /// <summary>
    /// Reads all bytes of a file
    /// </summary>
    private readonly Func<string, byte[]> _readAllBytes;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    public ClientCertificateLoader()
        : this(File.ReadAllBytes)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="readAllBytes">File reader</param>
    public ClientCertificateLoader(Func<string, byte[]> readAllBytes)
    {
        _readAllBytes = readAllBytes;
    }

    #endregion // Constructors

    #region Static methods

    /// <summary>
    /// Describe a load failure
    /// </summary>
    /// <param name="status">Failed load status</param>
    /// <returns>Reason text</returns>
    public static string DescribeFailure(ClientCertificateLoadStatus status)
    {
        return status switch
               {
                   ClientCertificateLoadStatus.UnsupportedFormat => "only password-less PKCS#12 files with the extension .pfx or .p12 are supported",
                   ClientCertificateLoadStatus.FileNotFound => "the file does not exist",
                   ClientCertificateLoadStatus.Unreadable => "the file cannot be read or is not a password-less PKCS#12 bundle",
                   ClientCertificateLoadStatus.NoPrivateKey => "the bundle contains no certificate with a private key",
                   _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The status does not describe a load failure"),
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Try to load the client certificate
    /// </summary>
    /// <param name="certificatePath">Configured certificate path</param>
    /// <param name="certificate">Loaded certificate when the status is <see cref="ClientCertificateLoadStatus.Loaded"/></param>
    /// <returns>Load status</returns>
    public ClientCertificateLoadStatus TryLoad(string? certificatePath, out X509Certificate2? certificate)
    {
        certificate = null;

        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            return ClientCertificateLoadStatus.NotSet;
        }

        var extension = Path.GetExtension(certificatePath);

        if (string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) == false
            && string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase) == false)
        {
            return ClientCertificateLoadStatus.UnsupportedFormat;
        }

        byte[] bytes;

        try
        {
            bytes = _readAllBytes(certificatePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ClientCertificateLoadStatus.FileNotFound;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ClientCertificateLoadStatus.Unreadable;
        }

        X509Certificate2 loaded;

        try
        {
            loaded = X509CertificateLoader.LoadPkcs12(bytes, password: null);
        }
        catch (CryptographicException)
        {
            return ClientCertificateLoadStatus.Unreadable;
        }

        if (loaded.HasPrivateKey == false)
        {
            loaded.Dispose();

            return ClientCertificateLoadStatus.NoPrivateKey;
        }

        certificate = loaded;

        return ClientCertificateLoadStatus.Loaded;
    }

    #endregion // Methods
}