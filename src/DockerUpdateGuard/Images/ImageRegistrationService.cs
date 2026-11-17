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

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ReportableException(EmptyNameMessage);
        }

        var name = request.Name.Trim();

        if (name.Length > MaxNameLength)
        {
            throw new ReportableException(NameTooLongMessage);
        }

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

        var candidates = await _dbContext.ObservedImages.Where(entity => entity.CurrentImageVersionId == imageVersion.Id)
                                                        .ToListAsync(cancellationToken)
                                                        .ConfigureAwait(false);

        candidates = candidates.OrderBy(entity => entity.CreatedAtUtc)
                               .ThenBy(entity => entity.Id)
                               .ToList();

        var ownImage = candidates.FirstOrDefault(entity => entity.Source == RegistrationSource.Discovery && entity.IsEnabled);

        if (ownImage is not null)
        {
            throw new OwnImageAlreadyTrackedException($"This image is already tracked as own image '{ResponseBodyExcerpt.ReplaceControlCharacters(ownImage.Name)}'.", ownImage.Id);
        }

        imageVersion.Source = ImageVersionSource.ObservedImage;

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var now = _timeProvider.GetUtcNow();
        var observedImage = candidates.FirstOrDefault(entity => entity.Source == RegistrationSource.Manual);

        if (observedImage is null)
        {
            observedImage = new ObservedImage
                            {
                                Name = name,
                                Description = description,
                                CurrentImageVersionId = imageVersion.Id,
                                Source = RegistrationSource.Manual,
                                CreatedAtUtc = now,
                                UpdatedAtUtc = now,
                            };

            _dbContext.ObservedImages.Add(observedImage);
        }
        else
        {
            observedImage.Name = name;
            observedImage.Description = description;
            observedImage.IsEnabled = true;
            observedImage.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

        return observedImage;
    }

    #endregion // IImageRegistrationService
}