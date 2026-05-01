using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
    /// Base images page
/// </summary>
public partial class SharedBaseImages
{
    #region Fields

    /// <summary>
    /// Base-image items
    /// </summary>
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
        var items = await ViewService.GetBaseImagesAsync()
                                     .ConfigureAwait(false);

        await InvokeAsync(() =>
                          {
                              _items = items;
                          }).ConfigureAwait(false);
    }

    #endregion // Methods
}