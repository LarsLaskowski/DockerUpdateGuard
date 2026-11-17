# Plan: Data-driven tests for ImageReferenceParser

Source: Issue #10
Tier: tests — the change adds test cases to one existing test file and touches no production code.
Security areas touched: none

## Problem / root cause

`ImageReferenceParserTests` (src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs:17) holds one test, for
the `docker.io/mcr.microsoft.com/…` unwrap. The parsing rules stated in
[Image catalog](../../docs/areas/image-catalog.md#image-references) (lines 15–39) are otherwise unpinned, as are
`Format` and `NormalizeDigest`. No defect: every case below matches the area document on reading the code.

## Verified facts

| # | Claim | Evidence | Status |
| - | ----- | -------- | ------ |
| 1 | The test class has exactly one test | ImageReferenceParserTests.cs:16–32 | confirmed |
| 2 | `nginx` → `docker.io`, `library/nginx`, tag `latest` | ImageReferenceParser.cs:118, 130–134 | confirmed |
| 3 | Digest is the text after the first `@`, trimmed, lowercased; digest-only keeps tag `latest`; `nginx@` has no digest | :108–113, 118, 153, 21–35 | confirmed |
| 4 | `localhost:5000/app` has registry `localhost:5000` and tag `latest` (colon before the last slash is no tag) | :116–119, 43–50 | confirmed |
| 5 | Registry and repository are lowercased, the tag keeps its case; empty segments are dropped | :120, 133–143, 118 | confirmed |
| 6 | `docker.io/nginx` keeps repository `nginx`, `index.docker.io` stays its own registry (documented, not a defect) | :135–139, image-catalog.md:35–37 | confirmed |
| 7 | Unwrap only for mcr.microsoft.com, ghcr.io, quay.io, host-like names containing `harbor`, with at least two repository segments | :57–95 | confirmed |
| 8 | Null input throws `ArgumentNullException`, blank `ArgumentException` (`ThrowIfNullOrWhiteSpace`); `/`, `//`, `:1.0`, `/:1.0`, `@sha256:abc` throw `ArgumentException` for `value` with message starting `The image reference does not contain a repository name` | :104, 120–125 | confirmed |
| 9 | `Format` omits `@digest` for a null or blank digest and throws `ArgumentNullException` for a null version or repository | :158–166 | confirmed |
| 10 | `NormalizeDigest` is internal and visible to the test project | :19, src/DockerUpdateGuard/AssemblyInfo.cs:3 | confirmed |
| 11 | MSTest 4 offers `Assert.ThrowsExactly` | Directory.Packages.props:18 (4.5.3), RequestDeadlineHandlerTests.cs:121 | confirmed |

Issue claims confirmed: one test only; the listed forms untested.

## Acceptance criteria

Each row asserts registry, repository, tag and digest of the result (digest `null` where none).

- AC1: A reference without a registry gets `docker.io`, a single segment the `library/` namespace, a missing or empty
  tag `latest`; whitespace around the reference is ignored (`nginx`, `NGINX`, ` nginx:1.25 `, `nginx:`,
  `library/nginx`, `myuser/app:2`).
- AC2: A first segment with `.`, `:` or `localhost` is the registry, lowercased, with the rest as a multi-part
  repository; the tag keeps its case (`localhost:5000/app`, `localhost:5000/app:1.0`, `LOCALHOST/App`,
  `ghcr.io/org/team/app:v1`, `Registry.Example.com/Team/App:RC1`, `ghcr.io//org/app`).
- AC3: Docker Hub spellings stay as documented: `docker.io/nginx` → repository `nginx`; `index.docker.io/library/nginx`
  → registry `index.docker.io`.
- AC4: Digests: `nginx@sha256:ABC` → tag `latest`, digest `sha256:abc`; `nginx:1.25@sha256:abc`;
  `registry.example.com:5000/team/app:2.0@ sha256:Def ` → digest `sha256:def`; `nginx@` → digest null.
- AC5: Known wrapped registries are unwrapped (`docker.io/ghcr.io/owner/app:1`, `docker.io/quay.io/org/app`,
  `DOCKER.IO/Harbor.Example.com/Project/App:3` → `harbor.example.com`, `project/app`, tag `3`); others stay on
  `docker.io` (`docker.io/harbor/app`, `docker.io/registry.example.com/app`, `docker.io/ghcr.io`).
- AC6: Invalid input throws as fact 8 states: null; `""`, `"   "`, `"\t"`; and the five forms without a repository
  (exception type exactly, `ParamName` `value`, message prefix).
- AC7: `Format` returns `registry/repository:tag` for a null, empty or blank digest and appends `@digest` otherwise;
  `Parse(Format(v))` gives back the same parts (`localhost:5000/app:1.0`, `docker.io/library/nginx:Latest@sha256:abc`);
  a null version or a version with a null repository throws `ArgumentNullException`.
- AC8: `NormalizeDigest` returns null for null and blank input, and the lowercased text after the last `@`
  otherwise (` SHA256:ABC `, `ghcr.io/org/app@sha256:ABC`, `a@b@sha256:abc` → `sha256:abc`).

## Tests per criterion

| AC | Test(s) | Test file |
| -- | ------- | --------- |
| AC1 | ImageReferenceParserParseReferenceWithoutRegistryUsesDockerHubDefaults | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC2 | ImageReferenceParserParseReferenceWithRegistryHostSplitsRegistryAndRepository | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC3 | ImageReferenceParserParseDockerHubSpellingKeepsSpelledRegistryAndRepository | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC4 | ImageReferenceParserParseReferenceWithDigestNormalizesDigest | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC5 | ImageReferenceParserParseMirrorWrappedReferenceUnwrapsKnownRegistry, ImageReferenceParserParseMirrorWrappedReferenceOfUnknownHostKeepsDockerHub | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC6 | ImageReferenceParserParseNullReferenceThrowsArgumentNullException, ImageReferenceParserParseBlankReferenceThrowsArgumentException, ImageReferenceParserParseReferenceWithoutRepositoryNameThrowsArgumentException | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC7 | ImageReferenceParserFormatImageVersionReturnsCanonicalReference, ImageReferenceParserFormatCanonicalReferenceParsesBackToSameParts, ImageReferenceParserFormatNullImageVersionThrowsArgumentNullException, ImageReferenceParserFormatImageVersionWithoutRepositoryThrowsArgumentNullException | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |
| AC8 | ImageReferenceParserNormalizeDigestValueReturnsNormalizedDigest | src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs |

## Approach

One `[DataRow]`-parameterized `[TestMethod]` per scenario (`docs/UNIT_TESTS.md`), at most five parameters (value,
expected registry, repository, tag, digest), each test with its `[TestCategory("ACn")]`. The tab of AC6 is written as
the C# escape `"\t"`. The existing test stays unchanged (`docs/UNIT_TESTS.md` quotes it). These tests pass on today's
code; a failing row is a parser defect to report back to the Lead, never a reason to change the expectation silently.

## Signatures (for the Dev's skeleton)

None: no production API changes.

## Test files

src/Tests/DockerUpdateGuard.Tests/ImageReferenceParserTests.cs (extended). Existing call sites of changed signatures:
none. Files no unit test can cover: none.

## Areas

None (no change in behavior).

## Documentation updates

None: the area document already states every rule tested; no user-visible change, so no changelog entry.

## Architecture check

No guarantee touched; test code only.

## Security considerations

- [x] No limits.
- [x] None: no production code changes.

## Decision records

- none: no decision beyond the obvious tests

## Out of scope / follow-ups

Unifying Docker Hub spellings (`docker.io/nginx`, `index.docker.io`) is documented behavior; changing it is a
separate feature, not this issue.
