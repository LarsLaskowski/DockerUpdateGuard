namespace DockerUpdateGuard.Tests.Helper;

/// <summary>
/// Assertions that a failure message and log text carry no credential and no part of a rejected link
/// </summary>
internal static class PaginationLeakAssert
{
    #region Methods

    /// <summary>
    /// Assert the text carries neither a secret nor the path or query of the rejected link
    /// </summary>
    /// <param name="text">Failure message and log messages joined</param>
    /// <param name="bearerToken">Bearer token that must not appear</param>
    public static void DoesNotLeak(string text, string bearerToken)
    {
        Assert.DoesNotContain("test-pat", text, "Messages and logs must never carry the Docker Hub PAT");
        Assert.DoesNotContain("token=leak", text, "Messages and logs must never carry the query of the rejected link");
        Assert.DoesNotContain("/collect", text, "Messages and logs must never carry the path of the rejected link");
        Assert.DoesNotContain(bearerToken, text, "Messages and logs must never carry a bearer token");
    }

    #endregion // Methods
}