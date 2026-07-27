using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update7 : Migration
{
    #region Migration

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "FixStatus",
                                        table: "VulnerabilityFindings",
                                        type: "integer",
                                        nullable: false,
                                        defaultValue: 0);

        migrationBuilder.AddColumn<string>(name: "PackageClass",
                                           table: "VulnerabilityFindings",
                                           type: "character varying(50)",
                                           maxLength: 50,
                                           nullable: true);

        migrationBuilder.AddColumn<string>(name: "PackageType",
                                           table: "VulnerabilityFindings",
                                           type: "character varying(50)",
                                           maxLength: 50,
                                           nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FixStatus",
                                    table: "VulnerabilityFindings");

        migrationBuilder.DropColumn(name: "PackageClass",
                                    table: "VulnerabilityFindings");

        migrationBuilder.DropColumn(name: "PackageType",
                                    table: "VulnerabilityFindings");
    }

    #endregion // Migration
}