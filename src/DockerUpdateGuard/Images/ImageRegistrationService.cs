using DockerUpdateGuard.Data;
using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Data.Repositories;
using DockerUpdateGuard.Images.Data;
using DockerUpdateGuard.Images.Interfaces;
using DockerUpdateGuard.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Observed image registration service
/// </summary>
public class ImageRegistrationService : IImageRegistrationService
{
    #region Constants

    /// <summary>
    /// Message of the error raised when the image reference does not name a repository
    /// </summary>
    internal const string ReferenceWithoutRepositoryMessage = "The image reference does not contain a repository name";

    /// <summary>
    /// Maximum length of an image name
    /// </summary>
    internal const int MaxNameLength = 200;

    /// <summary>
    /// Message of the error raised when the image name is empty
    /// </summary>
    internal const string EmptyNameMessage = "The name must not be empty";

    /// <summary>
    /// Message of the error raised when the image name is too long
    /// </summary>
    internal const string NameTooLongMessage = "The name must not be longer than 200 characters";

    #endregion // Constants

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

    /// <summary>
    /// Time provider
    /// </summary>
    private readonly TimeProvider _timeProvider;

    #endregion // Fields

    #region Constructors

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dbContext">Database context</param>
    /// <param name="imageCatalogRepository">Image catalog repository</param>
    /// <param name="imageReferenceParser">Image reference parser</param>
    /// <param name="timeProvider">Time provider</param>
    public ImageRegistrationService(DockerUpdateGuardDbContext dbContext,
                                    IImageCatalogRepository imageCatalogRepository,
                                    IImageReferenceParser imageReferenceParser,
                                    TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _imageCatalogRepository = imageCatalogRepository;
        _imageReferenceParser = imageReferenceParser;
        _timeProvider = timeProvider;
    }

    #endregion // Constructors

    #region IImageRegistrationService

    /// <inheritdoc/>
    public async Task<ObservedImage> RegisterAsync(ObservedImageRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImageReference);

        ImageReference parsedReference;

        try
        {
            parsedReference = _imageReferenceParser.Parse(request.ImageReference);
        }
        catch (ArgumentException exception)
        {
            throw new ReportableException(ReferenceWithoutRepositoryMessage, exception);
        }

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

    #endregion // IImageRegistrationService
}