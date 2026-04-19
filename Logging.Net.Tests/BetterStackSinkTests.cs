using System.Collections.Generic;
using Logging.Net.Serilog;
using Serilog.Events;

namespace Logging.Net.Tests;

public class BetterStackSinkTests
{
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
}
