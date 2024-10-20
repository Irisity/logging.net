# logging.net
[![.NET](https://github.com/Irisity/logging.net/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Irisity/logging.net/actions/workflows/dotnet.yml)

Logging abstraction written in C#/.NET Standard 2.0.

## Why another logging abstraction?

## General logging guidelines

## Getting started

Pass an `ILog` into your class and give it a name:
```
public class Generator(ILog log)
{
  private ILog log = log.NameOf<Generator>();
}
```

Use the log in a method to log something, including properties:
```
public void ReadFile(string path)
{
  using (LogContext.With("path", path)) // This will make path be added to all logging within the scope,
                                        // including when an exception is thrown out of it and logged by the caller.
  {        
    var fileContents = File.ReadAllBytes(path);

    this.log
      .With("bytes", fileContents.Length) // Include the number of bytes read into a property
      .Info("Read file"); // For searchability, the message string should always be static and unique within this log

    // ... Rest of method
  }
}
```

## Logging context

## Logging using Serilog

## Asserting logging in unit tests

## Time-and-log helper
