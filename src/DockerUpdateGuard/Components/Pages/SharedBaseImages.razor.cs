using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Shared base images page
/// </summary>
public partial class SharedBaseImages
{
    #region Fields

    private IReadOnlyList<SharedBaseImageListItemData>? _items;

    #endregion // Fields

    #region Properties

    /// <summary>
    /// Application view service
    /// </summary>
    [Inject]
    public IApplicationViewService ViewService { get; set; } = null!;

    #endregion // Properties

    #region Methods

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        var items = await ViewService.GetSharedBaseImagesAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _items = items;
                          }).ConfigureAwait(false);
    }

    #endregion // Methods
}