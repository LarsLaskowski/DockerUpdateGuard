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
        throw new InvalidOperationException("Skeleton: not implemented");
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
        throw new InvalidOperationException("Skeleton: not implemented");
    }

    #endregion // Methods
}