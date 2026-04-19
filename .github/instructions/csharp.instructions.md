# C# Code Style Instructions

This file describes the preferred C# code style conventions.
Copilot and other AI assistants must follow these guidelines when generating or modifying code.

---

## Naming Conventions

### General

| Element | Convention | Example |
|---|---|---|
| Classes | PascalCase | `PacketAnalyzer`, `SessionViewData` |
| Interfaces | `I` + PascalCase | `ITelemetryClient`, `IAppMetrics` |
| Methods | PascalCase | `GetSessionData`, `ConfigureTracing` |
| Properties | PascalCase | `LastError`, `IsRunning` |
| Private fields | `_` + camelCase | `_logger`, `_dbContext`, `_serviceProvider` |
| Constants (private) | PascalCase | `ServiceName`, `DefaultMetricName` |
| Constants (public) | PascalCase | `F1MeterName` |
| Parameters | camelCase | `packetHeader`, `serviceProvider` |
| Local variables | camelCase | `sessionData`, `isConfigured` |
| Enums | PascalCase (type and values) | `PacketTypes.CarTelemetry` |
| Enum types | Plural when enumerating | `PacketTypes`, `WeatherCondition` |

### Type Suffixes

- Controllers: `*Controller` (e.g. `SessionsController`)
- Repositories: `*Repository` (e.g. `SessionRepository`)
- ViewData/DTOs: `*ViewData` or `*Data` (e.g. `SessionViewData`, `LiveDriverData`)
- Exceptions: `*Exception` (e.g. `DbException`)
- Tests: `*Tests` (e.g. `PacketHeader2025Tests`)
- Factories: `*Factory` (e.g. `RepositoryFactory`)

---

## File Structure

### File and Type Layout

- The **main type name must match the file name exactly** (for example, `SessionsController` in `SessionsController.cs`)
- Use **exactly one top-level type per file**
- Classes, interfaces, enums, structs, and delegates are each placed in their own file
- Do not keep helper classes or helper interfaces in the same file as the primary type

### Namespaces

- Use **file-scoped namespaces**:

```csharp
namespace F1Server.Core;
```

### Using Directives

- **Outside** the namespace block
- Grouped with blank lines between groups:
  1. System namespaces
  2. Own project namespaces
  3. Third-party namespaces (Microsoft, etc.)

```csharp
using System;
using System.Collections.Concurrent;

using F1Server.Core;
using F1Server.Core.Data;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
```

### Regions

**Regions are used extensively** for structuring within classes. Each region has a descriptive name and `#endregion` is annotated with `// Name`:

```csharp
#region Fields

private readonly ILogger<MyClass> _logger;

#endregion // Fields

#region Constructors

/// <summary>
/// Constructor
/// </summary>
public MyClass(ILogger<MyClass> logger)
{
    _logger = logger;
}

#endregion // Constructors

#region Properties

/// <summary>
/// Is running?
/// </summary>
public bool IsRunning { get; private set; }

#endregion // Properties

#region Methods

/// <summary>
/// Do something
/// </summary>
public void DoSomething()
{
}

#endregion // Methods
```

**Typical region order within a class:**
1. `Constants` or `Const fields`
2. `Fields`
3. `Constructors`
4. `Properties`
5. `Static methods`
6. `Methods`
7. `Controller methods` (for controllers)
8. `IDisposable` / `IDisposable implementation`
9. Interface implementations (e.g. `IAppMetrics properties`, `IAppMetrics methods`)

---

## Code Formatting

### General

- **Indentation**: 4 spaces (no tabs)
- **Line endings**: CRLF
- **No trailing newline** at end of file (`SA1518`, `layoutRules.newlineAtEndOfFile = "omit"`)
- **Braces**: Allman style (new line before opening brace)
- **Braces always required** for `if`, `for`, `while`, etc. — even for single-line bodies

### Parameter Formatting (Constructors, Methods, `new()`)

- Method declarations, constructor declarations, method calls, constructor calls, and `new()` calls with **one or two parameters stay on a single line**
- Parameter lists should only be wrapped once there are **more than two parameters**
- When wrapping is required, the first parameter goes on the **same line** as the method/constructor/call
- Subsequent parameters are **aligned under the first parameter**:

```csharp
// Two parameters stay on one line
public MyService(ILogger<MyService> logger, IMemoryCache cache)
{
    _logger = logger;
    _cache = cache;
}

// Constructor with more than two parameters
public MyService(ILogger<MyService> logger,
                 IMemoryCache cache,
                 IOptions<MyOptions> options)
{
    _logger = logger;
    _cache = cache;
    _options = options.Value;
}

// Method call with more than two parameters
_logger.LogInformation("Message {Param1}, {Param2}",
                       value1,
                       value2);

// new() call with more than two parameters
var instance = new MyClass(param1,
                           param2,
                           param3);
```

- Do not split a two-parameter declaration or call across multiple lines just for alignment

### Method Chaining

- First call stays on the same line
- All subsequent calls are placed **under the dot** of the first call:

```csharp
builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

return seriesList.OrderBy(s => s.Name)
                 .ToList();

result.Series.Select((s, i) => (Series: s, Index: i))
             .Where(x => x.Series.Name.Contains(searchTerm));
```

### Object Initializers

- Keep short object initializers on a single line
- For multi-line object initializers:
  - keep the opening brace on the same line as the type
  - put each assigned member on its own line
  - align assignments consistently
  - keep the closing brace on its own line

```csharp
var sessionData = new SessionViewData
{
    Name = "Practice",
    IsActive = true,
};
```

### Blank Lines Between Statement Types

- **Insert a blank line** when switching between:
  - Method call → variable assignment
  - Variable assignment → method call
  - Method call or variable assignment → control block (`if`, `for`, `foreach`, `while`, `try`, `switch`, `using`, `return`)
- **No blank line** between consecutive method calls or consecutive variable assignments
- **No blank line** immediately before `continue`, `break`, or `return` when they finish the current control block branch

```csharp
var stopwatch = Stopwatch.StartNew();
var scanResult = new ScanResult();

OnProgressChanged?.Invoke("Scanning directory...");
_logger.LogInformation("Starting directory scan");

var seriesList = _directoryScanner.ScanDirectory();
scanResult.Series = seriesList;

foreach (var series in seriesList)
{
    // ...
}
```

### Multi-line Expressions

- When an expression must wrap, split it into **logical units**, not arbitrary positions
- For wrapped binary or logical expressions, put the operator at the **start of the continuation line**
- Keep each continuation line at the same indentation level
- If a get-only expression-bodied property would need wrapping, convert it to a block-bodied property instead of splitting the expression awkwardly

```csharp
if (isEnabled
    && string.IsNullOrWhiteSpace(name) == false
    && (isReady || allowPending))
{
    return true;
}

public string Name => _settings.Name;
```

### var Usage

- **`var` is preferred** wherever possible:

```csharp
var isConfigured = false;
var sessionData = new SessionViewData();
var logger = serviceProvider.GetRequiredService<ILogger<MyClass>>();
```

### Expression-bodied Members

- **Constructors**: Never expression-bodied (`false:error`)
- **Methods**: Never expression-bodied (`false:silent`)
- **Properties, Accessors, Indexers, Lambdas, Operators**: Expression-bodied when single-line (`when_on_single_line`)
- **Get-only properties** should stay single-line when expression-bodied

```csharp
// Property - expression-bodied OK
public string Name => _name;

// Method - NOT expression-bodied
{
    return _name;
}
```

### No `this.` Prefix

- `this.` is **not used** for local calls

### No Primary Constructors

- **Primary constructors are not used** — classic constructors with field assignments are preferred

### Using Statements

- **Explicit `using` blocks** with braces are preferred (not simplified `using`):

```csharp
using (var dbFactory = RepositoryFactory.CreateInstance())
{
    dbFactory.InitDatabase();
}
```

### Switch-Case with Braces

- Each `case` block is enclosed in **braces**:

```csharp
switch (packetType)
{
    case PacketTypes.Session:
        {
            data = GetSessionData(header, rawData);
        }
        break;

    case PacketTypes.LapData:
        {
            data = GetLapData(header, rawData);
        }
        break;
}
```

---

## Null Handling & Boolean Checks

### Boolean Negation with `== false`

- Instead of `!condition`, use **`condition == false`**:

```csharp
// Preferred
if (string.IsNullOrEmpty(endpoint) == false)

// Not preferred
if (!string.IsNullOrEmpty(endpoint))
```

### Null Checks

- Use null propagation: `_logger?.LogError(...)`
- Use null coalescing: `ServiceProvider ??= serviceProvider;`
- Prefer `is null` / `is not null` for reference type comparison
- Use `string.IsNullOrEmpty()` / `string.IsNullOrWhiteSpace()` for string checks
- Guard nullable values in explicit locals before assigning to non-nullable members or dereferencing them
- When LINQ or EF Core navigation flow is unclear, materialize first and use explicit locals, lookups, or guards instead of assuming optional navigations are populated

### Async / Await Safety

- In service, infrastructure, and data-access code, append `.ConfigureAwait(false)` to awaited tasks unless resuming a specific context is required
- Apply the same pattern consistently to EF Core, HTTP, stream, and other framework async APIs to avoid CA2007 regressions

---

## Language Features & Types

- **Language keywords over BCL types**: `int` instead of `Int32`, `string` instead of `String`, etc.
- **Collection initializers**: Modern `[]` syntax for empty collections: `sessions = [];`
- **Object initializers** preferred
- **Auto-properties** preferred
- **Readonly fields** where possible
- **Pattern matching**: Use `is`, `is not`, `switch expressions` where appropriate
- **String interpolation** preferred: `$"{DefaultMetricName}.packets_received"`
- **ReadOnlySpan<byte>** for performant byte processing
- Prefer supported framework loaders/helpers over obsolete constructors or `Import`-style APIs (for example, use `X509CertificateLoader` instead of `new X509Certificate2(path)`)

---

## Dependency Injection

- **Constructor injection** with private readonly fields:

```csharp
#region Fields

private readonly ILogger<MyController> _logger;
private readonly IMemoryCache _cache;

#endregion // Fields

#region Constructors

/// <summary>
/// Constructor
/// </summary>
/// <param name="logger">Logging interface</param>
/// <param name="cache">Cache</param>
public MyController(ILogger<MyController> logger, IMemoryCache cache)
{
    _logger = logger;
    _cache = cache;
}

#endregion // Constructors
```

---

## XML Documentation

### Required for:

- All **public** classes, interfaces, enums, methods, properties
- All **internal** and **private** members (except private fields)
- Enum values documented individually

### Rules:

- **No `<remarks>`**: Do **not** add `<remarks>` sections. All relevant information must be in the `<summary>` block alone
- **All methods must have `<returns>`**: Every method with a return type must have a `<returns>` tag — including methods returning `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`
- For interface implementations: use `/// <inheritdoc/>`
- Documentation language: **English**

### Style:

```csharp
/// <summary>
/// Analyze the complete packet and return the correct data class
/// </summary>
/// <param name="receivedData">Complete received packet data</param>
/// <returns>Object</returns>
public object? GetPacketData(ReceivedPacketData receivedData)
```

```csharp
/// <summary>
/// Save the session to the database
/// </summary>
/// <param name="session">Session to save</param>
/// <returns>Task</returns>
public async Task SaveSessionAsync(Session session)
```

```csharp
/// <summary>
/// Load the session from the database
/// </summary>
/// <param name="sessionId">Session identifier</param>
/// <returns>Session view data</returns>
public async Task<SessionViewData> GetSessionAsync(int sessionId)
```

---

## Logging

- **`ILogger<T>`** via dependency injection
- **Null-conditional** calls: `_logger?.LogInformation("...")`
- Prefer structured logging

---

## Enumerations

- Each enum value is **individually XML-documented**
- First value is typically `Unknown = 0` or `NotSet = 0`
- Obsolete values marked with `[Obsolete]`:

```csharp
/// <summary>
/// Tracing target framework
/// </summary>
public enum TracingTarget
{
    /// <summary>
    /// No tracing target set
    /// </summary>
    NotSet = 0,

    /// <summary>
    /// Export to console
    /// </summary>
    Console,

    /// <summary>
    /// Export to zipkin
    /// </summary>
    [Obsolete]
    Zipkin,

    /// <summary>
    /// Open telemetry standard
    /// </summary>
    OpenTelemetry
}
```

---

## IDisposable Pattern

```csharp
#region IDisposable implementation

/// <summary>
/// Releases the resources used by the current instance of the class
/// </summary>
public void Dispose()
{
    _resource?.Dispose();
    _resource = null;
}

#endregion // IDisposable implementation
```

For more complex resources: Flush → Shutdown → Dispose → set to null.

---

## ASP.NET Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class MyController : ControllerBase
{
    #region Fields
    // ...
    #endregion // Fields

    #region Constructors
    // ...
    #endregion // Constructors

    #region Controller methods
    // ...
    #endregion // Controller methods

    #region Methods
    // Private helper methods
    #endregion // Methods
}
```

---

## Suppressed Rules (StyleCop / Analyzer)

The following rules are intentionally disabled:

| Rule | Description |
|---|---|
| SA1101 | No `this.` prefix required |
| SA1116 | Split parameters do not need to start on a new line |
| SA1200 | Using directives outside the namespace |
| SA1309 | Fields may begin with underscore |
| SA1310 | Fields may contain underscores |
| SA1413 | No trailing comma required |
| SA1513 | No blank line required after closing brace |
| SA1629 | Documentation text does not need to end with a period |
| SA1633 | No file header required |
| SA1642 | Constructor documentation freely formulated |
| IDE0290 | No primary constructors |
| CS8618 | Non-nullable field warning disabled |
| CA1822 | Methods do not need to be static when possible |
| CA2254 | Logger template warning disabled |
