namespace DockerUpdateGuard.Configuration;

/// <summary>
/// Outcome of loading a configured client certificate
/// </summary>
public enum ClientCertificateLoadStatus
{
    /// <summary>
    /// No certificate path is configured (null, empty or whitespace)
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// The certificate with its private key was loaded
    /// </summary>
    Loaded,

    /// <summary>
    /// The file extension is not .pfx or .p12
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// The file or its directory does not exist
    /// </summary>
    FileNotFound,

    /// <summary>
    /// The file cannot be read or is not a password-less PKCS#12 bundle
    /// </summary>
    Unreadable,

    /// <summary>
    /// The bundle contains no certificate with a private key
    /// </summary>
    NoPrivateKey
}