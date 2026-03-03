using CopilotAgent.Office.Models;
using CopilotAgent.Office.Services;
using FluentAssertions;
using Xunit;

namespace CopilotAgent.Tests.Office;

#region ExtractionResult Value Object Tests

public sealed class ExtractionResultTests
{
    [Fact]
    public void NotFound_IsNotFound()
    {
        var result = ExtractionResult.NotFound;

        result.IsFound.Should().BeFalse();
        result.Minutes.Should().Be(0);
        result.NormalizedExpression.Should().BeEmpty();
    }

    [Fact]
    public void Found_ReturnsCorrectValues()
    {
        var result = ExtractionResult.Found(5, "every 5 minutes");

        result.IsFound.Should().BeTrue();
        result.Minutes.Should().Be(5);
        result.NormalizedExpression.Should().Be("every 5 minutes");
    }

    [Fact]
    public void Found_ClampsMinimumTo1()
    {
        var result = ExtractionResult.Found(0, "every 0 minutes");

        result.IsFound.Should().BeTrue();
        result.Minutes.Should().Be(1, "minutes should be clamped to minimum of 1");
    }

    [Fact]
    public void Found_ClampsNegativeTo1()
    {
        var result = ExtractionResult.Found(-5, "negative value");

        result.IsFound.Should().BeTrue();
        result.Minutes.Should().Be(1, "negative values should be clamped to minimum of 1");
    }

    [Fact]
    public void Found_ClampsMaximumTo60()
    {
        var result = ExtractionResult.Found(120, "every 2 hours");

        result.IsFound.Should().BeTrue();
        result.Minutes.Should().Be(60, "minutes should be clamped to maximum of 60");
    }

    [Fact]
    public void Found_TruncatesLongExpressions()
    {
        var longText = new string('x', 200);
        var result = ExtractionResult.Found(5, longText);

        result.NormalizedExpression.Should().HaveLength(100,
            "expressions longer than 100 chars should be truncated");
    }

    [Fact]
    public void StructuralEquality_Works()
    {
        var a = ExtractionResult.Found(5, "test");
        var b = ExtractionResult.Found(5, "test");

        a.Should().Be(b, "record structs should have structural equality");
    }

    [Fact]
    public void StructuralEquality_NotFoundInstances()
    {
        var a = ExtractionResult.NotFound;
        var b = ExtractionResult.NotFound;

        a.Should().Be(b);
    }
}

#endregion

#region IntervalExtractionCache Tests

public sealed class IntervalExtractionCacheTests
{
    private readonly IntervalExtractionCache _sut = new();

    [Fact]
    public void TryGet_MissReturnsNull()
    {
        var result = _sut.TryGet("some text");
        result.Should().BeNull();
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsCachedResult()
    {
        var expected = ExtractionResult.Found(5, "every 5 minutes");
        _sut.Set("check every 5 minutes", expected);

        var cached = _sut.TryGet("check every 5 minutes");
        cached.Should().Be(expected);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        var expected = ExtractionResult.Found(10, "test");
        _sut.Set("EVERY 10 MINUTES", expected);

        var cached = _sut.TryGet("every 10 minutes");
        cached.Should().Be(expected);
    }

    [Fact]
    public void Set_OverwritesPreviousValue()
    {
        _sut.Set("text", ExtractionResult.Found(5, "first"));
        _sut.Set("text", ExtractionResult.Found(10, "second"));

        var cached = _sut.TryGet("text");
        cached!.Value.Minutes.Should().Be(10);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _sut.Set("a", ExtractionResult.Found(1, "a"));
        _sut.Set("b", ExtractionResult.Found(2, "b"));

        _sut.Clear();

        _sut.TryGet("a").Should().BeNull();
        _sut.TryGet("b").Should().BeNull();
    }

    [Fact]
    public void CachesNotFoundResults()
    {
        _sut.Set("no interval here", ExtractionResult.NotFound);

        var cached = _sut.TryGet("no interval here");
        cached.Should().NotBeNull();
        cached!.Value.IsFound.Should().BeFalse();
    }

    [Fact]
    public void ThreadSafety_ConcurrentReadWrite_DoesNotThrow()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    _sut.Set($"text-{index}-{j}", ExtractionResult.Found(j % 60 + 1, $"expr-{j}"));
                    _ = _sut.TryGet($"text-{index}-{j}");
                }
            }));
        }

        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }
}

#endregion

#region IntervalChangedEvent Tests

public sealed class IntervalChangedEventTests
{
    [Fact]
    public void HasCorrectEventType()
    {
        var evt = new CopilotAgent.Office.Events.IntervalChangedEvent
        {
            PreviousIntervalMinutes = 10,
            NewIntervalMinutes = 5,
            Source = "Objective",
            NormalizedExpression = "every 5 minutes",
            Description = "test"
        };

        evt.EventType.Should().Be(CopilotAgent.Office.Events.OfficeEventType.IntervalChanged);
        evt.PreviousIntervalMinutes.Should().Be(10);
        evt.NewIntervalMinutes.Should().Be(5);
        evt.Source.Should().Be("Objective");
        evt.NormalizedExpression.Should().Be("every 5 minutes");
    }
}

#endregion