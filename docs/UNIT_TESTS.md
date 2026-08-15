# How to create Unit Tests for DockerUpdateGuard

## Overview

This document describes how unit tests are written in this repository. It is
binding for both human contributors and AI agents: new tests must follow the
conventions below, and existing tests are the reference implementation — when
in doubt, look at a neighboring test file in the same project before inventing
a new pattern.

## Unit tests vs. other test types

1. **Unit tests**

   A unit test exercises an individual component or method in isolation. Unit
   tests should only test code within the developer's control; they do not
   exercise infrastructure concerns such as real databases, file systems, or
   network resources.

2. **Integration tests**

   An integration test exercises two or more components working together and
   often does include infrastructure concerns. The `DockerUpdateGuard.Data.Tests`
   project sits closer to this category: it runs real EF Core migrations and
   queries against a SQLite database rather than mocking the data layer.

3. **Load tests**

   DockerUpdateGuard does not currently have load tests.

## Why unit test?

- **Fast feedback.** Unit tests run in milliseconds and don't require manual
  steps through the UI.
- **Protection against regression.** The full suite can be rerun after every
  change to confirm existing behavior still holds.
- **Executable documentation.** A well-named test tells the reader what a
  method does for a given input without needing to read its implementation.
- **Less coupled code.** Code that is hard to unit test is usually a sign of
  tight coupling; writing the test first tends to produce better-decoupled
  designs.

## Test stack

| Concern | Tooling |
| --- | --- |
| Test framework | MSTest (`[TestClass]`, `[TestMethod]`, `[DataRow]`) |
| Mocking | NSubstitute (`Substitute.For<T>()`, `Arg.Any<T>()`) |
| Blazor component rendering | bUnit, wired up for MudBlazor via `Helper.BlazorTestContextFactory.Create()` |
| EF Core (host/app layer, `DockerUpdateGuard.Tests`) | `Microsoft.EntityFrameworkCore.InMemory` |
| EF Core (data layer, `DockerUpdateGuard.Data.Tests`) | SQLite in-memory via `Data.SqliteTestDatabase` |
| Code coverage | `coverlet.collector`, collected via `dotnet test --collect:"XPlat Code Coverage"` |

Do not introduce xUnit, NUnit, FluentAssertions, or Moq — the project standardizes on
MSTest, NSubstitute, and MSTest's own `Assert`/`CollectionAssert` APIs.

## Where tests live

- Tests live under `src\Tests`, never in a top-level `tests` folder.
- `src\Tests\DockerUpdateGuard.Tests` tests the main host/application layer
  (services, background jobs, Blazor components, EF Core InMemory).
- `src\Tests\DockerUpdateGuard.Data.Tests` tests the data layer (repositories,
  query services, migrations) against SQLite.
- Shared test doubles and fakes live in a project-local `Helper` folder (for
  example `DockerUpdateGuard.Tests\Helper`), reusable EF Core test fixtures in
  a `Data` folder (for example `Data.SqliteTestDatabase`).
- One test file per type under test. Name the file `{TypeUnderTest}Tests.cs`.
  When a single type has distinct concerns worth separating (for example
  rendering vs. persisted state of a Blazor page), split into
  `{TypeUnderTest}RenderTests.cs` and `{TypeUnderTest}PersistentStateTests.cs`
  rather than growing one file indefinitely.

## Naming your tests

Test method names are a single PascalCase identifier with no underscores,
built from three parts, concatenated directly:

- The name of the **type or method** being tested.
- The **scenario** under which it's being tested.
- The **expected behavior** when the scenario is invoked.

Async test methods keep the `Async` suffix as the final word.

**Why?** The name alone must explain the test's intent when it shows up in a
failing test list, without opening the file.

**Examples from this codebase:**

```csharp
public void ImageReferenceParserParseWrappedMicrosoftRegistryReferenceNormalizesRegistry()
public void UpdateDetectionServiceDigestChangeReturnsCurrentTagUpdate()
public void MyImagesGetSectionTitleWithUserNameIncludesUserName()
public async Task VulnerabilityEnrichmentServiceRefreshAsyncProviderExceptionMarksRunFailedAsync()
```

Test class names follow `{TypeUnderTest}Tests` (or `{TypeUnderTest}RenderTests`
/ `{TypeUnderTest}PersistentStateTests` when split, per `CLAUDE.md`).

## Arranging your tests

Follow Arrange, Act, Assert without labeling the sections with comments — a
blank line before the act and before the assert block is enough to separate
them, consistent with the blank-line rules in
`.github/instructions/csharp.instructions.md`.

```csharp
[TestMethod]
public void ImageReferenceParserParseWrappedMicrosoftRegistryReferenceNormalizesRegistry()
{
    var parser = new ImageReferenceParser();

    var imageReference = parser.Parse("docker.io/mcr.microsoft.com/mssql/server:2019-CU32-GDR7-ubuntu-20.04");

    Assert.AreEqual("mcr.microsoft.com",
                    imageReference.Registry,
                    "Wrapped Microsoft registry references must be normalized back to mcr.microsoft.com");
}
```

## Always include an assertion message

Every `Assert.*` call must include a message explaining what the assertion
guarantees — not what it checks mechanically, but why it matters. This is a
binding project rule (see `CLAUDE.md`), not a suggestion:

```csharp
Assert.AreEqual(UpdateEvaluationStatus.UpdateAvailable,
                evaluation.Status,
                "A changed digest on the current tag must be treated as an available update");
```

Prefer MSTest's own `Assert` and `CollectionAssert` members
(`Assert.AreEqual`, `Assert.HasCount`, `Assert.AreSequenceEqual`,
`Assert.Contains`, `Assert.IsTrue`, ...) directly instead of FluentAssertions.

## Write minimally passing tests

Use the simplest input that exercises the behavior under test. Minimal tests
stay resilient to unrelated changes elsewhere in the type and keep the focus
on behavior rather than implementation details.

## Avoid logic in tests

Do not add `if`, `for`, `while`, or `switch` statements inside a test body.
When multiple inputs must be checked against the same behavior, use MSTest's
`[DataRow]` on a single parameterized `[TestMethod]` instead of writing
conditional logic:

```csharp
[TestMethod]
[DataRow("Completed", Color.Success)]
[DataRow("Failed", Color.Error)]
[DataRow("Running", Color.Info)]
public void MyImagesGetScanStatusColorKnownStatusReturnsExpectedColor(string status, Color expectedColor)
{
    var color = (Color)_getScanStatusColorMethod.Invoke(null, [status])!;

    Assert.AreEqual(expectedColor,
                    color,
                    $"Scan status '{status}' must map to Color.{expectedColor}");
}
```

## Prefer helper methods over constructor setup

MSTest constructs a fresh test class instance per test, so shared state is
already isolated. Even so, do not build shared fixtures in the constructor.
Use a private (or private static) helper method instead, following the
`#region Static methods` / `#region Methods` ordering from
`.github/instructions/csharp.instructions.md`:

```csharp
#region Static methods

/// <summary>
/// Create an update detection service with the default scanning options
/// </summary>
/// <returns>Update detection service</returns>
private static UpdateDetectionService CreateService()
{
    return CreateService(new ScanningOptions());
}

#endregion // Static methods
```

**Why?** All setup relevant to a test stays visible from the call site, and
there is no risk of over-setting-up state that later tests then depend on.

## Avoid multiple acts

Include a single logical action per test. When a scenario needs multiple
related outcomes checked, that's still one act followed by multiple
assertions — not multiple acts. Add a separate `[TestMethod]`, or a
`[DataRow]`-parameterized test, for each distinct scenario instead of
branching within one test.

## Mocking with NSubstitute

Use `Substitute.For<TInterface>()` for collaborators and `Arg.Any<T>()` /
`Arg.Is<T>()` to configure return values:

```csharp
var vulnerabilityProvider = Substitute.For<IVulnerabilityProvider>();

vulnerabilityProvider.GetVulnerabilitiesAsync(Arg.Any<ImageReference>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromException<ExternalOperationResult<IReadOnlyList<VulnerabilityAdvisoryData>>>(new InvalidOperationException("provider boom")));
```

Only mock the collaborators you don't own or that touch infrastructure
(HTTP clients, external providers). Prefer exercising real, in-process
collaborators (parsers, calculators, EF Core against InMemory/SQLite) over
mocking them, so the test also verifies the wiring between them.

## Testing EF Core-backed code

- In `DockerUpdateGuard.Tests`, use `Microsoft.EntityFrameworkCore.InMemory`
  for tests that need a `DbContext` but are not verifying SQL-specific or
  migration behavior.
- In `DockerUpdateGuard.Data.Tests`, use the `Data.SqliteTestDatabase` helper,
  which opens a `:memory:` SQLite connection and calls
  `Database.EnsureCreated()`, so repository and query-service tests run
  against real EF Core query translation:

```csharp
using (var database = new SqliteTestDatabase())
{
    var dbContext = database.CreateDbContext();

    await using (dbContext.ConfigureAwait(false))
    {
        var repository = new ImageCatalogRepository(dbContext);

        // Act + Assert against the repository
    }
}
```

- Migration tests (`Update{N}MigrationTests.cs`) follow the migration naming
  rules from `CLAUDE.md` (`InitialCreate`, `Update1`, `Update2`, ...).

## Testing Blazor components

Blazor page and component tests use bUnit, configured for MudBlazor through
`Helper.BlazorTestContextFactory.Create()`. Register only the services the
component under test needs, render it, and assert against the rendered
markup:

```csharp
var testContext = BlazorTestContextFactory.Create();

await using (testContext)
{
    var viewService = Substitute.For<IApplicationViewService>();

    viewService.GetDashboardAsync(Arg.Any<CancellationToken>())
               .Returns(Task.FromResult(new DashboardViewData { /* ... */ }));

    testContext.Services.AddSingleton(viewService);

    var component = testContext.Render<DashboardPage>();
    var markup = component.Markup;

    Assert.Contains("Observed Images", markup, "The Observed Images metric tile must render");
}
```

For non-public members that a component intentionally does not expose
publicly (for example a static markup-formatting helper), reflection via
`MethodInfo` is an accepted pattern in this codebase — see
`MyImagesTests.cs` for the reference implementation.

## XML documentation on tests

Per `.github/instructions/csharp.instructions.md`, XML documentation is
required on all members, including test classes and test methods. Document
what the test verifies, not what MSTest attribute it carries:

```csharp
/// <summary>
/// Verify digest changes on the current tag produce an update
/// </summary>
[TestMethod]
public void UpdateDetectionServiceDigestChangeReturnsCurrentTagUpdate()
{
    // ...
}
```

## Code coverage

Code coverage is collected with `coverlet.collector`, already referenced by
both test projects — no separate installation is needed to collect coverage
during `dotnet test`.

Run the full suite with coverage collection, as documented in `README.md` and
`CLAUDE.md`:

```shell
dotnet test src\Tests\**\*.csproj -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
```

This produces a `coverage.cobertura.xml` file per test project under its
`TestResults` folder. To turn those into a browsable HTML report locally,
install [ReportGenerator](https://reportgenerator.io) once:

```shell
dotnet tool install --global dotnet-reportgenerator-globaltool
```

and merge the reports:

```shell
reportgenerator "-reports:src\Tests\**\TestResults\**\coverage.cobertura.xml" "-targetdir:coverage-report" -reporttypes:Html
```

## Checklist for new tests

- [ ] Test class named `{TypeUnderTest}Tests` (or `...RenderTests` /
      `...PersistentStateTests` when split), in the matching test project.
- [ ] Test method named `{TypeUnderTest}{Scenario}{ExpectedResult}` (PascalCase,
      no underscores, `Async` suffix for async tests).
- [ ] `[TestClass]` / `[TestMethod]` (MSTest), `[DataRow]` instead of in-test
      branching for multiple inputs.
- [ ] Arrange / Act / Assert, separated by blank lines, one act per test.
- [ ] Every `Assert.*` call includes an explanatory message.
- [ ] Collaborators mocked with NSubstitute only where they cross an
      infrastructure boundary; real objects and real EF Core otherwise.
- [ ] Shared setup factored into a private/static helper method, not a
      constructor.
- [ ] `#region` layout and XML docs follow
      `.github/instructions/csharp.instructions.md`.
- [ ] `reihitsu-format ./` run before committing.
