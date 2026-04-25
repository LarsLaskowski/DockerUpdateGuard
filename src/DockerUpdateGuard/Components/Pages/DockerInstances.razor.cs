using DockerUpdateGuard.UI;

using Microsoft.AspNetCore.Components;

namespace DockerUpdateGuard.Components.Pages;

/// <summary>
/// Docker instances page
/// </summary>
public partial class DockerInstances
{
    #region Fields

    private IReadOnlyList<DockerInstanceListItemData>? _instances;

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
        _instances = await ViewService.GetDockerInstancesAsync()
                                      .ConfigureAwait(true);
    }

    #endregion // Methods
}