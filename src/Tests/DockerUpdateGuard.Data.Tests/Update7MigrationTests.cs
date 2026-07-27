using DockerUpdateGuard.Data.Migrations;

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="Update7"/>
/// </summary>
[TestClass]
public class Update7MigrationTests
{
    #region Const fields

    /// <summary>
    /// Provider the migration operations are built for
    /// </summary>
    private const string ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    #endregion // Const fields

    #region Static methods

    /// <summary>
    /// Create the migration under test with the active provider applied
    /// </summary>
    /// <returns>Migration under test</returns>
    private static Update7 CreateMigration()
    {
        return new Update7
               {
                   ActiveProvider = ActiveProvider,
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the migration adds the fix status column with a NotSet default so existing findings stay valid
    /// </summary>
    [TestMethod]
    public void Update7UpAddsFixStatusColumnDefaultingToNotSet()
    {
        var operations = CreateMigration().UpOperations;
        var fixStatusColumn = operations.OfType<AddColumnOperation>()
                                        .SingleOrDefault(operation => operation.Table == "VulnerabilityFindings"
                                                                      && operation.Name == "FixStatus");

        Assert.IsNotNull(fixStatusColumn, "The migration must add the fix status column to the vulnerability findings");
        Assert.AreEqual("integer",
                        fixStatusColumn.ColumnType,
                        "The fix status column must be stored as an integer enum value");
        Assert.IsFalse(fixStatusColumn.IsNullable, "The fix status column must be non-nullable");
        Assert.AreEqual(0,
                        fixStatusColumn.DefaultValue,
                        "Existing findings must default to NotSet so they are treated as 'no information'");
    }

    /// <summary>
    /// Verify the migration adds the nullable package classification columns
    /// </summary>
    [TestMethod]
    public void Update7UpAddsNullablePackageClassificationColumns()
    {
        var operations = CreateMigration().UpOperations;
        var addedColumns = operations.OfType<AddColumnOperation>()
                                     .ToList();
        var packageClassColumn = addedColumns.SingleOrDefault(operation => operation.Table == "VulnerabilityFindings"
                                                                           && operation.Name == "PackageClass");
        var packageTypeColumn = addedColumns.SingleOrDefault(operation => operation.Table == "VulnerabilityFindings"
                                                                          && operation.Name == "PackageType");

        Assert.IsNotNull(packageClassColumn, "The migration must add the package class column to the vulnerability findings");
        Assert.IsTrue(packageClassColumn.IsNullable, "The package class column must be nullable for providers that do not report one");
        Assert.AreEqual(50,
                        packageClassColumn.MaxLength,
                        "The package class column must use the configured maximum length");
        Assert.IsNotNull(packageTypeColumn, "The migration must add the package type column to the vulnerability findings");
        Assert.IsTrue(packageTypeColumn.IsNullable, "The package type column must be nullable for providers that do not report one");
        Assert.AreEqual(50,
                        packageTypeColumn.MaxLength,
                        "The package type column must use the configured maximum length");
    }

    /// <summary>
    /// Verify the migration only touches the vulnerability findings and leaves the finding identity index alone
    /// </summary>
    [TestMethod]
    public void Update7UpLeavesFindingIdentityUnchanged()
    {
        var operations = CreateMigration().UpOperations;

        Assert.HasCount(3,
                        operations,
                        "The migration must add exactly the three new columns");
        Assert.IsEmpty(operations.OfType<CreateIndexOperation>(),
                       "The migration must not change any index so the finding identity stays untouched");
        Assert.IsEmpty(operations.OfType<DropIndexOperation>(),
                       "The migration must not drop any index so the finding identity stays untouched");
        Assert.IsTrue(operations.OfType<AddColumnOperation>()
                                .All(operation => operation.Table == "VulnerabilityFindings"),
                      "The migration must only extend the vulnerability findings table");
    }

    /// <summary>
    /// Verify the migration reverts every schema change it applies
    /// </summary>
    [TestMethod]
    public void Update7DownRemovesTheAddedColumns()
    {
        var operations = CreateMigration().DownOperations;
        var droppedColumns = operations.OfType<DropColumnOperation>()
                                       .Select(operation => operation.Name)
                                       .OrderBy(name => name, StringComparer.Ordinal)
                                       .ToList();

        Assert.HasCount(3,
                        operations,
                        "The rollback must drop exactly the three columns added by the migration");
        Assert.AreSequenceEqual(["FixStatus", "PackageClass", "PackageType"],
                                droppedColumns,
                                "The rollback must drop every column added by the migration");
        Assert.IsTrue(operations.OfType<DropColumnOperation>()
                                .All(operation => operation.Table == "VulnerabilityFindings"),
                      "The rollback must only touch the vulnerability findings table");
    }

    #endregion // Methods
}