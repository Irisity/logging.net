using System;
using System.Collections.Generic;
using Flurl.Http.Testing;
using Logging.Net.Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Logging.Net.Tests;

public class BetterStackSinkTests
{
    private static LogEvent MakeEvent(string message) =>
        new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse(message),
            Array.Empty<LogEventProperty>());

    [Fact]
    public void Dispose_UploadsWhatIsStillQueued()
    {
        // Logging.Flush() reaches the sink as a Dispose. Cancelling the uploader without draining
        // would throw away exactly the events written on the way out of the process.
        using var http = new HttpTest();
        var sink = new BetterStackSink("token");
        sink.Emit(MakeEvent("queued-at-shutdown"));

        sink.Dispose();

        http.ShouldHaveMadeACall();
        Assert.Equal(0, sink.QueuedEventCount);
    }

    [Fact]
    public void Emit_EnforcesTheQueueCap()
    {
        // The cap has to hold while the uploader is busy, which is when the queue actually grows.
        using var http = new HttpTest();
        var sink = new BetterStackSink("token");
        try
        {
            for (int i = 0; i < 6000; i++)
            {
                sink.Emit(MakeEvent("event-" + i));
            }

            Assert.True(sink.QueuedEventCount <= 5000, $"queue grew to {sink.QueuedEventCount}");
        }
        finally
        {
            sink.Dispose();
        }
    }

    [Fact]
    public void Emit_AfterDispose_IsNotQueued()
    {
        using var http = new HttpTest();
        var sink = new BetterStackSink("token");
        sink.Dispose();

        sink.Emit(MakeEvent("too-late"));

        Assert.Equal(0, sink.QueuedEventCount);
    }

    [Theory]
    [InlineData(null, true)]  // no status at all: timeout, DNS failure, reset connection
    [InlineData(408, true)]
    [InlineData(429, true)]   // rate limited — retrying is the whole point
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]  // a malformed payload will not become valid on a retry
    [InlineData(401, false)]
    [InlineData(404, false)]
    public void IsTransientError_CoversTheFailuresThatActuallyOccur(int? statusCode, bool expected)
    {
        Assert.Equal(expected, BetterStackSink.IsTransientError(statusCode));
    }

    [Fact]
    public void RenderMessage_RecoversTheLiteralMessage()
    {
        // Messages are escaped before being handed to Serilog, so the sink must upload the rendered
        // message rather than the raw template text, which still carries the escaping.
        var literal = "regex a{2,3} and {Unfilled}";
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse(Log.EscapeTemplate(literal)),
            Array.Empty<LogEventProperty>());

        Assert.Equal(literal, logEvent.RenderMessage());
    }

    [Fact]
    public void RenderProperties_NestsStructuredValues()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["log"] = new StructureValue(new[]
            {
                new LogEventProperty("name", new ScalarValue("Generator/ReadFile")),
                new LogEventProperty("level", new ScalarValue(0)),
            }),
        };

        var result = BetterStackSink.RenderProperties(props);

        var log = Assert.IsType<Dictionary<string, object>>(result["log"]);
        Assert.Equal("Generator/ReadFile", log["name"]);
        Assert.Equal(0, log["level"]);
    }

    [Fact]
    public void RenderProperties_SplitsDottedTopLevelKeys()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["log.name"] = new ScalarValue("A"),
            ["log.level"] = new ScalarValue(1),
        };

        var result = BetterStackSink.RenderProperties(props);

        var log = Assert.IsType<Dictionary<string, object>>(result["log"]);
        Assert.Equal("A", log["name"]);
        Assert.Equal(1, log["level"]);
    }

    [Fact]
    public void RenderProperties_MergesDottedKeysWithStructuredSibling()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["log"] = new StructureValue(new[]
            {
                new LogEventProperty("name", new ScalarValue("X")),
            }),
            ["log.level"] = new ScalarValue(2),
        };

        var result = BetterStackSink.RenderProperties(props);

        var log = Assert.IsType<Dictionary<string, object>>(result["log"]);
        Assert.Equal("X", log["name"]);
        Assert.Equal(2, log["level"]);
    }

    [Fact]
    public void RenderProperties_SplitsDeeplyNestedDottedKeys()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["a.b.c.d"] = new ScalarValue("leaf"),
            ["a.b.c.e"] = new ScalarValue(42),
        };

        var result = BetterStackSink.RenderProperties(props);

        var a = Assert.IsType<Dictionary<string, object>>(result["a"]);
        var b = Assert.IsType<Dictionary<string, object>>(a["b"]);
        var c = Assert.IsType<Dictionary<string, object>>(b["c"]);
        Assert.Equal("leaf", c["d"]);
        Assert.Equal(42, c["e"]);
    }

    [Fact]
    public void RenderProperties_PreservesNullScalar()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["maybe"] = new ScalarValue(null),
            ["log"] = new StructureValue(new[]
            {
                new LogEventProperty("missing", new ScalarValue(null)),
            }),
        };

        var result = BetterStackSink.RenderProperties(props);

        Assert.Null(result["maybe"]);
        var log = Assert.IsType<Dictionary<string, object>>(result["log"]);
        Assert.Null(log["missing"]);
    }

    [Fact]
    public void RenderProperties_RendersSequenceAndDictionary()
    {
        var props = new Dictionary<string, LogEventPropertyValue>
        {
            ["items"] = new SequenceValue(new LogEventPropertyValue[]
            {
                new ScalarValue(1),
                new ScalarValue(2),
            }),
            ["map"] = new DictionaryValue(new[]
            {
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue("k"), new ScalarValue("v")),
            }),
        };

        var result = BetterStackSink.RenderProperties(props);

        var items = Assert.IsType<List<object>>(result["items"]);
        Assert.Equal(new object[] { 1, 2 }, items);

        var map = Assert.IsType<Dictionary<string, object>>(result["map"]);
        Assert.Equal("v", map["k"]);
    }

    [Fact]
    public async Task CreateGzipJsonContent_GzipsPayloadAndSetsHeaders()
    {
        var payload = "[{\"dt\":\"2026-07-25T00:00:00Z\",\"message\":\"hello\",\"level\":\"INF\"}]";

        var content = BetterStackSink.CreateGzipJsonContent(payload);

        // Headers advertise gzipped JSON so BetterStack decompresses server-side.
        Assert.Equal("gzip", Assert.Single(content.Headers.ContentEncoding));
        Assert.Equal("application/json", content.Headers.ContentType!.MediaType);

        // The body must be actual gzip that decompresses back to the exact JSON.
        var compressed = await content.ReadAsByteArrayAsync();
        using var input = new System.IO.MemoryStream(compressed);
        using var gzip = new System.IO.Compression.GZipStream(
            input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(gzip, System.Text.Encoding.UTF8);
        var decompressed = reader.ReadToEnd();

        Assert.Equal(payload, decompressed);
    }

    [Fact]
    public async Task CreateGzipJsonContent_ShrinksRepetitivePayload()
    {
        // A realistic batch is many similar JSON events; gzip should shrink it substantially.
        var payload = string.Concat(Enumerable.Repeat(
            "{\"dt\":\"2026-07-25T00:00:00Z\",\"message\":\"request handled\",\"level\":\"INF\"},", 500));

        var content = BetterStackSink.CreateGzipJsonContent(payload);

        var compressedLength = (await content.ReadAsByteArrayAsync()).Length;
        Assert.True(
            compressedLength < System.Text.Encoding.UTF8.GetByteCount(payload) / 5,
            $"expected >5x compression, got {payload.Length} -> {compressedLength} bytes");
    }
}
