using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Infrastructure;

using Microsoft.Extensions.Logging;

namespace DockerUpdateGuard.Images;

/// <summary>
/// Source-generated logging contracts for host image operations and background services
/// </summary>
internal static partial class ImageHostLoggingExtensions
{
    #region Methods

    /// <summary>
    /// Log that a scheduled background service has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="backgroundServiceName">Background service name</param>
    /// <param name="intervalMinutes">Configured interval in minutes</param>
    [LoggerMessage(EventId = 2000,
                   Level = LogLevel.Information,
                   Message = "Starting scheduled background service {BackgroundServiceName} with a {IntervalMinutes} minute interval")]
    public static partial void BackgroundServiceStarted(this ILogger logger,
                                                        string backgroundServiceName,
                                                        double intervalMinutes);

    /// <summary>
    /// Log that a scheduled background service is waiting for its next run
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="backgroundServiceName">Background service name</param>
    /// <param name="intervalMinutes">Configured interval in minutes</param>
    [LoggerMessage(EventId = 2001,
                   Level = LogLevel.Debug,
                   Message = "Scheduled background service {BackgroundServiceName} is waiting {IntervalMinutes} minutes before the next run")]
    public static partial void BackgroundServiceWaiting(this ILogger logger,
                                                        string backgroundServiceName,
                                                        double intervalMinutes);

    /// <summary>
    /// Log that a scheduled background service execution has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="backgroundServiceName">Background service name</param>
    [LoggerMessage(EventId = 2002,
                   Level = LogLevel.Information,
                   Message = "Executing scheduled background service {BackgroundServiceName}")]
    public static partial void BackgroundServiceExecutionStarted(this ILogger logger, string backgroundServiceName);

    /// <summary>
    /// Log that a scheduled background service execution has completed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="backgroundServiceName">Background service name</param>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds</param>
    [LoggerMessage(EventId = 2003,
                   Level = LogLevel.Information,
                   Message = "Completed scheduled background service {BackgroundServiceName} in {ElapsedMilliseconds} ms")]
    public static partial void BackgroundServiceExecutionCompleted(this ILogger logger,
                                                                   string backgroundServiceName,
                                                                   long elapsedMilliseconds);

    /// <summary>
    /// Log that a scheduled background service execution has failed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="backgroundServiceName">Background service name</param>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds</param>
    [LoggerMessage(EventId = 2004,
                   Level = LogLevel.Error,
                   Message = "Scheduled background service {BackgroundServiceName} failed after {ElapsedMilliseconds} ms")]
    public static partial void BackgroundServiceExecutionFailed(this ILogger logger,
                                                                Exception exception,
                                                                string backgroundServiceName,
                                                                long elapsedMilliseconds);

    /// <summary>
    /// Log configured Docker instance synchronization results
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="configuredInstanceCount">Configured instance count</param>
    /// <param name="enabledInstanceCount">Enabled instance count</param>
    /// <param name="disabledInstanceCount">Disabled instance count</param>
    /// <param name="portainerEndpointCount">Enabled Portainer endpoint count</param>
    [LoggerMessage(EventId = 2010,
                   Level = LogLevel.Information,
                   Message = "Synchronized {ConfiguredInstanceCount} configured Docker instances with {EnabledInstanceCount} enabled instances, {DisabledInstanceCount} disabled instances, and {PortainerEndpointCount} enabled Portainer endpoints")]
    public static partial void DockerInstanceSynchronizationCompleted(this ILogger logger,
                                                                      int configuredInstanceCount,
                                                                      int enabledInstanceCount,
                                                                      int disabledInstanceCount,
                                                                      int portainerEndpointCount);

    /// <summary>
    /// Log scan cleanup results
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="retainScanRunsDays">Retention period in days</param>
    /// <param name="removedTagCandidates">Removed tag candidate count</param>
    /// <param name="removedUpdateFindings">Removed update finding count</param>
    /// <param name="removedVulnerabilityFindings">Removed vulnerability finding count</param>
    /// <param name="removedSnapshots">Removed container snapshot count</param>
    /// <param name="removedScanRuns">Removed scan run count</param>
    [LoggerMessage(EventId = 2020,
                   Level = LogLevel.Information,
                   Message = "Completed scan cleanup for a {RetainScanRunsDays} day retention window by removing {RemovedTagCandidates} tag candidates, {RemovedUpdateFindings} update findings, {RemovedVulnerabilityFindings} vulnerability findings, {RemovedSnapshots} container snapshots, and {RemovedScanRuns} scan runs")]
    public static partial void ScanCleanupCompleted(this ILogger logger,
                                                    int retainScanRunsDays,
                                                    int removedTagCandidates,
                                                    int removedUpdateFindings,
                                                    int removedVulnerabilityFindings,
                                                    int removedSnapshots,
                                                    int removedScanRuns);

    /// <summary>
    /// Log an observed image scan failure
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="observedImage">Observed image name</param>
    [LoggerMessage(EventId = 2030,
                   Level = LogLevel.Error,
                   Message = "Observed image scan failed for {ObservedImage}")]
    public static partial void ObservedImageScanFailed(this ILogger logger,
                                                       Exception exception,
                                                       string observedImage);

    /// <summary>
    /// Log an observed image scan summary
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    /// <param name="status">Final scan status</param>
    /// <param name="baseImageCount">Resolved base image count</param>
    /// <param name="statusMessageCount">Status message count</param>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds</param>
    [LoggerMessage(EventId = 2031,
                   Level = LogLevel.Information,
                   Message = "Completed observed image scan for {ObservedImage} with status {Status}, {BaseImageCount} resolved base images, {StatusMessageCount} status messages, and {ElapsedMilliseconds} ms elapsed")]
    public static partial void ObservedImageScanCompleted(this ILogger logger,
                                                          string observedImage,
                                                          ScanRunStatus status,
                                                          int baseImageCount,
                                                          int statusMessageCount,
                                                          long elapsedMilliseconds);

    /// <summary>
    /// Log a runtime container scan failure
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="dockerInstance">Docker instance name</param>
    [LoggerMessage(EventId = 2040,
                   Level = LogLevel.Error,
                   Message = "Runtime container scan failed for {DockerInstance}")]
    public static partial void RuntimeContainerScanFailed(this ILogger logger,
                                                          Exception exception,
                                                          string dockerInstance);

    /// <summary>
    /// Log a runtime container scan summary
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="status">Final scan status</param>
    /// <param name="containerCount">Processed container count</param>
    /// <param name="statusMessageCount">Status message count</param>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds</param>
    [LoggerMessage(EventId = 2041,
                   Level = LogLevel.Information,
                   Message = "Completed runtime container scan for {DockerInstance} with status {Status}, {ContainerCount} processed containers, {StatusMessageCount} status messages, and {ElapsedMilliseconds} ms elapsed")]
    public static partial void RuntimeContainerScanCompleted(this ILogger logger,
                                                             string dockerInstance,
                                                             ScanRunStatus status,
                                                             int containerCount,
                                                             int statusMessageCount,
                                                             long elapsedMilliseconds);

    /// <summary>
    /// Log that vulnerability refresh is disabled
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 2050,
                   Level = LogLevel.Information,
                   Message = "Skipping vulnerability refresh because vulnerability scanning is disabled")]
    public static partial void VulnerabilityRefreshSkipped(this ILogger logger);

    /// <summary>
    /// Log a vulnerability refresh failure
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    [LoggerMessage(EventId = 2051,
                   Level = LogLevel.Error,
                   Message = "Vulnerability refresh failed")]
    public static partial void VulnerabilityRefreshFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Log a vulnerability refresh summary
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="status">Final scan status</param>
    /// <param name="imageCount">Processed image count</param>
    /// <param name="statusMessageCount">Status message count</param>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds</param>
    [LoggerMessage(EventId = 2052,
                   Level = LogLevel.Information,
                   Message = "Completed vulnerability refresh with status {Status}, {ImageCount} processed images, {StatusMessageCount} status messages, and {ElapsedMilliseconds} ms elapsed")]
    public static partial void VulnerabilityRefreshCompleted(this ILogger logger,
                                                             ScanRunStatus status,
                                                             int imageCount,
                                                             int statusMessageCount,
                                                             long elapsedMilliseconds);

    /// <summary>
    /// Log that observed image batch scanning has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="observedImageCount">Enabled observed image count</param>
    [LoggerMessage(EventId = 2060,
                   Level = LogLevel.Information,
                   Message = "Starting observed image scan batch from {TriggerSource} for {ObservedImageCount} enabled images")]
    public static partial void ObservedImageScanBatchStarted(this ILogger logger,
                                                             ScanTriggerSource triggerSource,
                                                             int observedImageCount);

    /// <summary>
    /// Log that observed image batch scanning was skipped
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2061,
                   Level = LogLevel.Information,
                   Message = "Skipping observed image scan batch from {TriggerSource} because no enabled observed images are registered")]
    public static partial void ObservedImageScanBatchSkipped(this ILogger logger, ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that observed image batch scanning has completed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="observedImageCount">Processed observed image count</param>
    [LoggerMessage(EventId = 2062,
                   Level = LogLevel.Information,
                   Message = "Completed observed image scan batch from {TriggerSource} after processing {ObservedImageCount} observed images")]
    public static partial void ObservedImageScanBatchCompleted(this ILogger logger,
                                                               ScanTriggerSource triggerSource,
                                                               int observedImageCount);

    /// <summary>
    /// Log that an observed image scan has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2063,
                   Level = LogLevel.Information,
                   Message = "Starting observed image scan for {ObservedImage} from {TriggerSource}")]
    public static partial void ObservedImageScanStarted(this ILogger logger,
                                                        string observedImage,
                                                        ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that observed image metadata refresh was incomplete
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2064,
                   Level = LogLevel.Warning,
                   Message = "Observed image metadata refresh for {ObservedImage} returned {OperationStatus}: {Message}")]
    public static partial void ObservedImageMetadataRefreshIncomplete(this ILogger logger,
                                                                      string observedImage,
                                                                      ExternalOperationStatus operationStatus,
                                                                      string? message);

    /// <summary>
    /// Log that base image resolution was incomplete
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2065,
                   Level = LogLevel.Warning,
                   Message = "Base image resolution for {ObservedImage} returned {OperationStatus}: {Message}")]
    public static partial void ObservedImageBaseImageResolutionIncomplete(this ILogger logger,
                                                                          string observedImage,
                                                                          ExternalOperationStatus operationStatus,
                                                                          string? message);

    /// <summary>
    /// Log that no base images were resolved for an observed image
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    [LoggerMessage(EventId = 2066,
                   Level = LogLevel.Information,
                   Message = "Observed image scan for {ObservedImage} resolved no base images")]
    public static partial void ObservedImageBaseImagesMissing(this ILogger logger, string observedImage);

    /// <summary>
    /// Log that a base image evaluation was incomplete
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="observedImage">Observed image name</param>
    /// <param name="baseImageReference">Base image reference</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2067,
                   Level = LogLevel.Warning,
                   Message = "Base image evaluation for {ObservedImage} dependency {BaseImageReference} returned {OperationStatus}: {Message}")]
    public static partial void ObservedImageBaseImageEvaluationIncomplete(this ILogger logger,
                                                                          string observedImage,
                                                                          string baseImageReference,
                                                                          ExternalOperationStatus operationStatus,
                                                                          string? message);

    /// <summary>
    /// Log that runtime container batch scanning has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="dockerInstanceCount">Enabled Docker instance count</param>
    [LoggerMessage(EventId = 2070,
                   Level = LogLevel.Information,
                   Message = "Starting runtime container scan batch from {TriggerSource} for {DockerInstanceCount} enabled Docker instances")]
    public static partial void RuntimeContainerScanBatchStarted(this ILogger logger,
                                                                ScanTriggerSource triggerSource,
                                                                int dockerInstanceCount);

    /// <summary>
    /// Log that runtime container batch scanning was skipped
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2071,
                   Level = LogLevel.Information,
                   Message = "Skipping runtime container scan batch from {TriggerSource} because no enabled Docker instances are available")]
    public static partial void RuntimeContainerScanBatchSkipped(this ILogger logger, ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that runtime container batch scanning has completed
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    /// <param name="processedInstanceCount">Processed Docker instance count</param>
    /// <param name="skippedInstanceCount">Skipped Docker instance count</param>
    [LoggerMessage(EventId = 2072,
                   Level = LogLevel.Information,
                   Message = "Completed runtime container scan batch from {TriggerSource} after processing {ProcessedInstanceCount} Docker instances and skipping {SkippedInstanceCount} instances")]
    public static partial void RuntimeContainerScanBatchCompleted(this ILogger logger,
                                                                  ScanTriggerSource triggerSource,
                                                                  int processedInstanceCount,
                                                                  int skippedInstanceCount);

    /// <summary>
    /// Log that a runtime container scan has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2073,
                   Level = LogLevel.Information,
                   Message = "Starting runtime container scan for {DockerInstance} from {TriggerSource}")]
    public static partial void RuntimeContainerScanStarted(this ILogger logger,
                                                           string dockerInstance,
                                                           ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that a runtime container scan was skipped because configuration is missing
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    [LoggerMessage(EventId = 2074,
                   Level = LogLevel.Warning,
                   Message = "Skipping runtime container scan for {DockerInstance} because no matching configuration entry is available")]
    public static partial void RuntimeContainerScanSkippedConfigurationMissing(this ILogger logger, string dockerInstance);

    /// <summary>
    /// Log that a runtime container scan was skipped because the configuration entry is disabled
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    [LoggerMessage(EventId = 2079,
                   Level = LogLevel.Information,
                   Message = "Skipping runtime container scan for {DockerInstance} because the matching configuration entry is disabled")]
    public static partial void RuntimeContainerScanSkippedConfigurationDisabled(this ILogger logger, string dockerInstance);

    /// <summary>
    /// Log that runtime container discovery was incomplete
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2075,
                   Level = LogLevel.Warning,
                   Message = "Runtime container discovery for {DockerInstance} returned {OperationStatus}: {Message}")]
    public static partial void RuntimeContainerDiscoveryIncomplete(this ILogger logger,
                                                                   string dockerInstance,
                                                                   ExternalOperationStatus operationStatus,
                                                                   string? message);

    /// <summary>
    /// Log that a runtime scan found no containers
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    [LoggerMessage(EventId = 2076,
                   Level = LogLevel.Information,
                   Message = "Runtime container scan for {DockerInstance} found no containers")]
    public static partial void RuntimeContainerScanFoundNoContainers(this ILogger logger, string dockerInstance);

    /// <summary>
    /// Log that runtime image registry evaluation was incomplete
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2077,
                   Level = LogLevel.Warning,
                   Message = "Runtime image evaluation for {DockerInstance} image {ImageReference} returned {OperationStatus}: {Message}")]
    public static partial void RuntimeContainerRegistryEvaluationIncomplete(this ILogger logger,
                                                                            string dockerInstance,
                                                                            string imageReference,
                                                                            ExternalOperationStatus operationStatus,
                                                                            string? message);

    /// <summary>
    /// Log that runtime image registry evaluation is unsupported
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2078,
                   Level = LogLevel.Warning,
                   Message = "Runtime image evaluation for {DockerInstance} image {ImageReference} is not supported by the current adapter because it returned {OperationStatus}: {Message}")]
    public static partial void RuntimeContainerRegistryEvaluationUnsupported(this ILogger logger,
                                                                             string dockerInstance,
                                                                             string imageReference,
                                                                             ExternalOperationStatus operationStatus,
                                                                             string? message);

    /// <summary>
    /// Log that a single runtime container could not be processed but the scan continues
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="exception">Exception</param>
    /// <param name="dockerInstance">Docker instance name</param>
    /// <param name="containerName">Container name</param>
    /// <param name="imageReference">Image reference</param>
    [LoggerMessage(EventId = 2098,
                   Level = LogLevel.Warning,
                   Message = "Runtime container scan for {DockerInstance} skipped container {ContainerName} ({ImageReference}) because processing failed")]
    public static partial void RuntimeContainerProcessingFailed(this ILogger logger,
                                                                Exception exception,
                                                                string dockerInstance,
                                                                string containerName,
                                                                string imageReference);

    /// <summary>
    /// Log that vulnerability refresh has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2080,
                   Level = LogLevel.Information,
                   Message = "Starting vulnerability refresh from {TriggerSource}")]
    public static partial void VulnerabilityRefreshStarted(this ILogger logger, ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that vulnerability refresh found no eligible images
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="triggerSource">Trigger source</param>
    [LoggerMessage(EventId = 2081,
                   Level = LogLevel.Information,
                   Message = "Vulnerability refresh from {TriggerSource} found no images that require enrichment")]
    public static partial void VulnerabilityRefreshNoImages(this ILogger logger, ScanTriggerSource triggerSource);

    /// <summary>
    /// Log that vulnerability enrichment was incomplete for an image
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="imageReference">Image reference</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Status message</param>
    [LoggerMessage(EventId = 2082,
                   Level = LogLevel.Warning,
                   Message = "Vulnerability enrichment for {ImageReference} returned {OperationStatus}: {Message}")]
    public static partial void VulnerabilityRefreshImageIncomplete(this ILogger logger,
                                                                   string imageReference,
                                                                   ExternalOperationStatus operationStatus,
                                                                   string? message);

    /// <summary>
    /// Log that Docker instance synchronization has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="configuredInstanceCount">Configured instance count</param>
    [LoggerMessage(EventId = 2090,
                   Level = LogLevel.Information,
                   Message = "Starting Docker instance synchronization for {ConfiguredInstanceCount} configured instances")]
    public static partial void DockerInstanceSynchronizationStarted(this ILogger logger, int configuredInstanceCount);

    /// <summary>
    /// Log that Docker instance configuration uses unsupported connection settings
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="dockerInstanceName">Docker instance name</param>
    /// <param name="baseUrl">Configured endpoint</param>
    [LoggerMessage(EventId = 2091,
                   Level = LogLevel.Warning,
                   Message = "Docker instance {DockerInstanceName} uses unsupported connection settings for endpoint {BaseUrl}")]
    public static partial void DockerInstanceConfigurationUnsupported(this ILogger logger,
                                                                      string dockerInstanceName,
                                                                      string? baseUrl);

    /// <summary>
    /// Log that Docker Hub account image synchronization was skipped because no PAT is configured
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 2092,
                   Level = LogLevel.Information,
                   Message = "Skipping Docker Hub account image synchronization because no Docker Hub PAT is configured")]
    public static partial void DockerHubAccountSynchronizationSkippedPatMissing(this ILogger logger);

    /// <summary>
    /// Log that Docker Hub account image synchronization was skipped because no user name is configured
    /// </summary>
    /// <param name="logger">Logger</param>
    [LoggerMessage(EventId = 2097,
                   Level = LogLevel.Warning,
                   Message = "Skipping Docker Hub account image synchronization because DockerHub:UserName is not configured")]
    public static partial void DockerHubAccountSynchronizationSkippedUserNameMissing(this ILogger logger);

    /// <summary>
    /// Log that Docker Hub account image synchronization has started
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="accountName">Docker Hub account name</param>
    [LoggerMessage(EventId = 2093,
                   Level = LogLevel.Information,
                   Message = "Starting Docker Hub account image synchronization for account {AccountName}")]
    public static partial void DockerHubAccountSynchronizationStarted(this ILogger logger, string accountName);

    /// <summary>
    /// Log that Docker Hub account image synchronization could not continue because of a Docker Hub operation result
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Operation message</param>
    [LoggerMessage(EventId = 2094,
                   Level = LogLevel.Warning,
                   Message = "Docker Hub account image synchronization could not continue because a Docker Hub operation returned {OperationStatus}: {Message}")]
    public static partial void DockerHubAccountSynchronizationAccountUnavailable(this ILogger logger,
                                                                                 ExternalOperationStatus operationStatus,
                                                                                 string? message);

    /// <summary>
    /// Log that a Docker Hub repository was skipped because no trackable tag could be selected
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="repository">Repository path</param>
    /// <param name="operationStatus">Operation status</param>
    /// <param name="message">Operation message</param>
    [LoggerMessage(EventId = 2095,
                   Level = LogLevel.Warning,
                   Message = "Skipping Docker Hub repository {Repository} because no trackable tag could be selected. Docker Hub returned {OperationStatus}: {Message}")]
    public static partial void DockerHubAccountSynchronizationRepositorySkipped(this ILogger logger,
                                                                                string repository,
                                                                                ExternalOperationStatus operationStatus,
                                                                                string? message);

    /// <summary>
    /// Log Docker Hub account image synchronization results
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="accountName">Docker Hub account name</param>
    /// <param name="repositoryCount">Repository count</param>
    /// <param name="synchronizedImageCount">Synchronized image count</param>
    /// <param name="disabledImageCount">Disabled image count</param>
    /// <param name="skippedRepositoryCount">Skipped repository count</param>
    [LoggerMessage(EventId = 2096,
                   Level = LogLevel.Information,
                   Message = "Synchronized Docker Hub account {AccountName} with {RepositoryCount} repositories, {SynchronizedImageCount} synchronized images, {DisabledImageCount} disabled images, and {SkippedRepositoryCount} skipped repositories")]
    public static partial void DockerHubAccountSynchronizationCompleted(this ILogger logger,
                                                                        string accountName,
                                                                        int repositoryCount,
                                                                        int synchronizedImageCount,
                                                                        int disabledImageCount,
                                                                        int skippedRepositoryCount);

    #endregion // Methods
}