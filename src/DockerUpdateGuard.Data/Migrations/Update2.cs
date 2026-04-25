using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations
{
    /// <inheritdoc />
    public partial class Update2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "DockerInstanceResourceSamples",
                                         columns: table => new
                                                           {
                                                               Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                               DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                               ContainerCount = table.Column<int>(type: "integer", nullable: false),
                                                               CpuPercent = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                                                               MemoryUsageBytes = table.Column<long>(type: "bigint", nullable: false),
                                                               MemoryLimitBytes = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkRxBytesTotal = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkTxBytesTotal = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkRxBytesPerSecond = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                                                               NetworkTxBytesPerSecond = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                                                               RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                           },
                                         constraints: table =>
                                                      {
                                                          table.PrimaryKey("PK_DockerInstanceResourceSamples", x => x.Id);
                                                          table.ForeignKey(name: "FK_DockerInstanceResourceSamples_DockerInstances_DockerInstanc~",
                                                                           column: x => x.DockerInstanceId,
                                                                           principalTable: "DockerInstances",
                                                                           principalColumn: "Id",
                                                                           onDelete: ReferentialAction.Cascade);
                                                      });

            migrationBuilder.CreateTable(name: "RuntimeContainerResourceSamples",
                                         columns: table => new
                                                           {
                                                               Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                               DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                               ContainerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                               ContainerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                               CpuPercent = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                                                               MemoryUsageBytes = table.Column<long>(type: "bigint", nullable: false),
                                                               MemoryLimitBytes = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkRxBytesTotal = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkTxBytesTotal = table.Column<long>(type: "bigint", nullable: false),
                                                               NetworkRxBytesPerSecond = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                                                               NetworkTxBytesPerSecond = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                                                               RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                           },
                                         constraints: table =>
                                                      {
                                                          table.PrimaryKey("PK_RuntimeContainerResourceSamples", x => x.Id);
                                                          table.ForeignKey(name: "FK_RuntimeContainerResourceSamples_DockerInstances_DockerInsta~",
                                                                           column: x => x.DockerInstanceId,
                                                                           principalTable: "DockerInstances",
                                                                           principalColumn: "Id",
                                                                           onDelete: ReferentialAction.Cascade);
                                                      });

            migrationBuilder.CreateIndex(name: "IX_DockerInstanceResourceSamples_DockerInstanceId_RecordedAtUtc",
                                         table: "DockerInstanceResourceSamples",
                                         columns: new[] { "DockerInstanceId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(name: "IX_RuntimeContainerResourceSamples_DockerInstanceId_ContainerI~",
                                         table: "RuntimeContainerResourceSamples",
                                         columns: new[] { "DockerInstanceId", "ContainerId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DockerInstanceResourceSamples");

            migrationBuilder.DropTable(name: "RuntimeContainerResourceSamples");
        }
    }
}