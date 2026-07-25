# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
dotnet restore          # Restore NuGet packages
dotnet build            # Build all projects (multi-target: netstandard2.0, net6.0, net8.0, net10.0)
dotnet test             # Run tests (Logging.Net.Tests, targeting net8.0 and net10.0)
dotnet pack -c Release -p:Version=x.y.z   # Produce the two NuGet packages
```

Both libraries pin `LangVersion` to 7.3, because the `netstandard2.0` target constrains them to it —
post-7.3 syntax compiles on the other targets and then breaks that one.

Shared NuGet metadata lives in `Directory.Build.props`; `Version` defaults to 1.0.0 and is meant to be
overridden per release. CI runs on GitHub Actions (`.github/workflows/dotnet.yml`) on Ubuntu with the
.NET 6/8/10 SDKs installed, building all four target frameworks in Release, running the tests, and
packing the packages as build artifacts.

## Architecture

This is a logging abstraction library with a Serilog-based implementation, structured as two projects:

- **Logging.Net.Abstractions** — Zero-dependency interface contracts (`ILog`, `LogFactory`, `LogContext`, `Level`). Consumers depend only on this.
- **Logging.Net.Serilog** — Concrete implementation using Serilog. Depends on Abstractions. Provides console, file, and BetterStack sinks.

### Key Design Patterns

**Deferred initialization**: `LogFactory` uses a static `LogImplementationAccessor` delegate so `ILog` instances can be created (via `LogFactory.Name()` / `LogFactory.NameOf<T>()`) before the Serilog implementation is initialized. `DeferredLogRouter` retains names, levels, and properties and replays them onto the implementation it resolves.

**Rebinding on re-initialization**: every `ILog` keeps its configuration rather than composing it into an implementation once, and re-resolves when the thing it bound to is replaced — `DeferredLogRouter` compares the `LogImplementationAccessor` delegate reference, `Serilog.Log` compares `Logging.mainLogger`. Without this, an `ILog` that logged before a second `Init()` keeps writing into a disposed logger, and every such write is discarded silently. `Init()` must therefore hand out a *new* accessor delegate each time, and `Flush()` swaps in a silent logger rather than leaving a disposed one in place.

**Pre-init buffering**: log calls made before any accessor is configured go into `PreInitLogBuffer` (bounded at 256, drops oldest) and are replayed when `LogImplementationAccessor` is next assigned. Replayed events carry the initialization timestamp, not their original one. Tests that reset the accessor must call `PreInitLogBuffer.Clear()`, or buffered events leak into the next test.

**Fluent API**: `ILog` methods (`Name()`, `With<T>()`, `Level()`) return `ILog` for chaining. Actual log output happens via terminal methods like `.Info("message")`, `.Error("message")`, etc. (defined as extension methods in `LogExtensions`).

**Hierarchical naming**: Log names are separated by `/` (e.g., `"Generator/ReadFile"`). Use `NameOf<T>()` to name from a type.

**LogContext**: Static scoped context via `LogContext.With("key", value)` — properties are added to all log entries within the `using` scope, including exceptions that escape it. Backed by Serilog's async-aware `LogContext`.

**Level system**: Custom integer levels (Error=-1, Info=0, Debug=1, Verbose=2, Trace=3) with additive accumulation. Mapped to Serilog levels in the implementation. The accumulation applies to the severity as well: `.Level(Level.Verbose).Error(...)` accumulates to 1 and is therefore reported as Debug, and is dropped entirely if `maxLevel` is below that. This is deliberate — a level configured on a log means "everything here is that much more verbose".

**Warning**: the `Level` scale has no warning, but events bridged in from `Microsoft.Extensions.Logging` (EF Core, Kestrel) still carry one. Both the output template and the BetterStack sink render it as `INF` so the sinks agree; the event keeps its Serilog `Warning` level, so `MinimumLevel.Override` and other level filters still work as written.

**Messages are literals, not templates**: messages are expected to be static and searchable, with the data in properties, so `Serilog.Log` escapes braces before handing the message to Serilog (`Log.EscapeTemplate`). Sinks must therefore use `LogEvent.RenderMessage()` rather than `MessageTemplate.Text`, which still carries the escaping.

**`log` is a reserved property name**: the name and level are attached as a structured `log` property so the `ExpressionTemplate` can reference `log.name` (Serilog cannot reference names containing dots). A caller's property with that name is renamed to `user.log` by `Log.SafePropertyName`. This has to happen where the property is attached, not in the enricher: Serilog runs the most recently added enricher *first*, and `ForContext` properties use `AddPropertyIfAbsent`, so the reserved object is already in place and the caller's value would be dropped with no trace.

### BetterStackSink

Custom Serilog sink (`BetterStackSink.cs`) with async batching (queue max 5000, send batch 500), Polly retry with exponential backoff, and a shared `HttpClient` (reusing a single client is critical — see commit a8fc7e0 for the socket exhaustion fix).

The queue cap is enforced in `Emit`, not in the uploader: the uploader can sit inside a minutes-long retry ladder, during which it applies no back-pressure at all. `Dispose` — which is how `Logging.Flush()` and a re-`Init()` reach the sink — drains what is still queued before returning, bounded by `DrainTimeout` so shutdown cannot hang and so a blocking wait cannot deadlock on Blazor WebAssembly's single-threaded context.
