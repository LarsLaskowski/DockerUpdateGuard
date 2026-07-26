using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update6 : Migration
{
    #region Migration

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ResolvedByScanRunId",
                                         table: "VulnerabilityFindings",
                                         type: "uuid",
                                         nullable: true);

        migrationBuilder.AddColumn<Guid>(name: "VulnerabilityAssessmentScanRunId",
                                         table: "ImageVersions",
                                         type: "uuid",
                                         nullable: true);

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ResolvedByScanRunId",
                                     table: "VulnerabilityFindings",
                                     column: "ResolvedByScanRunId");

        migrationBuilder.AddForeignKey(name: "FK_VulnerabilityFindings_ScanRuns_ResolvedByScanRunId",
                                       table: "VulnerabilityFindings",
                                       column: "ResolvedByScanRunId",
                                       principalTable: "ScanRuns",
                                       principalColumn: "Id",
                                       onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_VulnerabilityFindings_ScanRuns_ResolvedByScanRunId",
                                        table: "VulnerabilityFindings");

        migrationBuilder.DropIndex(name: "IX_VulnerabilityFindings_ResolvedByScanRunId",
                                   table: "VulnerabilityFindings");

        migrationBuilder.DropColumn(name: "ResolvedByScanRunId",
                                    table: "VulnerabilityFindings");

        migrationBuilder.DropColumn(name: "VulnerabilityAssessmentScanRunId",
                                    table: "ImageVersions");
    }

    #endregion // Migration
}