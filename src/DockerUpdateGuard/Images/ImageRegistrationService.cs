using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Observed image registration service
/// </summary>
public class ImageRegistrationService : IImageRegistrationService
{
    #region Fields

    /// <summary>
    /// Database context
    /// </summary>
    private readonly DockerUpdateGuardDbContext _dbContext;

    /// <summary>
    /// Image-catalog repository
    /// </summary>
    private readonly IImageCatalogRepository _imageCatalogRepository;

    /// <summary>
    /// Image-reference parser
    /// </summary>
    private readonly IImageReferenceParser _imageReferenceParser;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    public ImageRegistrationService(DockerUpdateGuardDbContext dbContext,
                                    IImageCatalogRepository imageCatalogRepository,
                                    IImageReferenceParser imageReferenceParser)
    {
        _dbContext = dbContext;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
    }

    #endregion // Constructors

    #region Methods

    /// <inheritdoc/>
    public async Task<ObservedImage> RegisterAsync(ObservedImageRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsedReference = _imageReferenceParser.Parse(request.ImageReference);
        var imageVersion = await _imageCatalogRepository.GetOrCreateImageVersionAsync(parsedReference.Registry,
                                                                                      parsedReference.Repository,
                                                                                      parsedReference.Tag,
                                                                                      parsedReference.Digest,
                                                                                      cancellationToken: cancellationToken)
                                                        .ConfigureAwait(false);
        imageVersion.Source = ImageVersionSource.ObservedImage;

        var existingImage = await _dbContext.ObservedImages.SingleOrDefaultAsync(entity => entity.Name == request.Name, cancellationToken)
                                                           .ConfigureAwait(false);

        if (existingImage is null)
        {
            existingImage = new ObservedImage
                            {
                                Name = request.Name.Trim(),
                                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                                CurrentImageVersionId = imageVersion.Id,
                                Source = RegistrationSource.Manual,
                            };

            _dbContext.ObservedImages.Add(existingImage);
        }
        else
        {
            existingImage.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            existingImage.CurrentImageVersionId = imageVersion.Id;
            existingImage.UpdatedAtUtc = DateTimeOffset.UtcNow;
            existingImage.IsEnabled = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        return existingImage;
    }

    #endregion // Methods
}