using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update5 : Migration
{
    #region Migration

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_VulnerabilityFindings_ImageVersionId_AdvisoryId_AffectedPac~",
                                   table: "VulnerabilityFindings");

        migrationBuilder.AddColumn<string>(name: "InstalledVersion",
                                           table: "VulnerabilityFindings",
                                           type: "character varying(100)",
                                           maxLength: 100,
                                           nullable: true);

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ImageVersionId_AdvisoryId_AffectedPac~",
                                     table: "VulnerabilityFindings",
                                     columns: new[] { "ImageVersionId", "AdvisoryId", "AffectedPackage" },
                                     unique: true,
                                     filter: "\"IsActive\"");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_VulnerabilityFindings_ImageVersionId_AdvisoryId_AffectedPac~",
                                   table: "VulnerabilityFindings");

        migrationBuilder.DropColumn(name: "InstalledVersion",
                                    table: "VulnerabilityFindings");

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ImageVersionId_AdvisoryId_AffectedPac~",
                                     table: "VulnerabilityFindings",
                                     columns: new[] { "ImageVersionId", "AdvisoryId", "AffectedPackage", "FixedVersion" },
                                     unique: true);
    }

    #endregion // Migration
}