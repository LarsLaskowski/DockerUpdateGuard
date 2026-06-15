using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update1 : Migration
{
    #region Migration

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "VulnerabilityAssessmentCheckedAtUtc",
                                                   table: "ImageVersions",
                                                   type: "timestamp with time zone",
                                                   nullable: true);

        migrationBuilder.AddColumn<string>(name: "VulnerabilityAssessmentMessage",
                                           table: "ImageVersions",
                                           type: "character varying(2000)",
                                           maxLength: 2000,
                                           nullable: true);

        migrationBuilder.AddColumn<int>(name: "VulnerabilityAssessmentSource",
                                        table: "ImageVersions",
                                        type: "integer",
                                        nullable: false,
                                        defaultValue: 0);

        migrationBuilder.AddColumn<int>(name: "VulnerabilityAssessmentStatus",
                                        table: "ImageVersions",
                                        type: "integer",
                                        nullable: false,
                                        defaultValue: 0);

        migrationBuilder.AddColumn<string>(name: "UpdateAssessmentMessage",
                                           table: "ContainerSnapshots",
                                           type: "character varying(2000)",
                                           maxLength: 2000,
                                           nullable: true);

        migrationBuilder.AddColumn<int>(name: "UpdateAssessmentStatus",
                                        table: "ContainerSnapshots",
                                        type: "integer",
                                        nullable: false,
                                        defaultValue: 0);

        migrationBuilder.CreateTable(name: "RuntimeContainerTagSelections",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           RegistryRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ContainerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Digest = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                                                           SelectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_RuntimeContainerTagSelections", x => x.Id);
                                                      table.ForeignKey(name: "FK_RuntimeContainerTagSelections_DockerInstances_DockerInstanc~",
                                                                       column: x => x.DockerInstanceId,
                                                                       principalTable: "DockerInstances",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                      table.ForeignKey(name: "FK_RuntimeContainerTagSelections_RegistryRepositories_Registry~",
                                                                       column: x => x.RegistryRepositoryId,
                                                                       principalTable: "RegistryRepositories",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                  });

        migrationBuilder.CreateIndex(name: "IX_RuntimeContainerTagSelections_DockerInstanceId_ContainerId",
                                     table: "RuntimeContainerTagSelections",
                                     columns: new[] { "DockerInstanceId", "ContainerId" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_RuntimeContainerTagSelections_RegistryRepositoryId",
                                     table: "RuntimeContainerTagSelections",
                                     column: "RegistryRepositoryId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RuntimeContainerTagSelections");

        migrationBuilder.DropColumn(name: "VulnerabilityAssessmentCheckedAtUtc",
                                    table: "ImageVersions");

        migrationBuilder.DropColumn(name: "VulnerabilityAssessmentMessage",
                                    table: "ImageVersions");

        migrationBuilder.DropColumn(name: "VulnerabilityAssessmentSource",
                                    table: "ImageVersions");

        migrationBuilder.DropColumn(name: "VulnerabilityAssessmentStatus",
                                    table: "ImageVersions");

        migrationBuilder.DropColumn(name: "UpdateAssessmentMessage",
                                    table: "ContainerSnapshots");

        migrationBuilder.DropColumn(name: "UpdateAssessmentStatus",
                                    table: "ContainerSnapshots");
    }

    #endregion // Migration
}