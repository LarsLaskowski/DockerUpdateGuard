using DockerUpdateGuard.Data.Entities;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Data;

/// <summary>
/// EF Core database context for DockerUpdateGuard
/// </summary>
public class DockerUpdateGuardDbContext : DbContext
{
    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">DbContext options</param>
    public DockerUpdateGuardDbContext(DbContextOptions<DockerUpdateGuardDbContext> options)
        : base(options)
    {
    }

    #endregion // Constructors

    #region Properties

    /// <summary>
    /// Observed images
    /// </summary>
    public DbSet<ObservedImage> ObservedImages => Set<ObservedImage>();

    /// <summary>
    /// Docker instances
    /// </summary>
    public DbSet<DockerInstance> DockerInstances => Set<DockerInstance>();

    /// <summary>
    /// Portainer endpoints
    /// </summary>
    public DbSet<PortainerEndpoint> PortainerEndpoints => Set<PortainerEndpoint>();

    /// <summary>
    /// Container snapshots
    /// </summary>
    public DbSet<ContainerSnapshot> ContainerSnapshots => Set<ContainerSnapshot>();

    /// <summary>
    /// Registry repositories
    /// </summary>
    public DbSet<RegistryRepository> RegistryRepositories => Set<RegistryRepository>();

    /// <summary>
    /// Image versions
    /// </summary>
    public DbSet<ImageVersion> ImageVersions => Set<ImageVersion>();

    /// <summary>
    /// Image relationships
    /// </summary>
    public DbSet<ImageRelationship> ImageRelationships => Set<ImageRelationship>();

    /// <summary>
    /// Scan runs
    /// </summary>
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();

    /// <summary>
    /// Update findings
    /// </summary>
    public DbSet<UpdateFinding> UpdateFindings => Set<UpdateFinding>();

    /// <summary>
    /// Tag candidates
    /// </summary>
    public DbSet<TagCandidate> TagCandidates => Set<TagCandidate>();

    /// <summary>
    /// Vulnerability findings
    /// </summary>
    public DbSet<VulnerabilityFinding> VulnerabilityFindings => Set<VulnerabilityFinding>();

    /// <summary>
    /// Container action runs
    /// </summary>
    public DbSet<ContainerActionRun> ContainerActionRuns => Set<ContainerActionRun>();

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DockerUpdateGuardDbContext).Assembly);
    }

    #endregion // Methods
}