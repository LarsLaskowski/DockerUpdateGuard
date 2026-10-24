using DockerUpdateGuard.Infrastructure;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="UrlRedaction"/>
/// </summary>
[TestClass]
public class UrlRedactionTests
{
    #region Methods

    /// <summary>
    /// Verify a null or empty URL text yields an empty text
    /// </summary>
    [TestMethod]
    public void UrlRedactionRedactUserInfoNullOrEmptyReturnsEmpty()
    {
        Assert.AreEqual(string.Empty,
                        UrlRedaction.RedactUserInfo((string?)null),
                        "A null URL text must be returned as an empty text");
        Assert.AreEqual(string.Empty,
                        UrlRedaction.RedactUserInfo(string.Empty),
                        "An empty URL text must be returned as an empty text");
    }

    /// <summary>
    /// Verify a URL text without an at sign is returned unchanged
    /// </summary>
    /// <param name="url">URL text without an at sign</param>
    [TestMethod]
    [DataRow("unix:///var/run/docker.sock")]
    [DataRow("npipe://./pipe/docker_engine")]
    [DataRow("https://host.example/path?token=x")]
    public void UrlRedactionRedactUserInfoWithoutAtSignReturnsInputUnchanged(string url)
    {
        Assert.AreEqual(url,
                        UrlRedaction.RedactUserInfo(url),
                        "A URL text without an at sign carries no user info and must stay as it is");
    }

    /// <summary>
    /// Verify an already masked URL text is returned unchanged
    /// </summary>
    [TestMethod]
    public void UrlRedactionRedactUserInfoAlreadyMaskedReturnsInputUnchanged()
    {
        var masked = "https://***@docker.example.test/";

        Assert.AreEqual(masked,
                        UrlRedaction.RedactUserInfo(masked),
                        "Masking an already masked URL text must not change it");
    }

    /// <summary>
    /// Verify parsed user info is replaced by the mask and neither part of it remains
    /// </summary>
    /// <param name="url">URL text with parsed user info</param>
    /// <param name="expected">Expected masked URL text</param>
    /// <param name="userPart">Part of the user name that must not remain</param>
    /// <param name="secretPart">Part of the password that must not remain</param>
    [TestMethod]
    [DataRow("https://user:secret@portainer.example:9443/api/auth?x=1", "https://***@portainer.example:9443/api/auth?x=1", "user", "secret")]
    [DataRow("https://user@host.example", "https://***@host.example/", "user", "user")]
    [DataRow("https://:secret@host.example/", "https://***@host.example/", "secret", "secret")]
    [DataRow("tcp://user:secret@docker.example:2376", "tcp://***@docker.example:2376/", "user", "secret")]
    [DataRow("https://user:secret@[::1]:2376/", "https://***@[::1]:2376/", "user", "secret")]
    [DataRow("https://user%40x:se%3Acret@host.example", "https://***@host.example/", "user", "se%3Acret")]
    [DataRow("https://user:secret@host.example/p#frag", "https://***@host.example/p#frag", "user", "secret")]
    public void UrlRedactionRedactUserInfoUserAndPasswordMasked(string url,
                                                                string expected,
                                                                string userPart,
                                                                string secretPart)
    {
        var result = UrlRedaction.RedactUserInfo(url);

        Assert.AreEqual(expected, result, "Parsed user info must be replaced by the mask and the rest of the URL kept");
        Assert.DoesNotContain(userPart, result, "The user name must not remain in the masked URL text");
        Assert.DoesNotContain(secretPart, result, "The password must not remain in the masked URL text");
    }

    /// <summary>
    /// Verify an at sign the parser does not resolve to user info is masked fail-closed
    /// </summary>
    /// <param name="url">URL text with an at sign and no parsed user info</param>
    /// <param name="expected">Expected masked URL text</param>
    /// <param name="userPart">Part of the user name that must not remain</param>
    /// <param name="secretPart">Part of the password that must not remain</param>
    [TestMethod]
    [DataRow("https://admin:#Xy7secret@portainer:9443", "https://***@portainer:9443", "admin", "Xy7secret")]
    [DataRow("https://admin:?Xy7secret@portainer:9443", "https://***@portainer:9443", "admin", "Xy7secret")]
    [DataRow("https://admin:/Xy7secret@portainer:9443", "https://***@portainer:9443", "admin", "Xy7secret")]
    [DataRow("https://admin:12/34secret@host", "https://***@host", "admin", "34secret")]
    [DataRow("https://admin:12#secret@host", "https://***@host", "admin", "secret")]
    [DataRow("https://ad/min:secret@host", "https://***@host", "min", "secret")]
    [DataRow("https://admin#:secret@host", "https://***@host", "admin", "secret")]
    [DataRow("tcp://admin:#secret@docker:2376", "tcp://***@docker:2376", "admin", "secret")]
    [DataRow("https://host.example/@scope/pkg", "https://***@scope/pkg", "host.example", "example")]
    public void UrlRedactionRedactUserInfoAtSignWithoutParsedUserInfoMasked(string url,
                                                                            string expected,
                                                                            string userPart,
                                                                            string secretPart)
    {
        var result = UrlRedaction.RedactUserInfo(url);

        Assert.AreEqual(expected, result, "Everything before the last at sign after the scheme separator must be masked");
        Assert.DoesNotContain(userPart, result, "The user name must not remain in the masked URL text");
        Assert.DoesNotContain(secretPart, result, "The password must not remain in the masked URL text");
    }

    /// <summary>
    /// Verify URL texts the rebuild rule does not take are masked up to their last at sign
    /// </summary>
    /// <param name="url">Unparsable or irregular URL text with user info</param>
    /// <param name="expected">Expected masked URL text</param>
    /// <param name="userPart">Part of the user name that must not remain</param>
    /// <param name="secretPart">Part of the password that must not remain</param>
    [TestMethod]
    [DataRow("https://user:p@ss@host.example/path", "https://***@host.example/path", "user", "ss")]
    [DataRow("https://user:pa/ss@host.example", "https://***@host.example", "user", "pa/ss")]
    [DataRow("npipe://user:secret@./pipe/docker_engine", "npipe://***@./pipe/docker_engine", "user", "secret")]
    [DataRow("user:secret@host:2376", "***@host:2376", "user", "secret")]
    [DataRow("user:secret@host?x=http://a", "***@host?x=http://a", "user", "secret")]
    [DataRow("https://a:b@c:/secret@host", "https://***@host", "a:b", "secret")]
    [DataRow("https:\\\\user:secret@host.example", "***@host.example", "user", "secret")]
    [DataRow("user:secret://x@host", "***@host", "user", "secret")]
    public void UrlRedactionRedactUserInfoUnparsableWithUserInfoMasked(string url,
                                                                       string expected,
                                                                       string userPart,
                                                                       string secretPart)
    {
        var result = UrlRedaction.RedactUserInfo(url);

        Assert.AreEqual(expected, result, "Everything before the last at sign must be masked, keeping a valid scheme separator");
        Assert.DoesNotContain(userPart, result, "The user name must not remain in the masked URL text");
        Assert.DoesNotContain(secretPart, result, "The password must not remain in the masked URL text");
    }

    /// <summary>
    /// Verify a null URI yields an empty text
    /// </summary>
    [TestMethod]
    public void UrlRedactionRedactUserInfoNullUriReturnsEmpty()
    {
        Assert.AreEqual(string.Empty,
                        UrlRedaction.RedactUserInfo((Uri?)null),
                        "A null URI must be returned as an empty text");
    }

    /// <summary>
    /// Verify an absolute URI is masked through its absolute URI text
    /// </summary>
    [TestMethod]
    public void UrlRedactionRedactUserInfoAbsoluteUriMasked()
    {
        var result = UrlRedaction.RedactUserInfo(new Uri("https://user:secret@host.example/a"));

        Assert.AreEqual("https://***@host.example/a",
                        result,
                        "The user info of an absolute URI must be replaced by the mask");
    }

    /// <summary>
    /// Verify a relative URI is returned through its original string
    /// </summary>
    [TestMethod]
    public void UrlRedactionRedactUserInfoRelativeUriUsesOriginalString()
    {
        var result = UrlRedaction.RedactUserInfo(new Uri("/v2/library/nginx", UriKind.Relative));

        Assert.AreEqual("/v2/library/nginx",
                        result,
                        "A relative URI has no user info and must be returned as its original string");
    }

    #endregion // Methods
}