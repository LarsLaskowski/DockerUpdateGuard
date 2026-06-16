// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Minor Code Smell", "S1125:Boolean literals should not be redundant", Justification = "SonarCloud", Scope = "module")]
[assembly: SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "SonarCloud", Scope = "module")]

[assembly: SuppressMessage("Major Bug", "S1244:Floating point numbers should not be tested for equality", Justification = "SonarCloud", Scope = "module")]

[assembly: SuppressMessage("Major Code Smell", "S6968:Actions that return a value should be annotated with ProducesResponseTypeAttribute containing the return type", Justification = "SonarCloud", Scope = "module")]

[assembly: SuppressMessage("Vulnerability", "S4830:Server certificates should be verified during SSL/TLS connections", Justification = "Opt-in administrator setting (DockerInstanceOptions.SkipCertificateValidation); the bypass is gated behind explicit configuration and a runtime warning is logged whenever it is active.", Scope = "member", Target = "~M:DockerUpdateGuard.Docker.DockerInstanceClient.CreateHttpClient(DockerUpdateGuard.Configuration.DockerInstanceOptions,System.Uri,Microsoft.Extensions.Logging.ILogger)~System.Net.Http.HttpClient")]