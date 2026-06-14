# DockerUpdateGuard – Review Matrix

Central tracking table, originally generated from `git ls-files` (**330 rows**).
**332 rows** after the P8 correction: `.github/workflows/ci.yml` + `release.yml` added
(rows `4a`/`4b`), `azure-pipelines.yml` (row 19) deleted in commit fc81f4f and marked as
"removed" (CI/CD migration Azure DevOps → GitHub Actions, see F-036).
Each file belongs to exactly **one phase** (P1–P8, see [README.md](README.md)).

**Status:** ⬜ open · 🔬 in deep dive · ✅ reviewed
**Traffic light per focus area:** 🟢 ok · 🟡 note · 🔴 finding · — n/a
**Findings:** short reference to [findings.md](findings.md), e.g. `F-012`.

| # | Phase | File | Module | Status | Sec. | Correct. | Arch. | Tests | Findings |
|---|-------|-------|-------|--------|---------|----------|-------|-------|---------|
| 1 | P8 | `.dockerignore` | Root | ✅ | 🟡 | 🟢 | — | — | F-038 |
| 2 | P8 | `.editorconfig` | Root | ✅ | — | 🟢 | — | — |  |
| 3 | P8 | `.github/copilot-instructions.md` | .github | ✅ | — | 🟡 | — | — | F-035 |
| 4 | P8 | `.github/instructions/csharp.instructions.md` | .github | ✅ | — | 🟢 | — | — |  |
| 4a | P8 | `.github/workflows/ci.yml` | .github/workflows | ✅ | 🟢 | 🟡 | 🟢 | 🟢 | F-037 |
| 4b | P8 | `.github/workflows/release.yml` | .github/workflows | ✅ | 🟢 | 🟠 | 🟢 | 🟢 | F-033, F-037 |
| 5 | P8 | `.gitignore` | Root | ✅ | 🟢 | 🟢 | — | — |  |
| 6 | P8 | `.serena/.gitignore` | .serena | ✅ | 🟢 | 🟢 | — | — |  |
| 7 | P8 | `.serena/memories/project_overview.md` | .serena | ✅ | — | 🟢 | — | — |  |
| 8 | P8 | `.serena/memories/style_and_conventions.md` | .serena | ✅ | — | 🟢 | — | — |  |
| 9 | P8 | `.serena/memories/suggested_commands.md` | .serena | ✅ | — | 🟢 | — | — |  |
| 10 | P8 | `.serena/memories/task_completion.md` | .serena | ✅ | — | 🟢 | — | — |  |
| 11 | P8 | `.serena/project.yml` | .serena | ✅ | 🟢 | 🟢 | — | — |  |
| 12 | P8 | `DOCKER.md` | Root | ✅ | — | 🟢 | — | — |  |
| 13 | P8 | `Directory.Build.props` | Root | ✅ | — | 🟢 | 🟢 | — |  |
| 14 | P8 | `Directory.Packages.props` | Root | ✅ | — | 🟢 | 🟢 | — |  |
| 15 | P8 | `DockerUpdateGuard.slnx` | Root | ✅ | — | 🟡 | 🟢 | — | F-036 |
| 16 | P8 | `LICENSE.md` | Root | ✅ | — | 🟡 | — | — | F-034 |
| 17 | P8 | `README.md` | Root | ✅ | — | 🟡 | — | — | F-034, F-005, F-006 |
| 18 | P8 | `SharedAssemblyInfo.cs` | Root | ✅ | — | 🟢 | 🟢 | — | F-039 |
| 19 | P8 | `azure-pipelines.yml` | Root | ✅ | — | — | — | — | F-036 (file removed → `.github/workflows/`) |
| 20 | P8 | `rules/DockerUpdateGuard.Debug.ruleset` | rules | ✅ | — | 🟢 | 🟢 | — |  |
| 21 | P8 | `rules/DockerUpdateGuard.Release.ruleset` | rules | ✅ | — | 🟢 | 🟢 | — |  |
| 22 | P4 | `src/DockerUpdateGuard.Data/Configurations/ContainerActionRunConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 23 | P4 | `src/DockerUpdateGuard.Data/Configurations/ContainerSnapshotConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 24 | P4 | `src/DockerUpdateGuard.Data/Configurations/DockerInstanceConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 25 | P4 | `src/DockerUpdateGuard.Data/Configurations/DockerInstanceResourceSampleConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 26 | P4 | `src/DockerUpdateGuard.Data/Configurations/ImageRelationshipConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 27 | P4 | `src/DockerUpdateGuard.Data/Configurations/ImageVersionConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 28 | P4 | `src/DockerUpdateGuard.Data/Configurations/ObservedImageConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 29 | P4 | `src/DockerUpdateGuard.Data/Configurations/PortainerEndpointConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 30 | P4 | `src/DockerUpdateGuard.Data/Configurations/RegistryRepositoryConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 31 | P4 | `src/DockerUpdateGuard.Data/Configurations/RuntimeContainerResourceSampleConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 32 | P4 | `src/DockerUpdateGuard.Data/Configurations/RuntimeContainerTagSelectionConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 33 | P4 | `src/DockerUpdateGuard.Data/Configurations/ScanRunConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 34 | P4 | `src/DockerUpdateGuard.Data/Configurations/TagCandidateConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 35 | P4 | `src/DockerUpdateGuard.Data/Configurations/UpdateFindingConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 36 | P4 | `src/DockerUpdateGuard.Data/Configurations/VulnerabilityFindingConfiguration.cs` | Data/Configurations | ✅ | 🟢 | 🟠 | 🟢 | — | F-021 |
| 37 | P4 | `src/DockerUpdateGuard.Data/Design/DockerUpdateGuardDbContextFactory.cs` | Data/Design | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 38 | P4 | `src/DockerUpdateGuard.Data/DockerUpdateGuard.Data.csproj` | Data (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 39 | P4 | `src/DockerUpdateGuard.Data/DockerUpdateGuardDbContext.cs` | Data (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 40 | P4 | `src/DockerUpdateGuard.Data/Entities/ContainerActionRun.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 41 | P4 | `src/DockerUpdateGuard.Data/Entities/ContainerActionStatus.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 42 | P4 | `src/DockerUpdateGuard.Data/Entities/ContainerActionType.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 43 | P4 | `src/DockerUpdateGuard.Data/Entities/ContainerRuntimeStatus.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 44 | P4 | `src/DockerUpdateGuard.Data/Entities/ContainerSnapshot.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 45 | P4 | `src/DockerUpdateGuard.Data/Entities/DockerConnectionKind.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 46 | P4 | `src/DockerUpdateGuard.Data/Entities/DockerInstance.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 47 | P4 | `src/DockerUpdateGuard.Data/Entities/DockerInstanceResourceSample.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 48 | P4 | `src/DockerUpdateGuard.Data/Entities/ImageRelationship.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 49 | P4 | `src/DockerUpdateGuard.Data/Entities/ImageRelationshipType.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 50 | P4 | `src/DockerUpdateGuard.Data/Entities/ImageVersion.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 51 | P4 | `src/DockerUpdateGuard.Data/Entities/ImageVersionSource.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 52 | P4 | `src/DockerUpdateGuard.Data/Entities/ObservedImage.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 53 | P4 | `src/DockerUpdateGuard.Data/Entities/PortainerEndpoint.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 54 | P4 | `src/DockerUpdateGuard.Data/Entities/PortainerResourceType.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 55 | P4 | `src/DockerUpdateGuard.Data/Entities/RegistrationSource.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 56 | P4 | `src/DockerUpdateGuard.Data/Entities/RegistryRepository.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 57 | P4 | `src/DockerUpdateGuard.Data/Entities/RuntimeContainerResourceSample.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 58 | P4 | `src/DockerUpdateGuard.Data/Entities/RuntimeContainerTagSelection.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 59 | P4 | `src/DockerUpdateGuard.Data/Entities/ScanRun.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 60 | P4 | `src/DockerUpdateGuard.Data/Entities/ScanRunStatus.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 61 | P4 | `src/DockerUpdateGuard.Data/Entities/ScanRunType.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 62 | P4 | `src/DockerUpdateGuard.Data/Entities/ScanTriggerSource.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 63 | P4 | `src/DockerUpdateGuard.Data/Entities/TagCandidate.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 64 | P4 | `src/DockerUpdateGuard.Data/Entities/UpdateAssessmentStatus.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 65 | P4 | `src/DockerUpdateGuard.Data/Entities/UpdateFinding.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 66 | P4 | `src/DockerUpdateGuard.Data/Entities/UpdateFindingType.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 67 | P4 | `src/DockerUpdateGuard.Data/Entities/VulnerabilityAssessmentStatus.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 68 | P4 | `src/DockerUpdateGuard.Data/Entities/VulnerabilityFinding.cs` | Data/Entities | ✅ | 🟢 | 🟡 | 🟢 | — | F-021 |
| 69 | P4 | `src/DockerUpdateGuard.Data/Entities/VulnerabilitySeverity.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 70 | P4 | `src/DockerUpdateGuard.Data/Entities/VulnerabilitySource.cs` | Data/Entities | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 71 | P4 | `src/DockerUpdateGuard.Data/Migrations/DockerUpdateGuardDbContextModelSnapshot.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 72 | P4 | `src/DockerUpdateGuard.Data/Migrations/InitialCreate.Designer.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 73 | P4 | `src/DockerUpdateGuard.Data/Migrations/InitialCreate.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 74 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update1.Designer.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 75 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update1.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 76 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update2.Designer.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 77 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update2.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 78 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update3.Designer.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 79 | P4 | `src/DockerUpdateGuard.Data/Migrations/Update3.cs` | Data/Migrations | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 80 | P4 | `src/DockerUpdateGuard.Data/Queries/ISharedBaseImageQueryService.cs` | Data/Queries | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 81 | P4 | `src/DockerUpdateGuard.Data/Queries/ObservedImageReferenceData.cs` | Data/Queries | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 82 | P4 | `src/DockerUpdateGuard.Data/Queries/SharedBaseImageQueryService.cs` | Data/Queries | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 83 | P4 | `src/DockerUpdateGuard.Data/Queries/SharedBaseImageUsageData.cs` | Data/Queries | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 84 | P4 | `src/DockerUpdateGuard.Data/Repositories/IImageCatalogRepository.cs` | Data/Repositories | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 85 | P4 | `src/DockerUpdateGuard.Data/Repositories/ImageCatalogRepository.cs` | Data/Repositories | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 86 | P4 | `src/DockerUpdateGuard.Data/ServiceCollectionExtensions.cs` | Data (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 87 | P6 | `src/DockerUpdateGuard.Telemetry/DockerUpdateGuard.Telemetry.csproj` | Telemetry (root) | ✅ | — | 🟢 | 🟢 | — | F-039 |
| 88 | P6 | `src/DockerUpdateGuard.Telemetry/DockerUpdateGuardTelemetry.cs` | Telemetry (root) | ✅ | — | 🟢 | 🟢 | — |  |
| 89 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryActivityNames.cs` | Telemetry (root) | ✅ | — | — | 🟡 | — | F-040 |
| 90 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryLogPropertyNames.cs` | Telemetry (root) | ✅ | — | — | 🟡 | — | F-041 |
| 91 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryMetricNames.cs` | Telemetry (root) | ✅ | — | — | 🟢 | — |  |
| 92 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryOptions.cs` | Telemetry (root) | ✅ | — | — | 🟢 | — |  |
| 93 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryOptionsValidator.cs` | Telemetry (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 94 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryResourceAttributeNames.cs` | Telemetry (root) | ✅ | — | — | 🟡 | — | F-044 |
| 95 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryServiceCollectionExtensions.cs` | Telemetry (root) | ✅ | 🟢 | 🟢 | 🟡 | — | F-044 |
| 96 | P6 | `src/DockerUpdateGuard.Telemetry/TelemetryTagNames.cs` | Telemetry (root) | ✅ | — | — | 🟡 | — | F-040 |
| 97 | P6 | `src/DockerUpdateGuard/ApplicationInitializationExtensions.cs` | App (root) | ✅ | 🟠 | 🟡 | 🟢 | — | F-042, F-043 |
| 98 | P6 | `src/DockerUpdateGuard/ApplicationTelemetry.cs` | App (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 99 | P5 | `src/DockerUpdateGuard/Components/App.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 100 | P5 | `src/DockerUpdateGuard/Components/Layout/MainLayout.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 101 | P5 | `src/DockerUpdateGuard/Components/Layout/MainLayout.razor.cs` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035 |
| 102 | P5 | `src/DockerUpdateGuard/Components/Layout/NavMenu.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 103 | P5 | `src/DockerUpdateGuard/Components/Layout/NavMenu.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 104 | P5 | `src/DockerUpdateGuard/Components/Pages/Dashboard.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035 |
| 105 | P5 | `src/DockerUpdateGuard/Components/Pages/Dashboard.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 106 | P5 | `src/DockerUpdateGuard/Components/Pages/DockerInstanceDetail.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-036 |
| 107 | P5 | `src/DockerUpdateGuard/Components/Pages/DockerInstanceDetail.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 108 | P5 | `src/DockerUpdateGuard/Components/Pages/DockerInstances.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035 |
| 109 | P5 | `src/DockerUpdateGuard/Components/Pages/DockerInstances.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 110 | P5 | `src/DockerUpdateGuard/Components/Pages/Error.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 111 | P5 | `src/DockerUpdateGuard/Components/Pages/MyImageDetail.razor` | App/Components | ✅ | 🟡 | 🟡 | 🟢 | — | F-034, F-035, F-036 |
| 112 | P5 | `src/DockerUpdateGuard/Components/Pages/MyImageDetail.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 113 | P5 | `src/DockerUpdateGuard/Components/Pages/MyImages.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 114 | P5 | `src/DockerUpdateGuard/Components/Pages/MyImages.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 115 | P5 | `src/DockerUpdateGuard/Components/Pages/NotFound.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 116 | P5 | `src/DockerUpdateGuard/Components/Pages/ObservedImageDetail.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035, F-036 |
| 117 | P5 | `src/DockerUpdateGuard/Components/Pages/ObservedImageDetail.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 118 | P5 | `src/DockerUpdateGuard/Components/Pages/ObservedImages.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 119 | P5 | `src/DockerUpdateGuard/Components/Pages/ObservedImages.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟡 | — | F-032 |
| 120 | P5 | `src/DockerUpdateGuard/Components/Pages/RuntimeContainerDetail.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035, F-036 |
| 121 | P5 | `src/DockerUpdateGuard/Components/Pages/RuntimeContainerDetail.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 122 | P5 | `src/DockerUpdateGuard/Components/Pages/RuntimeContainers.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 123 | P5 | `src/DockerUpdateGuard/Components/Pages/RuntimeContainers.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟡 | — | F-032 |
| 124 | P5 | `src/DockerUpdateGuard/Components/Pages/ScanHistory.razor` | App/Components | ✅ | 🟢 | 🟡 | 🟢 | — | F-035 |
| 125 | P5 | `src/DockerUpdateGuard/Components/Pages/ScanHistory.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 126 | P5 | `src/DockerUpdateGuard/Components/Pages/SharedBaseImages.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 127 | P5 | `src/DockerUpdateGuard/Components/Pages/SharedBaseImages.razor.cs` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 128 | P5 | `src/DockerUpdateGuard/Components/Routes.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 129 | P5 | `src/DockerUpdateGuard/Components/_Imports.razor` | App/Components | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 130 | P1 | `src/DockerUpdateGuard/Configuration/DockerHubOptions.cs` | App/Configuration | ✅ | 🟢 | 🟡 | 🟡 | — | F-005 |
| 131 | P1 | `src/DockerUpdateGuard/Configuration/DockerInstanceOptions.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 132 | P1 | `src/DockerUpdateGuard/Configuration/DockerUpdateGuardConnectionStringResolver.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 133 | P1 | `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptions.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 134 | P1 | `src/DockerUpdateGuard/Configuration/DockerUpdateGuardOptionsValidator.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟡 | 🟢 | F-007 |
| 135 | P1 | `src/DockerUpdateGuard/Configuration/PortainerOptions.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 136 | P1 | `src/DockerUpdateGuard/Configuration/ScanningOptions.cs` | App/Configuration | ✅ | 🟡 | 🟢 | 🟡 | — | F-006 |
| 137 | P1 | `src/DockerUpdateGuard/Configuration/VulnerabilityOptions.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 138 | P1 | `src/DockerUpdateGuard/Configuration/VulnerabilityProviderKind.cs` | App/Configuration | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 139 | P1 | `src/DockerUpdateGuard/Docker/DockerImageHistoryEntryData.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 140 | P1 | `src/DockerUpdateGuard/Docker/DockerImageInspectData.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 141 | P1 | `src/DockerUpdateGuard/Docker/DockerInstanceClient.cs` | App/Docker | ✅ | 🟡 | 🟡 | 🟢 | 🟢 | F-001, F-002, F-012 |
| 142 | P1 | `src/DockerUpdateGuard/Docker/DockerInstanceClientLogging.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 143 | P1 | `src/DockerUpdateGuard/Docker/IDockerInstanceClient.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 144 | P1 | `src/DockerUpdateGuard/Docker/RuntimeContainerDescriptor.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 145 | P1 | `src/DockerUpdateGuard/Docker/RuntimeContainerResourceDescriptor.cs` | App/Docker | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 146 | P1 | `src/DockerUpdateGuard/DockerHub/BaseImageDescriptor.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 147 | P1 | `src/DockerUpdateGuard/DockerHub/DockerHubAuthenticatedUserData.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 148 | P1 | `src/DockerUpdateGuard/DockerHub/DockerHubClient.cs` | App/DockerHub | ✅ | 🟡 | 🟡 | 🟡 | 🟢 | F-008, F-009, F-012 |
| 149 | P1 | `src/DockerUpdateGuard/DockerHub/DockerHubClientLogging.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 150 | P1 | `src/DockerUpdateGuard/DockerHub/DockerHubRepositoryData.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 151 | P1 | `src/DockerUpdateGuard/DockerHub/DockerHubTagData.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 152 | P1 | `src/DockerUpdateGuard/DockerHub/IDockerHubClient.cs` | App/DockerHub | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 153 | P8 | `src/DockerUpdateGuard/DockerUpdateGuard.csproj` | App (root) | ✅ | — | 🟡 | 🟢 | — | F-039 |
| 154 | P8 | `src/DockerUpdateGuard/Dockerfile` | App (root) | ✅ | 🟡 | 🟠 | 🟢 | — | F-032, F-033 |
| 155 | P6 | `src/DockerUpdateGuard/HostLoggingExtensions.cs` | App (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 156 | P3 | `src/DockerUpdateGuard/Images/Data/DerivedBaseRuntimeDescriptor.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 157 | P3 | `src/DockerUpdateGuard/Images/Data/DotNetChannelReleaseData.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 158 | P3 | `src/DockerUpdateGuard/Images/Data/ImageReference.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 159 | P3 | `src/DockerUpdateGuard/Images/Data/ManifestMetadata.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 160 | P3 | `src/DockerUpdateGuard/Images/Data/NginxChannelReleaseData.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 161 | P3 | `src/DockerUpdateGuard/Images/Data/ObservedImageRegistrationRequest.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 162 | P3 | `src/DockerUpdateGuard/Images/Data/RegistryImageConfigurationData.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 163 | P3 | `src/DockerUpdateGuard/Images/Data/RegistryTagQueryOptions.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 164 | P3 | `src/DockerUpdateGuard/Images/Data/RuntimeCandidate.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 165 | P3 | `src/DockerUpdateGuard/Images/Data/UpdateCandidateData.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 166 | P3 | `src/DockerUpdateGuard/Images/Data/UpdateEvaluationResult.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 167 | P3 | `src/DockerUpdateGuard/Images/Data/VersionTagCandidateData.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 168 | P3 | `src/DockerUpdateGuard/Images/DerivedBaseRuntimeDetector.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 169 | P3 | `src/DockerUpdateGuard/Images/DockerHubAccountImageDiscoveryBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 170 | P3 | `src/DockerUpdateGuard/Images/DockerHubAccountImageDiscoveryService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 171 | P3 | `src/DockerUpdateGuard/Images/DockerHubBaseImageResolver.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟡 | — | F-031 |
| 172 | P3 | `src/DockerUpdateGuard/Images/DockerInstanceDiscoveryBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 173 | P3 | `src/DockerUpdateGuard/Images/DotNetReleaseMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 174 | P3 | `src/DockerUpdateGuard/Images/Enums/DerivedBaseRuntimeDetectionSource.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 175 | P3 | `src/DockerUpdateGuard/Images/Enums/DerivedBaseRuntimeKind.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 176 | P3 | `src/DockerUpdateGuard/Images/Enums/UpdateEvaluationStatus.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 177 | P3 | `src/DockerUpdateGuard/Images/Helper/RegistryTagQueryHelper.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 178 | P3 | `src/DockerUpdateGuard/Images/Helper/UpdateFindingPersistenceHelper.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 179 | P3 | `src/DockerUpdateGuard/Images/Helper/VersionTagResolutionHelper.cs` | App/Images | ✅ | 🟢 | 🔴 | 🟢 | 🟢 | F-021, F-022 |
| 180 | P3 | `src/DockerUpdateGuard/Images/ImageHostLoggingExtensions.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 181 | P3 | `src/DockerUpdateGuard/Images/ImageReferenceParser.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 182 | P3 | `src/DockerUpdateGuard/Images/ImageRegistrationService.cs` | App/Images | ✅ | 🟢 | 🟡 | 🟢 | 🟢 | F-029 |
| 183 | P3 | `src/DockerUpdateGuard/Images/ImageScanOrchestrator.cs` | App/Images | ✅ | 🟡 | 🔴 | 🟢 | 🟢 | F-024, F-025, F-030 |
| 184 | P3 | `src/DockerUpdateGuard/Images/InstanceDiscoveryService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 185 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IBaseImageResolver.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 186 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IDerivedBaseRuntimeDetector.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 187 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IDockerHubAccountImageDiscoveryService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 188 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IDotNetReleaseMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 189 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IImageReferenceParser.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 190 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IImageRegistrationService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 191 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IImageScanOrchestrator.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 192 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IInstanceDiscoveryService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 193 | P3 | `src/DockerUpdateGuard/Images/Interfaces/INginxReleaseMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 194 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IRegistryMetadataClient.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 195 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IRegistryMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 196 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IResourceStatisticsCollector.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 197 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IRuntimeContainerScanOrchestrator.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 198 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IUpdateDetectionService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 199 | P3 | `src/DockerUpdateGuard/Images/Interfaces/IVulnerabilityEnrichmentService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 200 | P3 | `src/DockerUpdateGuard/Images/NginxReleaseMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 201 | P3 | `src/DockerUpdateGuard/Images/NullRegistryMetadataClient.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 202 | P3 | `src/DockerUpdateGuard/Images/ObservedImageScanIntervalCalculator.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 203 | P3 | `src/DockerUpdateGuard/Images/OciRegistryClient.cs` | App/Images | ✅ | 🟢 | 🔴 | 🟢 | 🟢 | F-023 |
| 204 | P3 | `src/DockerUpdateGuard/Images/OwnImageBaseRefreshBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 205 | P3 | `src/DockerUpdateGuard/Images/RegistryBaseImageResolver.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | 🟢 | |
| 206 | P3 | `src/DockerUpdateGuard/Images/RegistryMetadataService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 207 | P3 | `src/DockerUpdateGuard/Images/ResourceStatisticsCollector.cs` | App/Images | ✅ | 🟢 | 🔴 | 🟢 | 🟡 | F-026 |
| 208 | P3 | `src/DockerUpdateGuard/Images/ResourceStatisticsRefreshBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 209 | P3 | `src/DockerUpdateGuard/Images/RuntimeContainerRefreshBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 210 | P3 | `src/DockerUpdateGuard/Images/RuntimeContainerScanOrchestrator.cs` | App/Images | ✅ | 🟡 | 🔴 | 🟢 | 🟢 | F-024, F-025 |
| 211 | P3 | `src/DockerUpdateGuard/Images/ScanCleanupBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟡 | 🟢 | 🟢 | F-027 |
| 212 | P3 | `src/DockerUpdateGuard/Images/ScheduledBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 213 | P3 | `src/DockerUpdateGuard/Images/UpdateDetectionService.cs` | App/Images | ✅ | 🟢 | 🟡 | 🟢 | 🟢 | F-028, F-021, F-022 |
| 214 | P3 | `src/DockerUpdateGuard/Images/VulnerabilityEnrichmentService.cs` | App/Images | ✅ | 🟢 | 🟡 | 🟢 | 🟢 | F-015, F-017 |
| 215 | P3 | `src/DockerUpdateGuard/Images/VulnerabilityRefreshBackgroundService.cs` | App/Images | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 216 | P6 | `src/DockerUpdateGuard/Infrastructure/ExternalOperationResult{T}.cs` | App/Infrastructure | ✅ | — | 🟢 | 🟢 | — |  |
| 217 | P6 | `src/DockerUpdateGuard/Infrastructure/ExternalOperationStatus.cs` | App/Infrastructure | ✅ | — | — | 🟢 | — |  |
| 218 | P1 | `src/DockerUpdateGuard/Portainer/Data/DockerContainerItem.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 219 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerActionRequest.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 220 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerActionResult.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 221 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerAuthResponse.cs` | App/Portainer | ✅ | 🟡 | 🟢 | 🟢 | — | F-004 |
| 222 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerCapabilityData.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 223 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerEndpointItem.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 224 | P1 | `src/DockerUpdateGuard/Portainer/Data/PortainerLoginRequest.cs` | App/Portainer | ✅ | 🟡 | 🟢 | 🟢 | — | F-004 |
| 225 | P1 | `src/DockerUpdateGuard/Portainer/Interfaces/IPortainerClient.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 226 | P1 | `src/DockerUpdateGuard/Portainer/PortainerClient.cs` | App/Portainer | ✅ | 🟡 | 🟡 | 🟢 | 🟡 | F-003, F-010, F-011 |
| 227 | P1 | `src/DockerUpdateGuard/Portainer/PortainerClientLogging.cs` | App/Portainer | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 228 | P6 | `src/DockerUpdateGuard/Program.cs` | App (root) | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 229 | P8 | `src/DockerUpdateGuard/Properties/launchSettings.json` | App/Properties | ✅ | 🟢 | 🟢 | 🟢 | — |  |
| 230 | P6 | `src/DockerUpdateGuard/ServiceCollectionExtensions.cs` | App (root) | ✅ | 🟢 | 🟢 | 🟡 | — | F-020, F-045 |
| 231 | P5 | `src/DockerUpdateGuard/UI/ApplicationViewService.cs` | App/UI | ✅ | 🟢 | 🟠 | 🟡 | — | F-032, F-033 |
| 232 | P5 | `src/DockerUpdateGuard/UI/BaseImageRelationshipData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 233 | P5 | `src/DockerUpdateGuard/UI/DashboardRefreshState.cs` | App/UI | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 234 | P5 | `src/DockerUpdateGuard/UI/DashboardViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 235 | P5 | `src/DockerUpdateGuard/UI/DockerInstanceDetailViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 236 | P5 | `src/DockerUpdateGuard/UI/DockerInstanceListItemData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 237 | P5 | `src/DockerUpdateGuard/UI/IApplicationViewService.cs` | App/UI | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 238 | P5 | `src/DockerUpdateGuard/UI/IRuntimeContainerTagSelectionService.cs` | App/UI | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 239 | P5 | `src/DockerUpdateGuard/UI/ImageReferenceFormatter.cs` | App/UI | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 240 | P5 | `src/DockerUpdateGuard/UI/LinkedRuntimeContainerViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 241 | P5 | `src/DockerUpdateGuard/UI/ObservedImageDetailViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 242 | P5 | `src/DockerUpdateGuard/UI/ObservedImageListItemData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 243 | P5 | `src/DockerUpdateGuard/UI/ResourceUsageChartBuilder.cs` | App/UI | ✅ | 🟢 | 🟡 | 🟢 | — | F-035 |
| 244 | P5 | `src/DockerUpdateGuard/UI/ResourceUsageFormatter.cs` | App/UI | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 245 | P5 | `src/DockerUpdateGuard/UI/ResourceUsagePointViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 246 | P5 | `src/DockerUpdateGuard/UI/RuntimeContainerDetailViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 247 | P5 | `src/DockerUpdateGuard/UI/RuntimeContainerListItemData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 248 | P5 | `src/DockerUpdateGuard/UI/RuntimeContainerTagSelectionService.cs` | App/UI | ✅ | 🟢 | 🟡 | 🟡 | — | F-032 |
| 249 | P5 | `src/DockerUpdateGuard/UI/ScanHistoryItemData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 250 | P5 | `src/DockerUpdateGuard/UI/SharedBaseImageListItemData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 251 | P5 | `src/DockerUpdateGuard/UI/TagCandidateViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 252 | P5 | `src/DockerUpdateGuard/UI/UpdateFindingViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 253 | P5 | `src/DockerUpdateGuard/UI/VulnerabilityAssessmentViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 254 | P5 | `src/DockerUpdateGuard/UI/VulnerabilityFindingViewData.cs` | App/UI | ✅ | — | — | 🟢 | — | |
| 255 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginRequest.cs` | App/Vulnerabilities | ✅ | 🟡 | 🟢 | 🟢 | — | F-018 |
| 256 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/HubLoginResponse.cs` | App/Vulnerabilities | ✅ | 🟡 | 🟢 | 🟢 | — | F-018 |
| 257 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/ScoutVulnerabilityItem.cs` | App/Vulnerabilities | ✅ | 🟢 | 🔴 | 🟢 | — | F-015 |
| 258 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/ScoutVulnerabilityResponse.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 259 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyArtifact.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 260 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyResult.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 261 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyScanOptions.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 262 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyScanRequest.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 263 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyScanResponse.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 264 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Data/TrivyVulnerability.cs` | App/Vulnerabilities | ✅ | 🟢 | 🔴 | 🟢 | — | F-015 |
| 265 | P2 | `src/DockerUpdateGuard/Vulnerabilities/DefaultVulnerabilityProvider.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | 🟡 | F-016 |
| 266 | P2 | `src/DockerUpdateGuard/Vulnerabilities/DefaultVulnerabilityProviderLogging.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 267 | P2 | `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProvider.cs` | App/Vulnerabilities | ✅ | 🟡 | 🔴 | 🟢 | 🔴 | F-013, F-014, F-016, F-017 |
| 268 | P2 | `src/DockerUpdateGuard/Vulnerabilities/DockerScoutVulnerabilityProviderLogging.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 269 | P2 | `src/DockerUpdateGuard/Vulnerabilities/Interfaces/IVulnerabilityProvider.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 270 | P2 | `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProvider.cs` | App/Vulnerabilities | ✅ | 🟡 | 🟡 | 🟢 | 🟡 | F-015, F-016, F-017, F-019 |
| 271 | P2 | `src/DockerUpdateGuard/Vulnerabilities/TrivyVulnerabilityProviderLogging.cs` | App/Vulnerabilities | ✅ | 🟢 | 🟢 | 🟢 | — | |
| 272 | P2 | `src/DockerUpdateGuard/Vulnerabilities/VulnerabilityAdvisoryData.cs` | App/Vulnerabilities | ✅ | 🟢 | 🔴 | 🟢 | — | F-015 |
| 273 | P8 | `src/DockerUpdateGuard/appsettings.json` | App (root) | ✅ | 🟢 | 🟡 | 🟢 | — | F-005, F-006 |
| 274 | P8 | `src/DockerUpdateGuard/entrypoint.sh` | App (root) | ✅ | 🟡 | 🟢 | 🟢 | — | F-032 |
| 275 | P5 | `src/DockerUpdateGuard/wwwroot/app.css` | App/wwwroot | ✅ | — | — | 🟢 | — | |
| 276 | P5 | `src/DockerUpdateGuard/wwwroot/favicon.svg` | App/wwwroot | ✅ | — | — | 🟢 | — | |
| 277 | P5 | `src/DockerUpdateGuard/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` | App/wwwroot | ✅ | — | — | 🟢 | — | |
| 278 | P5 | `src/DockerUpdateGuard/wwwroot/logo-dg-shield.svg` | App/wwwroot | ✅ | — | — | 🟢 | — | |
| 279 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/Data/SqliteTestDatabase.cs` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-050 |
| 280 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/DockerUpdateGuard.Data.Tests.csproj` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 281 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/ImageCatalogRepositoryNullDigestTests.cs` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 282 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/ImageCatalogRepositoryTests.cs` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 283 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/MappingTests.cs` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 284 | P7 | `src/Tests/DockerUpdateGuard.Data.Tests/SharedBaseImageQueryServiceTests.cs` | Tests/DockerUpdateGuard.Data.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 285 | P7 | `src/Tests/DockerUpdateGuard.Tests/ApplicationViewServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-050 |
| 286 | P7 | `src/Tests/DockerUpdateGuard.Tests/DashboardRefreshStateTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 287 | P7 | `src/Tests/DockerUpdateGuard.Tests/DashboardTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 288 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/NullDisposable.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 289 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/NullScope.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 290 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/ObservedRequest.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 291 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/SqliteTestDatabase.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-050 |
| 292 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/TestDevelopmentConfiguration.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 293 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/TestLogEntry.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 294 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/TestLogger{TCategoryName}.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 295 | P7 | `src/Tests/DockerUpdateGuard.Tests/Data/TestOptionsMonitor{TOptions}.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 296 | P7 | `src/Tests/DockerUpdateGuard.Tests/DerivedBaseRuntimeDetectorTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 297 | P7 | `src/Tests/DockerUpdateGuard.Tests/DockerHubAccountImageDiscoveryServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 298 | P7 | `src/Tests/DockerUpdateGuard.Tests/DockerHubClientTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 299 | P7 | `src/Tests/DockerUpdateGuard.Tests/DockerInstanceClientTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 300 | P7 | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuard.Tests.csproj` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 301 | P7 | `src/Tests/DockerUpdateGuard.Tests/DockerUpdateGuardOptionsValidatorTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 302 | P7 | `src/Tests/DockerUpdateGuard.Tests/DotNetReleaseMetadataServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 303 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/SequenceHttpMessageHandler.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 304 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/StaticHttpJsonMessageHandler.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 305 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/StaticHttpMessageHandler.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 306 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/StubHttpMessageHandler.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 307 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/TestImageCatalogRepository.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 308 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/TestScanCleanupBackgroundService.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 309 | P7 | `src/Tests/DockerUpdateGuard.Tests/Helper/TimeoutHttpMessageHandler.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 310 | P7 | `src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-046 |
| 311 | P7 | `src/Tests/DockerUpdateGuard.Tests/ImageRegistrationServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-029 |
| 312 | P7 | `src/Tests/DockerUpdateGuard.Tests/ImageScanOrchestratorTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-048 |
| 313 | P7 | `src/Tests/DockerUpdateGuard.Tests/InstanceDiscoveryServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 314 | P7 | `src/Tests/DockerUpdateGuard.Tests/MainLayoutTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 315 | P7 | `src/Tests/DockerUpdateGuard.Tests/MyImageDetailTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 316 | P7 | `src/Tests/DockerUpdateGuard.Tests/MyImagesTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 317 | P7 | `src/Tests/DockerUpdateGuard.Tests/NavMenuTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 318 | P7 | `src/Tests/DockerUpdateGuard.Tests/NginxReleaseMetadataServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 319 | P7 | `src/Tests/DockerUpdateGuard.Tests/ObservedImageScanIntervalCalculatorTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 320 | P7 | `src/Tests/DockerUpdateGuard.Tests/OciRegistryClientTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 321 | P7 | `src/Tests/DockerUpdateGuard.Tests/RegistryBaseImageResolverTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 322 | P7 | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainerScanOrchestratorTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-048 |
| 323 | P7 | `src/Tests/DockerUpdateGuard.Tests/RuntimeContainersTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-049 |
| 324 | P7 | `src/Tests/DockerUpdateGuard.Tests/ScanCleanupBackgroundServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-050 |
| 325 | P7 | `src/Tests/DockerUpdateGuard.Tests/ServiceCollectionExtensionsTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 326 | P7 | `src/Tests/DockerUpdateGuard.Tests/TelemetryServiceCollectionExtensionsTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
| 327 | P7 | `src/Tests/DockerUpdateGuard.Tests/TrivyVulnerabilityProviderTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟡 | F-016 |
| 328 | P7 | `src/Tests/DockerUpdateGuard.Tests/UpdateDetectionServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-047 |
| 329 | P7 | `src/Tests/DockerUpdateGuard.Tests/VersionTagResolutionHelperTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟠 | F-047 |
| 330 | P7 | `src/Tests/DockerUpdateGuard.Tests/VulnerabilityEnrichmentServiceTests.cs` | Tests/DockerUpdateGuard.Tests | ✅ | — | 🟢 | 🟢 | 🟢 |  |
