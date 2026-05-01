using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class Update3 : Migration
{
    #region Methods

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AvailableUpdateVersionTag",
                                           table: "ContainerSnapshots",
                                           type: "character varying(200)",
                                           maxLength: 200,
                                           nullable: true);

        migrationBuilder.AddColumn<string>(name: "ResolvedVersionTag",
                                           table: "ContainerSnapshots",
                                           type: "character varying(200)",
                                           maxLength: 200,
                                           nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AvailableUpdateVersionTag",
                                    table: "ContainerSnapshots");

        migrationBuilder.DropColumn(name: "ResolvedVersionTag",
                                    table: "ContainerSnapshots");
    }

    #endregion // Methods
}