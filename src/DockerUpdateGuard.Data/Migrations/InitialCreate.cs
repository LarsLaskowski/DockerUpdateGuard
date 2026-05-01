using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DockerUpdateGuard.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    #region Methods

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "DockerInstances",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           EndpointUri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                                                           ConnectionKind = table.Column<int>(type: "integer", nullable: false),
                                                           IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                                                           Source = table.Column<int>(type: "integer", nullable: false),
                                                           SkipCertificateValidation = table.Column<bool>(type: "boolean", nullable: false),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_DockerInstances", x => x.Id);
                                                  });

        migrationBuilder.CreateTable(name: "RegistryRepositories",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Registry = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Repository = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_RegistryRepositories", x => x.Id);
                                                  });

        migrationBuilder.CreateTable(name: "PortainerEndpoints",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                                                           ExternalEndpointId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                                                           IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_PortainerEndpoints", x => x.Id);
                                                      table.ForeignKey(name: "FK_PortainerEndpoints_DockerInstances_DockerInstanceId",
                                                                       column: x => x.DockerInstanceId,
                                                                       principalTable: "DockerInstances",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                  });

        migrationBuilder.CreateTable(name: "ImageVersions",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           RegistryRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Digest = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                                                           PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                                                           Source = table.Column<int>(type: "integer", nullable: false),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ImageVersions", x => x.Id);
                                                      table.ForeignKey(name: "FK_ImageVersions_RegistryRepositories_RegistryRepositoryId",
                                                                       column: x => x.RegistryRepositoryId,
                                                                       principalTable: "RegistryRepositories",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                  });

        migrationBuilder.CreateTable(name: "ObservedImages",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                                                           IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                                                           Source = table.Column<int>(type: "integer", nullable: false),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           CurrentImageVersionId = table.Column<Guid>(type: "uuid", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ObservedImages", x => x.Id);
                                                      table.ForeignKey(name: "FK_ObservedImages_ImageVersions_CurrentImageVersionId",
                                                                       column: x => x.CurrentImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Restrict);
                                                  });

        migrationBuilder.CreateTable(name: "ScanRuns",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Type = table.Column<int>(type: "integer", nullable: false),
                                                           Status = table.Column<int>(type: "integer", nullable: false),
                                                           TriggerSource = table.Column<int>(type: "integer", nullable: false),
                                                           ObservedImageId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                                                           CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                                                           ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                                                           DiagnosticJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ScanRuns", x => x.Id);
                                                      table.ForeignKey(name: "FK_ScanRuns_DockerInstances_DockerInstanceId",
                                                                       column: x => x.DockerInstanceId,
                                                                       principalTable: "DockerInstances",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                      table.ForeignKey(name: "FK_ScanRuns_ObservedImages_ObservedImageId",
                                                                       column: x => x.ObservedImageId,
                                                                       principalTable: "ObservedImages",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                  });

        migrationBuilder.CreateTable(name: "ContainerSnapshots",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ImageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ScanRunId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           ContainerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           ComposeProject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                                                           StackName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                                                           ServiceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                                                           Status = table.Column<int>(type: "integer", nullable: false),
                                                           IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                                                           RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ContainerSnapshots", x => x.Id);
                                                      table.ForeignKey(name: "FK_ContainerSnapshots_DockerInstances_DockerInstanceId",
                                                                       column: x => x.DockerInstanceId,
                                                                       principalTable: "DockerInstances",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                      table.ForeignKey(name: "FK_ContainerSnapshots_ImageVersions_ImageVersionId",
                                                                       column: x => x.ImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Restrict);
                                                      table.ForeignKey(name: "FK_ContainerSnapshots_ScanRuns_ScanRunId",
                                                                       column: x => x.ScanRunId,
                                                                       principalTable: "ScanRuns",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                  });

        migrationBuilder.CreateTable(name: "ImageRelationships",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ChildImageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           BaseImageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ScanRunId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           RelationshipType = table.Column<int>(type: "integer", nullable: false),
                                                           Depth = table.Column<int>(type: "integer", nullable: false),
                                                           SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                                                           CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ImageRelationships", x => x.Id);
                                                      table.CheckConstraint("CK_ImageRelationships_ChildAndBaseDifferent", "\"ChildImageVersionId\" <> \"BaseImageVersionId\"");
                                                      table.CheckConstraint("CK_ImageRelationships_DepthGreaterThanZero", "\"Depth\" > 0");
                                                      table.ForeignKey(name: "FK_ImageRelationships_ImageVersions_BaseImageVersionId",
                                                                       column: x => x.BaseImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Restrict);
                                                      table.ForeignKey(name: "FK_ImageRelationships_ImageVersions_ChildImageVersionId",
                                                                       column: x => x.ChildImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                      table.ForeignKey(name: "FK_ImageRelationships_ScanRuns_ScanRunId",
                                                                       column: x => x.ScanRunId,
                                                                       principalTable: "ScanRuns",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                  });

        migrationBuilder.CreateTable(name: "VulnerabilityFindings",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ImageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ScanRunId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           AdvisoryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                                                           Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                                                           AffectedPackage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                                                           FixedVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                                                           Severity = table.Column<int>(type: "integer", nullable: false),
                                                           Source = table.Column<int>(type: "integer", nullable: false),
                                                           CvssScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                                                           IsActive = table.Column<bool>(type: "boolean", nullable: false),
                                                           Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                                                           ReferenceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                                                           DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_VulnerabilityFindings", x => x.Id);
                                                      table.ForeignKey(name: "FK_VulnerabilityFindings_ImageVersions_ImageVersionId",
                                                                       column: x => x.ImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                      table.ForeignKey(name: "FK_VulnerabilityFindings_ScanRuns_ScanRunId",
                                                                       column: x => x.ScanRunId,
                                                                       principalTable: "ScanRuns",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                  });

        migrationBuilder.CreateTable(name: "ContainerActionRuns",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           DockerInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           PortainerEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           ContainerSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           ActionType = table.Column<int>(type: "integer", nullable: false),
                                                           ResourceType = table.Column<int>(type: "integer", nullable: false),
                                                           Status = table.Column<int>(type: "integer", nullable: false),
                                                           ResourceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                                                           PortainerTaskId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                                                           ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                                                           RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_ContainerActionRuns", x => x.Id);
                                                      table.ForeignKey(name: "FK_ContainerActionRuns_ContainerSnapshots_ContainerSnapshotId",
                                                                       column: x => x.ContainerSnapshotId,
                                                                       principalTable: "ContainerSnapshots",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                      table.ForeignKey(name: "FK_ContainerActionRuns_DockerInstances_DockerInstanceId",
                                                                       column: x => x.DockerInstanceId,
                                                                       principalTable: "DockerInstances",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                      table.ForeignKey(name: "FK_ContainerActionRuns_PortainerEndpoints_PortainerEndpointId",
                                                                       column: x => x.PortainerEndpointId,
                                                                       principalTable: "PortainerEndpoints",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                  });

        migrationBuilder.CreateTable(name: "UpdateFindings",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           ScanRunId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           SubjectImageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           RecommendedImageVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           ObservedImageId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           ContainerSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                                                           Type = table.Column<int>(type: "integer", nullable: false),
                                                           IsActive = table.Column<bool>(type: "boolean", nullable: false),
                                                           Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                                                           Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                                                           DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                                                           ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_UpdateFindings", x => x.Id);
                                                      table.ForeignKey(name: "FK_UpdateFindings_ContainerSnapshots_ContainerSnapshotId",
                                                                       column: x => x.ContainerSnapshotId,
                                                                       principalTable: "ContainerSnapshots",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                      table.ForeignKey(name: "FK_UpdateFindings_ImageVersions_RecommendedImageVersionId",
                                                                       column: x => x.RecommendedImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Restrict);
                                                      table.ForeignKey(name: "FK_UpdateFindings_ImageVersions_SubjectImageVersionId",
                                                                       column: x => x.SubjectImageVersionId,
                                                                       principalTable: "ImageVersions",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Restrict);
                                                      table.ForeignKey(name: "FK_UpdateFindings_ObservedImages_ObservedImageId",
                                                                       column: x => x.ObservedImageId,
                                                                       principalTable: "ObservedImages",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.SetNull);
                                                      table.ForeignKey(name: "FK_UpdateFindings_ScanRuns_ScanRunId",
                                                                       column: x => x.ScanRunId,
                                                                       principalTable: "ScanRuns",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                  });

        migrationBuilder.CreateTable(name: "TagCandidates",
                                     columns: table => new
                                                       {
                                                           Id = table.Column<Guid>(type: "uuid", nullable: false),
                                                           UpdateFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                                                           Tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                                                           Digest = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                                                           Rank = table.Column<int>(type: "integer", nullable: false),
                                                           IsRecommended = table.Column<bool>(type: "boolean", nullable: false),
                                                           PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                                                           Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                                                       },
                                     constraints: table =>
                                                  {
                                                      table.PrimaryKey("PK_TagCandidates", x => x.Id);
                                                      table.ForeignKey(name: "FK_TagCandidates_UpdateFindings_UpdateFindingId",
                                                                       column: x => x.UpdateFindingId,
                                                                       principalTable: "UpdateFindings",
                                                                       principalColumn: "Id",
                                                                       onDelete: ReferentialAction.Cascade);
                                                  });

        migrationBuilder.CreateIndex(name: "IX_ContainerActionRuns_ContainerSnapshotId",
                                     table: "ContainerActionRuns",
                                     column: "ContainerSnapshotId");

        migrationBuilder.CreateIndex(name: "IX_ContainerActionRuns_DockerInstanceId_RequestedAtUtc",
                                     table: "ContainerActionRuns",
                                     columns: new[] { "DockerInstanceId", "RequestedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_ContainerActionRuns_PortainerEndpointId_RequestedAtUtc",
                                     table: "ContainerActionRuns",
                                     columns: new[] { "PortainerEndpointId", "RequestedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_ContainerSnapshots_DockerInstanceId_ContainerId_RecordedAtU~",
                                     table: "ContainerSnapshots",
                                     columns: new[] { "DockerInstanceId", "ContainerId", "RecordedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_ContainerSnapshots_ImageVersionId",
                                     table: "ContainerSnapshots",
                                     column: "ImageVersionId");

        migrationBuilder.CreateIndex(name: "IX_ContainerSnapshots_ScanRunId",
                                     table: "ContainerSnapshots",
                                     column: "ScanRunId");

        migrationBuilder.CreateIndex(name: "IX_DockerInstances_Name",
                                     table: "DockerInstances",
                                     column: "Name",
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_ImageRelationships_BaseImageVersionId",
                                     table: "ImageRelationships",
                                     column: "BaseImageVersionId");

        migrationBuilder.CreateIndex(name: "IX_ImageRelationships_ChildImageVersionId",
                                     table: "ImageRelationships",
                                     column: "ChildImageVersionId");

        migrationBuilder.CreateIndex(name: "IX_ImageRelationships_ChildImageVersionId_BaseImageVersionId_D~",
                                     table: "ImageRelationships",
                                     columns: new[] { "ChildImageVersionId", "BaseImageVersionId", "Depth", "RelationshipType" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_ImageRelationships_ScanRunId",
                                     table: "ImageRelationships",
                                     column: "ScanRunId");

        migrationBuilder.CreateIndex(name: "IX_ImageVersions_RegistryRepositoryId_Tag_Digest",
                                     table: "ImageVersions",
                                     columns: new[] { "RegistryRepositoryId", "Tag", "Digest" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_ObservedImages_CurrentImageVersionId",
                                     table: "ObservedImages",
                                     column: "CurrentImageVersionId");

        migrationBuilder.CreateIndex(name: "IX_PortainerEndpoints_DockerInstanceId",
                                     table: "PortainerEndpoints",
                                     column: "DockerInstanceId",
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_RegistryRepositories_Registry_Repository",
                                     table: "RegistryRepositories",
                                     columns: new[] { "Registry", "Repository" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_ScanRuns_DockerInstanceId",
                                     table: "ScanRuns",
                                     column: "DockerInstanceId");

        migrationBuilder.CreateIndex(name: "IX_ScanRuns_ObservedImageId",
                                     table: "ScanRuns",
                                     column: "ObservedImageId");

        migrationBuilder.CreateIndex(name: "IX_ScanRuns_Type_Status_StartedAtUtc",
                                     table: "ScanRuns",
                                     columns: new[] { "Type", "Status", "StartedAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_TagCandidates_UpdateFindingId_Tag_Digest",
                                     table: "TagCandidates",
                                     columns: new[] { "UpdateFindingId", "Tag", "Digest" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_UpdateFindings_ContainerSnapshotId_IsActive",
                                     table: "UpdateFindings",
                                     columns: new[] { "ContainerSnapshotId", "IsActive" });

        migrationBuilder.CreateIndex(name: "IX_UpdateFindings_ObservedImageId_IsActive",
                                     table: "UpdateFindings",
                                     columns: new[] { "ObservedImageId", "IsActive" });

        migrationBuilder.CreateIndex(name: "IX_UpdateFindings_RecommendedImageVersionId",
                                     table: "UpdateFindings",
                                     column: "RecommendedImageVersionId");

        migrationBuilder.CreateIndex(name: "IX_UpdateFindings_ScanRunId",
                                     table: "UpdateFindings",
                                     column: "ScanRunId");

        migrationBuilder.CreateIndex(name: "IX_UpdateFindings_SubjectImageVersionId_IsActive",
                                     table: "UpdateFindings",
                                     columns: new[] { "SubjectImageVersionId", "IsActive" });

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ImageVersionId_AdvisoryId_AffectedPac~",
                                     table: "VulnerabilityFindings",
                                     columns: new[] { "ImageVersionId", "AdvisoryId", "AffectedPackage", "FixedVersion" },
                                     unique: true);

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ImageVersionId_Severity_IsActive",
                                     table: "VulnerabilityFindings",
                                     columns: new[] { "ImageVersionId", "Severity", "IsActive" });

        migrationBuilder.CreateIndex(name: "IX_VulnerabilityFindings_ScanRunId",
                                     table: "VulnerabilityFindings",
                                     column: "ScanRunId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ContainerActionRuns");

        migrationBuilder.DropTable(name: "ImageRelationships");

        migrationBuilder.DropTable(name: "TagCandidates");

        migrationBuilder.DropTable(name: "VulnerabilityFindings");

        migrationBuilder.DropTable(name: "PortainerEndpoints");

        migrationBuilder.DropTable(name: "UpdateFindings");

        migrationBuilder.DropTable(name: "ContainerSnapshots");

        migrationBuilder.DropTable(name: "ScanRuns");

        migrationBuilder.DropTable(name: "DockerInstances");

        migrationBuilder.DropTable(name: "ObservedImages");

        migrationBuilder.DropTable(name: "ImageVersions");

        migrationBuilder.DropTable(name: "RegistryRepositories");
    }

    #endregion // Methods
}