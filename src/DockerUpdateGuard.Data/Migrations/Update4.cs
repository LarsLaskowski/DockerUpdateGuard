using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update4 : Migration
{
    #region Migration

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Summary",
                                             table: "VulnerabilityFindings",
                                             type: "text",
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "character varying(2000)",
                                             oldMaxLength: 2000,
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ReferenceUrl",
                                             table: "VulnerabilityFindings",
                                             type: "text",
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "character varying(1000)",
                                             oldMaxLength: 1000,
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ErrorMessage",
                                             table: "ScanRuns",
                                             type: "text",
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "character varying(1000)",
                                             oldMaxLength: 1000,
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "Description",
                                             table: "ObservedImages",
                                             type: "text",
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "character varying(1000)",
                                             oldMaxLength: 1000,
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ErrorMessage",
                                             table: "ContainerActionRuns",
                                             type: "text",
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "character varying(1000)",
                                             oldMaxLength: 1000,
                                             oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "Summary",
                                             table: "VulnerabilityFindings",
                                             type: "character varying(2000)",
                                             maxLength: 2000,
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "text",
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ReferenceUrl",
                                             table: "VulnerabilityFindings",
                                             type: "character varying(1000)",
                                             maxLength: 1000,
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "text",
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ErrorMessage",
                                             table: "ScanRuns",
                                             type: "character varying(1000)",
                                             maxLength: 1000,
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "text",
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "Description",
                                             table: "ObservedImages",
                                             type: "character varying(1000)",
                                             maxLength: 1000,
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "text",
                                             oldNullable: true);

        migrationBuilder.AlterColumn<string>(name: "ErrorMessage",
                                             table: "ContainerActionRuns",
                                             type: "character varying(1000)",
                                             maxLength: 1000,
                                             nullable: true,
                                             oldClrType: typeof(string),
                                             oldType: "text",
                                             oldNullable: true);
    }

    #endregion // Migration
}