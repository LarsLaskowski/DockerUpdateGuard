using DockerUpdateGuard.Data.Entities;
using DockerUpdateGuard.Images;
using DockerUpdateGuard.Images.Data;

namespace DockerUpdateGuard.Tests;

/// <summary>
/// Tests for <see cref="ImageReferenceParser"/>
/// </summary>
[TestClass]
public class ImageReferenceParserTests
{
    #region Static methods

    /// <summary>
    /// Assert all parts of a parsed image reference
    /// </summary>
    /// <param name="imageReference">Parsed image reference</param>
    /// <param name="value">Parsed input</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    private static void AssertReference(ImageReference imageReference, string value, string expectedRegistry, string expectedRepository, string expectedTag, string? expectedDigest)
    {
        Assert.AreEqual(expectedRegistry, imageReference.Registry, $"Registry of '{value}'");
        Assert.AreEqual(expectedRepository, imageReference.Repository, $"Repository of '{value}'");
        Assert.AreEqual(expectedTag, imageReference.Tag, $"Tag of '{value}'");
        Assert.AreEqual(expectedDigest, imageReference.Digest, $"Digest of '{value}'");
    }

    /// <summary>
    /// Create an image version with its registry repository
    /// </summary>
    /// <param name="registry">Registry</param>
    /// <param name="repository">Repository</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest</param>
    /// <returns>Image version</returns>
    private static ImageVersion CreateImageVersion(string registry, string repository, string tag, string? digest)
    {
        return new ImageVersion
               {
                   Tag = tag,
                   Digest = digest,
                   RegistryRepository = new RegistryRepository
                                        {
                                            Registry = registry,
                                            Repository = repository,
                                        },
               };
    }

    #endregion // Static methods

    #region Methods

    /// <summary>
    /// Verify wrapped foreign registry references are normalized back to their source registry
    /// </summary>
    [TestMethod]
    public void ImageReferenceParserParseWrappedMicrosoftRegistryReferenceNormalizesRegistry()
    {
        var parser = new ImageReferenceParser();

        var imageReference = parser.Parse("docker.io/mcr.microsoft.com/mssql/server:2019-CU32-GDR7-ubuntu-20.04");

        Assert.AreEqual("mcr.microsoft.com",
                        imageReference.Registry,
                        "Wrapped Microsoft registry references must be normalized back to mcr.microsoft.com");
        Assert.AreEqual("mssql/server",
                        imageReference.Repository,
                        "Wrapped Microsoft registry references must keep the repository after removing the docker.io wrapper");
        Assert.AreEqual("2019-CU32-GDR7-ubuntu-20.04",
                        imageReference.Tag,
                        "Wrapped Microsoft registry references must keep their original tag");
    }

    /// <summary>
    /// Verify references without a registry get the Docker Hub defaults
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("nginx", "library/nginx", "latest")]
    [DataRow("NGINX", "library/nginx", "latest")]
    [DataRow(" nginx:1.25 ", "library/nginx", "1.25")]
    [DataRow("nginx:", "library/nginx", "latest")]
    [DataRow("library/nginx", "library/nginx", "latest")]
    [DataRow("myuser/app:2", "myuser/app", "2")]
    public void ImageReferenceParserParseReferenceWithoutRegistryUsesDockerHubDefaults(string value, string expectedRepository, string expectedTag)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "docker.io", expectedRepository, expectedTag, null);
    }

    /// <summary>
    /// Verify a registry-like first segment becomes the lowercased registry and the tag keeps its case
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("localhost:5000/app", "localhost:5000", "app", "latest")]
    [DataRow("localhost:5000/app:1.0", "localhost:5000", "app", "1.0")]
    [DataRow("LOCALHOST/App", "localhost", "app", "latest")]
    [DataRow("ghcr.io/org/team/app:v1", "ghcr.io", "org/team/app", "v1")]
    [DataRow("Registry.Example.com/Team/App:RC1", "registry.example.com", "team/app", "RC1")]
    [DataRow("ghcr.io//org/app", "ghcr.io", "org/app", "latest")]
    public void ImageReferenceParserParseReferenceWithRegistryHostSplitsRegistryAndRepository(string value, string expectedRegistry, string expectedRepository, string expectedTag)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, expectedRegistry, expectedRepository, expectedTag, null);
    }

    /// <summary>
    /// Verify a docker.io reference without a namespace gets the library namespace
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("docker.io/nginx", "latest", null)]
    [DataRow("docker.io/library/nginx", "latest", null)]
    [DataRow("DOCKER.IO/Nginx", "latest", null)]
    [DataRow("docker.io/nginx:1.27", "1.27", null)]
    [DataRow("docker.io/nginx@sha256:ABC", "latest", "sha256:abc")]
    [DataRow("docker.io/library/nginx:1.27@sha256:ABC", "1.27", "sha256:abc")]
    public void ImageReferenceParserParseDockerIoSingleSegmentAddsLibraryNamespace(string value, string expectedTag, string? expectedDigest)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "docker.io", "library/nginx", expectedTag, expectedDigest);
    }

    /// <summary>
    /// Verify index.docker.io is mapped to docker.io
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("index.docker.io/nginx", "latest", null)]
    [DataRow("index.docker.io/library/nginx", "latest", null)]
    [DataRow("index.docker.io/nginx:1.27", "1.27", null)]
    [DataRow("index.docker.io/library/nginx@sha256:ABC", "latest", "sha256:abc")]
    [DataRow("INDEX.DOCKER.IO/Nginx:1.27@sha256:ABC", "1.27", "sha256:abc")]
    public void ImageReferenceParserParseIndexDockerIoNormalizesRegistry(string value, string expectedTag, string? expectedDigest)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "docker.io", "library/nginx", expectedTag, expectedDigest);
        Assert.StartsWith("docker.io/library/nginx:", imageReference.FullReference, $"Full reference of '{value}' must not keep the index.docker.io alias");
    }

    /// <summary>
    /// Verify registry-1.docker.io is mapped to docker.io
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC1")]
    [DataRow("registry-1.docker.io/nginx", "latest", null)]
    [DataRow("registry-1.docker.io/library/nginx", "latest", null)]
    [DataRow("registry-1.docker.io/nginx:1.27", "1.27", null)]
    [DataRow("registry-1.docker.io/library/nginx@sha256:ABC", "latest", "sha256:abc")]
    public void ImageReferenceParserParseRegistryOneDockerIoNormalizesRegistry(string value, string expectedTag, string? expectedDigest)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        Assert.AreEqual("docker.io", imageReference.Registry, $"Registry of '{value}' must be docker.io, not the registry-1 alias");
        Assert.AreEqual("library/nginx", imageReference.Repository, $"Repository of '{value}' must carry the library namespace");
        Assert.AreEqual(expectedTag, imageReference.Tag, $"Tag of '{value}'");
        Assert.AreEqual(expectedDigest, imageReference.Digest, $"Digest of '{value}'");
    }

    /// <summary>
    /// Verify every Docker Hub spelling of a namespaced repository yields docker.io and the repository
    /// </summary>
    /// <param name="value">Image reference</param>
    [TestMethod]
    [TestCategory("AC2")]
    [DataRow("docker.io/myuser/app")]
    [DataRow("index.docker.io/myuser/app")]
    [DataRow("registry-1.docker.io/myuser/app")]
    [DataRow("REGISTRY-1.DOCKER.IO/MyUser/App")]
    [DataRow("myuser/app")]
    public void ImageReferenceParserParseNamespacedDockerHubSpellingReturnsCanonicalReference(string value)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "docker.io", "myuser/app", "latest", null);
    }

    /// <summary>
    /// Verify a registry wrapped behind a Docker Hub alias is unwrapped
    /// </summary>
    /// <param name="value">Image reference</param>
    [TestMethod]
    [TestCategory("AC3")]
    [DataRow("index.docker.io/ghcr.io/owner/app:1")]
    [DataRow("registry-1.docker.io/ghcr.io/owner/app:1")]
    [DataRow("docker.io/ghcr.io/owner/app:1")]
    public void ImageReferenceParserParseWrappedRegistryBehindDockerHubAliasUnwraps(string value)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "ghcr.io", "owner/app", "1", null);
    }

    /// <summary>
    /// Verify other registries keep their registry and repository and Docker Hub spellings keep the tag case
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("localhost:5000/app", "localhost:5000", "app", "latest")]
    [DataRow("localhost/app", "localhost", "app", "latest")]
    [DataRow("ghcr.io/owner/app", "ghcr.io", "owner/app", "latest")]
    [DataRow("registry.example.com/app", "registry.example.com", "app", "latest")]
    [DataRow("INDEX.DOCKER.IO/Foo/Bar:RC1", "docker.io", "foo/bar", "RC1")]
    public void ImageReferenceParserParseOtherRegistryKeepsRegistryAndRepository(string value, string expectedRegistry, string expectedRepository, string expectedTag)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        Assert.AreEqual(expectedRegistry, imageReference.Registry, $"Registry of '{value}'");
        Assert.AreEqual(expectedRepository, imageReference.Repository, $"Repository of '{value}' must not get a library namespace outside Docker Hub");
        Assert.AreEqual(expectedTag, imageReference.Tag, $"Tag of '{value}' must keep its case");
    }

    /// <summary>
    /// Verify digests are split off, trimmed and lowercased
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC4")]
    [DataRow("nginx@sha256:ABC", "docker.io", "library/nginx", "latest", "sha256:abc")]
    [DataRow("nginx:1.25@sha256:abc", "docker.io", "library/nginx", "1.25", "sha256:abc")]
    [DataRow("registry.example.com:5000/team/app:2.0@ sha256:Def ", "registry.example.com:5000", "team/app", "2.0", "sha256:def")]
    [DataRow("nginx@", "docker.io", "library/nginx", "latest", null)]
    public void ImageReferenceParserParseReferenceWithDigestNormalizesDigest(string value, string expectedRegistry, string expectedRepository, string expectedTag, string? expectedDigest)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, expectedRegistry, expectedRepository, expectedTag, expectedDigest);
    }

    /// <summary>
    /// Verify docker.io wrapped references of known registries are unwrapped
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow("docker.io/ghcr.io/owner/app:1", "ghcr.io", "owner/app", "1")]
    [DataRow("docker.io/quay.io/org/app", "quay.io", "org/app", "latest")]
    [DataRow("DOCKER.IO/Harbor.Example.com/Project/App:3", "harbor.example.com", "project/app", "3")]
    public void ImageReferenceParserParseMirrorWrappedReferenceUnwrapsKnownRegistry(string value, string expectedRegistry, string expectedRepository, string expectedTag)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, expectedRegistry, expectedRepository, expectedTag, null);
        Assert.AreEqual($"{expectedRegistry}/{expectedRepository}:{expectedTag}", imageReference.FullReference, $"Full reference of '{value}' must not keep the docker.io wrapper");
    }

    /// <summary>
    /// Verify docker.io wrapped references of unknown hosts stay on docker.io
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRepository">Expected repository</param>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow("docker.io/harbor/app", "harbor/app")]
    [DataRow("docker.io/registry.example.com/app", "registry.example.com/app")]
    [DataRow("docker.io/ghcr.io", "library/ghcr.io")]
    public void ImageReferenceParserParseMirrorWrappedReferenceOfUnknownHostKeepsDockerHub(string value, string expectedRepository)
    {
        var imageReference = new ImageReferenceParser().Parse(value);

        AssertReference(imageReference, value, "docker.io", expectedRepository, "latest", null);
    }

    /// <summary>
    /// Verify a null reference is rejected
    /// </summary>
    [TestMethod]
    [TestCategory("AC6")]
    public void ImageReferenceParserParseNullReferenceThrowsArgumentNullException()
    {
        var parser = new ImageReferenceParser();

        var exception = Assert.ThrowsExactly<ArgumentNullException>(() => parser.Parse(null!), "A null reference must throw ArgumentNullException");

        Assert.AreEqual("value", exception.ParamName, "The exception must name the value parameter");
    }

    /// <summary>
    /// Verify an empty or whitespace reference is rejected
    /// </summary>
    /// <param name="value">Blank reference</param>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void ImageReferenceParserParseBlankReferenceThrowsArgumentException(string value)
    {
        var parser = new ImageReferenceParser();

        var exception = Assert.ThrowsExactly<ArgumentException>(() => parser.Parse(value), "A blank reference must throw ArgumentException");

        Assert.AreEqual("value", exception.ParamName, "The exception must name the value parameter");
    }

    /// <summary>
    /// Verify a reference without a repository name is rejected
    /// </summary>
    /// <param name="value">Reference without repository</param>
    [TestMethod]
    [TestCategory("AC6")]
    [DataRow("/")]
    [DataRow("//")]
    [DataRow(":1.0")]
    [DataRow("/:1.0")]
    [DataRow("@sha256:abc")]
    public void ImageReferenceParserParseReferenceWithoutRepositoryNameThrowsArgumentException(string value)
    {
        var parser = new ImageReferenceParser();

        var exception = Assert.ThrowsExactly<ArgumentException>(() => parser.Parse(value), $"'{value}' has no repository name and must throw ArgumentException");

        Assert.AreEqual("value", exception.ParamName, "The exception must name the value parameter");
        Assert.StartsWith("The image reference does not contain a repository name", exception.Message, "The exception must explain the missing repository name");
    }

    /// <summary>
    /// Verify Format renders registry, repository, tag and an optional digest
    /// </summary>
    /// <param name="digest">Version digest</param>
    /// <param name="expected">Expected reference</param>
    [TestMethod]
    [TestCategory("AC7")]
    [DataRow(null, "ghcr.io/org/app:1.0")]
    [DataRow("", "ghcr.io/org/app:1.0")]
    [DataRow("   ", "ghcr.io/org/app:1.0")]
    [DataRow("sha256:abc", "ghcr.io/org/app:1.0@sha256:abc")]
    public void ImageReferenceParserFormatImageVersionReturnsCanonicalReference(string? digest, string expected)
    {
        var imageVersion = CreateImageVersion("ghcr.io", "org/app", "1.0", digest);

        var reference = new ImageReferenceParser().Format(imageVersion);

        Assert.AreEqual(expected, reference, "Format must append the digest only when it is not blank");
    }

    /// <summary>
    /// Verify a formatted reference parses back to the same parts
    /// </summary>
    /// <param name="registry">Registry</param>
    /// <param name="repository">Repository</param>
    /// <param name="tag">Tag</param>
    /// <param name="digest">Digest</param>
    [TestMethod]
    [TestCategory("AC7")]
    [DataRow("localhost:5000", "app", "1.0", null)]
    [DataRow("docker.io", "library/nginx", "Latest", "sha256:abc")]
    public void ImageReferenceParserFormatCanonicalReferenceParsesBackToSameParts(string registry, string repository, string tag, string? digest)
    {
        var parser = new ImageReferenceParser();
        var reference = parser.Format(CreateImageVersion(registry, repository, tag, digest));

        var imageReference = parser.Parse(reference);

        AssertReference(imageReference, reference, registry, repository, tag, digest);
    }

    /// <summary>
    /// Verify Format of a parsed reference parses back to the same canonical parts for every Docker Hub spelling and other registry
    /// </summary>
    /// <param name="value">Image reference</param>
    /// <param name="expectedRegistry">Expected registry</param>
    /// <param name="expectedRepository">Expected repository</param>
    /// <param name="expectedTag">Expected tag</param>
    /// <param name="expectedDigest">Expected digest</param>
    [TestMethod]
    [TestCategory("AC5")]
    [DataRow("nginx", "docker.io", "library/nginx", "latest", null)]
    [DataRow("docker.io/nginx:1.27", "docker.io", "library/nginx", "1.27", null)]
    [DataRow("index.docker.io/library/nginx@sha256:ABC", "docker.io", "library/nginx", "latest", "sha256:abc")]
    [DataRow("registry-1.docker.io/nginx:1.27@sha256:ABC", "docker.io", "library/nginx", "1.27", "sha256:abc")]
    [DataRow("DOCKER.IO/Nginx", "docker.io", "library/nginx", "latest", null)]
    [DataRow("REGISTRY-1.DOCKER.IO/MyUser/App", "docker.io", "myuser/app", "latest", null)]
    [DataRow("index.docker.io/ghcr.io/owner/app:1", "ghcr.io", "owner/app", "1", null)]
    [DataRow("registry-1.docker.io/ghcr.io/owner/app:1", "ghcr.io", "owner/app", "1", null)]
    [DataRow("docker.io/ghcr.io", "docker.io", "library/ghcr.io", "latest", null)]
    [DataRow("localhost:5000/app", "localhost:5000", "app", "latest", null)]
    [DataRow("INDEX.DOCKER.IO/Foo/Bar:RC1", "docker.io", "foo/bar", "RC1", null)]
    public void ImageReferenceParserFormatCanonicalReferenceRoundTrips(string value, string expectedRegistry, string expectedRepository, string expectedTag, string? expectedDigest)
    {
        var parser = new ImageReferenceParser();
        var parsed = parser.Parse(value);
        var reference = parser.Format(CreateImageVersion(parsed.Registry, parsed.Repository, parsed.Tag, parsed.Digest));

        var reparsed = parser.Parse(reference);

        AssertReference(parsed, value, expectedRegistry, expectedRepository, expectedTag, expectedDigest);
        AssertReference(reparsed, reference, expectedRegistry, expectedRepository, expectedTag, expectedDigest);
    }

    /// <summary>
    /// Verify Format rejects a null image version
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void ImageReferenceParserFormatNullImageVersionThrowsArgumentNullException()
    {
        var parser = new ImageReferenceParser();

        Assert.ThrowsExactly<ArgumentNullException>(() => parser.Format(null!), "A null image version must throw ArgumentNullException");
    }

    /// <summary>
    /// Verify Format rejects an image version without a repository
    /// </summary>
    [TestMethod]
    [TestCategory("AC7")]
    public void ImageReferenceParserFormatImageVersionWithoutRepositoryThrowsArgumentNullException()
    {
        var parser = new ImageReferenceParser();
        var imageVersion = new ImageVersion
                           {
                               Tag = "1.0",
                               RegistryRepository = null!,
                           };

        Assert.ThrowsExactly<ArgumentNullException>(() => parser.Format(imageVersion), "An image version without a registry repository must throw ArgumentNullException");
    }

    /// <summary>
    /// Verify NormalizeDigest returns null for blank input and the lowercased digest otherwise
    /// </summary>
    /// <param name="digest">Digest or digest-bearing reference</param>
    /// <param name="expected">Expected digest</param>
    [TestMethod]
    [TestCategory("AC8")]
    [DataRow(null, null)]
    [DataRow("", null)]
    [DataRow("   ", null)]
    [DataRow(" SHA256:ABC ", "sha256:abc")]
    [DataRow("ghcr.io/org/app@sha256:ABC", "sha256:abc")]
    [DataRow("a@b@sha256:abc", "sha256:abc")]
    public void ImageReferenceParserNormalizeDigestValueReturnsNormalizedDigest(string? digest, string? expected)
    {
        var normalizedDigest = ImageReferenceParser.NormalizeDigest(digest);

        Assert.AreEqual(expected, normalizedDigest, $"Digest '{digest}' must normalize to '{expected}'");
    }

    #endregion // Methods
}