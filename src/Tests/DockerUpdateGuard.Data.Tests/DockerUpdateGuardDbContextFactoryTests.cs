using DockerUpdateGuard.Data.Design;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="DockerUpdateGuardDbContextFactory"/>
/// </summary>
[TestClass]
public class DockerUpdateGuardDbContextFactoryTests
{
    #region Constants

    /// <summary>
    /// Environment variable name used by the factory
    /// </summary>
    private const string ConnectionStringVariable = "ConnectionStrings__DockerUpdateGuard";

    /// <summary>
    /// Non-secret fallback connection string embedded in the factory
    /// </summary>
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=dockerupdateguard;Username=postgres";

    #endregion // Constants

    #region Properties

    /// <summary>
    /// Test context provided by MSTest framework
    /// </summary>
    public TestContext TestContext { get; set; }

    #endregion // Properties

    #region Methods

    /// <summary>
    /// CreateDbContext uses the environment variable connection string when it is set
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardDbContextFactoryCreateDbContextWithEnvVarUsesEnvironmentConnectionString()
    {
        var previous = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        try
        {
            var expected = "Host=ci-db;Port=5432;Database=dug-test;Username=ci";

            Environment.SetEnvironmentVariable(ConnectionStringVariable, expected);

            var factory = new DockerUpdateGuardDbContextFactory();

            using (var dbContext = factory.CreateDbContext([]))
            {
                var actual = dbContext.Database.GetConnectionString();

                Assert.AreEqual(expected, actual, "The factory must use the connection string from the environment variable");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, previous);
        }
    }

    /// <summary>
    /// CreateDbContext falls back to the non-secret default when the environment variable is absent
    /// </summary>
    [TestMethod]
    public void DockerUpdateGuardDbContextFactoryCreateDbContextWithoutEnvVarUsesFallbackConnectionString()
    {
        var previous = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, null);

            var factory = new DockerUpdateGuardDbContextFactory();

            using (var dbContext = factory.CreateDbContext([]))
            {
                var actual = dbContext.Database.GetConnectionString();

                Assert.AreEqual(FallbackConnectionString, actual, "The factory must fall back to the embedded non-secret connection string when no environment variable is set");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, previous);
        }
    }

    #endregion // Methods
}