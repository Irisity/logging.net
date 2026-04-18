# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
dotnet restore          # Restore NuGet packages
dotnet build            # Build all projects (multi-target: netstandard2.0, net6.0, net8.0, net10.0)
dotnet test             # Run tests (no test project exists yet)
```

CI runs on GitHub Actions (`.github/workflows/dotnet.yml`) against .NET 8.0 on Ubuntu.

## Architecture

This is a logging abstraction library with a Serilog-based implementation, structured as two projects:

- **Logging.Net.Abstractions** — Zero-dependency interface contracts (`ILog`, `LogFactory`, `LogContext`, `Level`). Consumers depend only on this.
- **Logging.Net.Serilog** — Concrete implementation using Serilog. Depends on Abstractions. Provides console, file, and BetterStack sinks.

### Key Design Patterns

**Deferred initialization**: `LogFactory` uses a static `LogImplementationAccessor` delegate so `ILog` instances can be created (via `LogFactory.Name()` / `LogFactory.NameOf<T>()`) before the Serilog implementation is initialized. `DeferredLogRouter` caches names, levels, and properties until initialization completes.

**Fluent API**: `ILog` methods (`Name()`, `With<T>()`, `Level()`) return `ILog` for chaining. Actual log output happens via terminal methods like `.Info("message")`, `.Error("message")`, etc. (defined as extension methods in `ILogExtensions`).

**Hierarchical naming**: Log names are separated by `/` (e.g., `"Generator/ReadFile"`). Use `NameOf<T>()` to name from a type.

**LogContext**: Static scoped context via `LogContext.With("key", value)` — properties are added to all log entries within the `using` scope, including exceptions that escape it. Backed by Serilog's async-aware `LogContext`.

**Level system**: Custom integer levels (Error=-1, Info=0, Debug=1, Verbose=2, Trace=3) with additive accumulation. Mapped to Serilog levels in the implementation.

### BetterStackSink

Custom Serilog sink (`BetterStackSink.cs`) with async batching (queue max 5000, send batch 500), Polly retry with exponential backoff, and a shared `HttpClient` (reusing a single client is critical — see commit a8fc7e0 for the socket exhaustion fix).
