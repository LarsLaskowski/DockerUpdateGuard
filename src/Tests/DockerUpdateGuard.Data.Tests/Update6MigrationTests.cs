using DockerUpdateGuard.Data.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DockerUpdateGuard.Data.Tests;

/// <summary>
/// Tests for <see cref="Update6"/>
/// </summary>
[TestClass]
public class Update6MigrationTests
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
    private static Migration CreateMigration()
    {
        return new Update6
               {
                   ActiveProvider = ActiveProvider,
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify the migration adds the scan-run columns backing the vulnerability scan deltas
    /// </summary>
    [TestMethod]
    public void Update6UpAddsScanRunColumns()
    {
        var operations = CreateMigration().UpOperations;
        var addedColumns = operations.OfType<AddColumnOperation>()
                                     .ToList();
        var resolvedByScanRunColumn = addedColumns.SingleOrDefault(operation => operation.Table == "VulnerabilityFindings"
                                                                                && operation.Name == "ResolvedByScanRunId");
        var assessmentScanRunColumn = addedColumns.SingleOrDefault(operation => operation.Table == "ImageVersions"
                                                                                && operation.Name == "VulnerabilityAssessmentScanRunId");

        Assert.IsNotNull(resolvedByScanRunColumn, "The migration must add the resolving scan-run column to the vulnerability findings");
        Assert.IsTrue(resolvedByScanRunColumn.IsNullable, "The resolving scan-run column must be nullable so existing findings stay valid");
        Assert.AreEqual("uuid",
                        resolvedByScanRunColumn.ColumnType,
                        "The resolving scan-run column must be stored as a uuid");
        Assert.IsNotNull(assessmentScanRunColumn, "The migration must add the assessment scan-run column to the image versions");
        Assert.IsTrue(assessmentScanRunColumn.IsNullable, "The assessment scan-run column must be nullable so unscanned image versions stay valid");
        Assert.AreEqual("uuid",
                        assessmentScanRunColumn.ColumnType,
                        "The assessment scan-run column must be stored as a uuid");
    }

    /// <summary>
    /// Verify the migration indexes the resolving scan run and links it to the scan runs without cascading deletes
    /// </summary>
    [TestMethod]
    public void Update6UpIndexesAndLinksTheResolvingScanRun()
    {
        var operations = CreateMigration().UpOperations;
        var createdIndex = operations.OfType<CreateIndexOperation>()
                                     .SingleOrDefault(operation => operation.Table == "VulnerabilityFindings");
        var addedForeignKey = operations.OfType<AddForeignKeyOperation>()
                                        .SingleOrDefault(operation => operation.Table == "VulnerabilityFindings");

        Assert.IsNotNull(createdIndex, "The migration must index the resolving scan run of the vulnerability findings");
        Assert.AreSequenceEqual(["ResolvedByScanRunId"],
                                createdIndex.Columns,
                                "The created index must cover the resolving scan-run column");
        Assert.IsNotNull(addedForeignKey, "The migration must link the resolving scan run to the scan runs");
        Assert.AreEqual("ScanRuns",
                        addedForeignKey.PrincipalTable,
                        "The resolving scan run must reference the scan runs table");
        Assert.AreEqual(ReferentialAction.SetNull,
                        addedForeignKey.OnDelete,
                        "Deleting a scan run must clear the resolving scan run instead of deleting the finding");
    }

    /// <summary>
    /// Verify the migration reverts every schema change it applies
    /// </summary>
    [TestMethod]
    public void Update6DownRemovesScanRunColumns()
    {
        var operations = CreateMigration().DownOperations;
        var droppedColumns = operations.OfType<DropColumnOperation>()
                                       .Select(operation => operation.Name)
                                       .OrderBy(name => name, StringComparer.Ordinal)
                                       .ToList();

        Assert.HasCount(1,
                        operations.OfType<DropForeignKeyOperation>(),
                        "The rollback must drop the resolving scan-run foreign key");
        Assert.HasCount(1,
                        operations.OfType<DropIndexOperation>(),
                        "The rollback must drop the resolving scan-run index");
        Assert.AreSequenceEqual(["ResolvedByScanRunId", "VulnerabilityAssessmentScanRunId"],
                                droppedColumns,
                                "The rollback must drop both scan-run columns added by the migration");
    }

    #endregion // Methods
}