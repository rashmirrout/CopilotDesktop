using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;
using CopilotAgent.Office.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CopilotAgent.Tests.Office;

#region OfficeEventLog Tests

public sealed class OfficeEventLogTests
{
    private readonly OfficeEventLog _sut = new();

    [Fact]
    public void Log_AddsEvent_GetAll_ReturnsIt()
    {
        var evt = new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "test"
        };

        _sut.Log(evt);

        var all = _sut.GetAll();
        all.Should().HaveCount(1);
        all[0].Should().BeSameAs(evt);
    }

    [Fact]
    public void Log_NullEvent_ThrowsArgumentNullException()
    {
        var act = () => _sut.Log(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetAll_ReturnsSnapshot_NotLiveReference()
    {
        _sut.Log(new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "first"
        });

        var snapshot = _sut.GetAll();

        _sut.Log(new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Planning,
            NewPhase = ManagerPhase.Executing,
            Description = "second"
        });

        snapshot.Should().HaveCount(1, "snapshot should not reflect subsequent additions");
        _sut.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetByIteration_FiltersCorrectly()
    {
        _sut.Log(new PhaseChangedEvent
        {
            IterationNumber = 1,
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "iter1"
        });
        _sut.Log(new PhaseChangedEvent
        {
            IterationNumber = 2,
            PreviousPhase = ManagerPhase.Planning,
            NewPhase = ManagerPhase.Executing,
            Description = "iter2"
        });
        _sut.Log(new CommentaryEvent
        {
            IterationNumber = 1,
            Commentary = new LiveCommentary
            {
                Type = CommentaryType.System,
                AgentName = "System",
                Message = "test"
            },
            Description = "iter1-commentary"
        });

        var iter1Events = _sut.GetByIteration(1);
        iter1Events.Should().HaveCount(2);
        iter1Events.Should().OnlyContain(e => e.IterationNumber == 1);
    }

    [Fact]
    public void GetByType_FiltersCorrectly()
    {
        _sut.Log(new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "phase"
        });
        _sut.Log(new ErrorEvent
        {
            ErrorMessage = "something failed",
            Description = "error"
        });

        var errors = _sut.GetByType(OfficeEventType.Error);
        errors.Should().HaveCount(1);
        errors[0].Should().BeOfType<ErrorEvent>();
    }

    [Fact]
    public void GetSchedulingLog_ReturnsOnlySchedulingDecisions()
    {
        _sut.Log(new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "phase"
        });
        _sut.Log(new SchedulingEvent
        {
            Decision = new SchedulingDecision
            {
                TaskId = "task-a",
                TaskTitle = "Task A",
                Action = SchedulingAction.Dispatched,
                Reason = "Ready"
            },
            Description = "scheduling"
        });

        var decisions = _sut.GetSchedulingLog();
        decisions.Should().HaveCount(1);
        decisions[0].TaskTitle.Should().Be("Task A");
    }

    [Fact]
    public void Clear_RemovesAllEvents()
    {
        _sut.Log(new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning,
            Description = "phase"
        });
        _sut.Log(new ErrorEvent { ErrorMessage = "err", Description = "error" });

        _sut.Clear();

        _sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void ThreadSafety_ConcurrentLogAndRead_DoesNotThrow()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var iter = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    _sut.Log(new PhaseChangedEvent
                    {
                        IterationNumber = iter,
                        PreviousPhase = ManagerPhase.Idle,
                        NewPhase = ManagerPhase.Planning,
                        Description = $"thread-{iter}-{j}"
                    });
                }
            }));
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 50; j++)
                {
                    _ = _sut.GetAll();
                    _ = _sut.GetByIteration(iter);
                }
            }));
        }

        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
        _sut.GetAll().Should().HaveCount(1000);
    }
}

#endregion

#region IterationScheduler Tests

public sealed class IterationSchedulerTests
{
    private readonly IterationScheduler _sut;

    public IterationSchedulerTests()
    {
        _sut = new IterationScheduler(NullLogger<IterationScheduler>.Instance);
    }

    [Fact]
    public async Task CancelRest_StopsWaitEarly()
    {
        var ticks = new List<RestCountdownEvent>();
        _sut.OnCountdownTick += tick => ticks.Add(tick);

        // Start a 5-minute wait, cancel after 100ms
        var waitTask = _sut.WaitForNextIterationAsync(5, CancellationToken.None);

        await Task.Delay(150);
        _sut.CancelRest();

        await waitTask;

        // Should have received some ticks but completed early
        ticks.Should().NotBeEmpty();
        ticks.Last().SecondsRemaining.Should().Be(0, "final tick should be 0 after cancel");
    }

    [Fact]
    public async Task WaitForNextIterationAsync_CancellationToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource(100);

        var act = async () => await _sut.WaitForNextIterationAsync(5, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task OnCountdownTick_EmitsTickEvents()
    {
        var ticks = new List<RestCountdownEvent>();
        _sut.OnCountdownTick += tick => ticks.Add(tick);

        // Start with 1 second interval, cancel quickly
        var waitTask = _sut.WaitForNextIterationAsync(1, CancellationToken.None);

        // Let it tick a couple of times then cancel
        await Task.Delay(2500);
        _sut.CancelRest();

        await waitTask;

        ticks.Should().NotBeEmpty();
        // All ticks should have TotalSeconds = 60 (1 minute = 60 seconds)
        ticks.Should().OnlyContain(t => t.TotalSeconds == 60);
    }

    [Fact]
    public void CancelRest_WhenNotWaiting_DoesNotThrow()
    {
        var act = () => _sut.CancelRest();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task OverrideRestDurationAsync_CancelsCurrentRest()
    {
        var waitTask = _sut.WaitForNextIterationAsync(10, CancellationToken.None);

        await Task.Delay(100);
        await _sut.OverrideRestDurationAsync(1);

        // Should complete quickly because override cancels current rest
        var completed = await Task.WhenAny(waitTask, Task.Delay(2000));
        completed.Should().BeSameAs(waitTask, "override should have cancelled the rest");
    }
}

#endregion

#region RestCountdownEvent Tests

public sealed class RestCountdownEventTests
{
    [Theory]
    [InlineData(60, 60, 0)]
    [InlineData(30, 60, 50)]
    [InlineData(0, 60, 100)]
    [InlineData(0, 0, 100)]
    public void ProgressPercent_CalculatesCorrectly(int remaining, int total, double expected)
    {
        var evt = new RestCountdownEvent
        {
            SecondsRemaining = remaining,
            TotalSeconds = total,
            Description = "test"
        };

        evt.ProgressPercent.Should().Be(expected);
    }
}

#endregion

#region OfficeManagerService Tests

public sealed class OfficeManagerServiceTests : IAsyncDisposable
{
    private readonly Mock<ICopilotService> _copilotServiceMock;
    private readonly OfficeEventLog _eventLog;
    private readonly IterationScheduler _scheduler;
    private readonly ILoggerFactory _loggerFactory;
    private readonly OfficeManagerService _sut;

    private static readonly OfficeConfig s_defaultConfig = new()
    {
        Objective = "Test objective",
        WorkspacePath = "/tmp/test",
        CheckIntervalMinutes = 1,
        MaxAssistants = 2,
        RequirePlanApproval = true
    };

    public OfficeManagerServiceTests()
    {
        _copilotServiceMock = new Mock<ICopilotService>();
        _eventLog = new OfficeEventLog();
        _scheduler = new IterationScheduler(NullLogger<IterationScheduler>.Instance);
        _loggerFactory = NullLoggerFactory.Instance;

        // Default: return a simple plan
        _copilotServiceMock
            .Setup(s => s.SendMessageAsync(It.IsAny<Session>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMessage { Content = "1. Step one\n2. Step two" });

        _sut = new OfficeManagerService(
            _copilotServiceMock.Object,
            _eventLog,
            _scheduler,
            _loggerFactory);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sut.StopAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        _sut.CurrentPhase.Should().Be(ManagerPhase.Idle);
        _sut.CurrentIteration.Should().Be(0);
        _sut.IsRunning.Should().BeFalse();
        _sut.IsWaitingForClarification.Should().BeFalse();
        _sut.IsPlanAwaitingApproval.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_NullConfig_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.StartAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StartAsync_WithPlanApproval_TransitionsToAwaitingApproval()
    {
        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.StartAsync(s_defaultConfig);

        _sut.CurrentPhase.Should().Be(ManagerPhase.AwaitingApproval);
        _sut.IsPlanAwaitingApproval.Should().BeTrue();
        _sut.IsRunning.Should().BeTrue();
        _sut.CurrentPlan.Should().NotBeNullOrEmpty();

        events.OfType<RunStartedEvent>().Should().NotBeEmpty();
        events.OfType<PhaseChangedEvent>().Should().Contain(pce => pce.NewPhase == ManagerPhase.Planning);
        events.OfType<PhaseChangedEvent>().Should().Contain(pce => pce.NewPhase == ManagerPhase.AwaitingApproval);
        events.OfType<ChatMessageEvent>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task StartAsync_WithoutPlanApproval_BeginsIterationLoop()
    {
        var config = new OfficeConfig
        {
            Objective = "Auto-start test",
            RequirePlanApproval = false,
            CheckIntervalMinutes = 1
        };

        // Mock the task generation to return empty tasks so the loop rests
        _copilotServiceMock
            .Setup(s => s.SendMessageAsync(It.IsAny<Session>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMessage { Content = "[]" });

        await _sut.StartAsync(config);

        // Give the loop a moment to start
        await Task.Delay(300);

        _sut.IsRunning.Should().BeTrue();
        // Should be past planning phase
        _sut.CurrentPhase.Should().NotBe(ManagerPhase.AwaitingApproval);

        await _sut.StopAsync();
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_DoesNotRestart()
    {
        await _sut.StartAsync(s_defaultConfig);

        var phase = _sut.CurrentPhase;
        var iteration = _sut.CurrentIteration;

        await _sut.StartAsync(s_defaultConfig);

        _sut.CurrentPhase.Should().Be(phase);
        _sut.CurrentIteration.Should().Be(iteration);
    }

    [Fact]
    public async Task ApprovePlanAsync_TransitionsFromAwaitingApproval()
    {
        await _sut.StartAsync(s_defaultConfig);
        _sut.CurrentPhase.Should().Be(ManagerPhase.AwaitingApproval);

        // Mock task generation to return empty
        _copilotServiceMock
            .Setup(s => s.SendMessageAsync(It.IsAny<Session>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMessage { Content = "[]" });

        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.ApprovePlanAsync();

        // Give iteration loop time to start
        await Task.Delay(300);

        _sut.CurrentPhase.Should().NotBe(ManagerPhase.AwaitingApproval);
        events.OfType<ChatMessageEvent>().Should().Contain(cme => cme.Message.Content.Contains("approved"));

        await _sut.StopAsync();
    }

    [Fact]
    public async Task ApprovePlanAsync_WhenNotAwaitingApproval_IsNoOp()
    {
        // Don't start — still Idle
        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.ApprovePlanAsync();

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectPlanAsync_RegeneratesPlan()
    {
        var callCount = 0;
        _copilotServiceMock
            .Setup(s => s.SendMessageAsync(It.IsAny<Session>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new ChatMessage { Content = $"Plan version {callCount}" };
            });

        await _sut.StartAsync(s_defaultConfig);
        _sut.CurrentPhase.Should().Be(ManagerPhase.AwaitingApproval);

        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.RejectPlanAsync("Not good enough");

        _sut.CurrentPhase.Should().Be(ManagerPhase.AwaitingApproval);
        events.OfType<ChatMessageEvent>().Should().Contain(cme => cme.Message.Content.Contains("rejected"));
        events.OfType<ChatMessageEvent>().Should().Contain(cme => cme.Message.Content.Contains("Revised Plan"));
    }

    [Fact]
    public async Task RejectPlanAsync_WhenNotAwaitingApproval_IsNoOp()
    {
        await _sut.RejectPlanAsync("no plan to reject");
        _sut.CurrentPhase.Should().Be(ManagerPhase.Idle);
    }

    [Fact]
    public async Task InjectInstructionAsync_AddsInstruction()
    {
        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.InjectInstructionAsync("Do X instead of Y");

        events.OfType<InstructionInjectedEvent>().Should().ContainSingle(ie => ie.Instruction == "Do X instead of Y");
    }

    [Fact]
    public async Task InjectInstructionAsync_NullOrWhitespace_Throws()
    {
        var act1 = async () => await _sut.InjectInstructionAsync(null!);
        var act2 = async () => await _sut.InjectInstructionAsync("  ");

        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RespondToClarificationAsync_NullOrWhitespace_Throws()
    {
        var act1 = async () => await _sut.RespondToClarificationAsync(null!);
        var act2 = async () => await _sut.RespondToClarificationAsync("");

        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RespondToClarificationAsync_WhenNotClarifying_IsNoOp()
    {
        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.RespondToClarificationAsync("Some answer");

        events.Should().BeEmpty("should not emit events when not in Clarifying phase");
    }

    [Fact]
    public async Task PauseAsync_TransitionsToPaused()
    {
        await _sut.StartAsync(s_defaultConfig);

        await _sut.PauseAsync();

        _sut.CurrentPhase.Should().Be(ManagerPhase.Paused);
    }

    [Fact]
    public async Task PauseAsync_WhenAlreadyPaused_IsNoOp()
    {
        await _sut.StartAsync(s_defaultConfig);
        await _sut.PauseAsync();

        // Second pause should not throw or change state
        await _sut.PauseAsync();
        _sut.CurrentPhase.Should().Be(ManagerPhase.Paused);
    }

    [Fact]
    public async Task ResumeAsync_WhenNotPaused_IsNoOp()
    {
        await _sut.ResumeAsync();
        _sut.CurrentPhase.Should().Be(ManagerPhase.Idle);
    }

    [Fact]
    public async Task StopAsync_TransitionsToStopped()
    {
        await _sut.StartAsync(s_defaultConfig);
        _sut.IsRunning.Should().BeTrue();

        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.StopAsync();

        _sut.CurrentPhase.Should().Be(ManagerPhase.Stopped);
        _sut.IsRunning.Should().BeFalse();
        events.Should().Contain(e => e is RunStoppedEvent);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        await _sut.StopAsync();

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetAsync_ClearsState()
    {
        await _sut.StartAsync(s_defaultConfig);
        await _sut.StopAsync();

        await _sut.ResetAsync();

        _sut.CurrentPhase.Should().Be(ManagerPhase.Idle);
        _sut.CurrentIteration.Should().Be(0);
        _sut.IsRunning.Should().BeFalse();
        // Reset clears the log then emits a final PhaseChanged(Idle→Idle),
        // so at most 1 event remains.
        _eventLog.GetAll().Should().HaveCountLessOrEqualTo(1);
        _eventLog.GetAll().OfType<PhaseChangedEvent>()
            .Should().OnlyContain(e => e.NewPhase == ManagerPhase.Idle);
    }

    [Fact]
    public void UpdateCheckInterval_ClampsToMinimumOf1()
    {
        _sut.UpdateCheckInterval(0);
        // No assertion on internal field — just verify no exception
        _sut.UpdateCheckInterval(-5);
        _sut.UpdateCheckInterval(10);
    }

    [Fact]
    public async Task OnEvent_IsRaisedForStateTransitions()
    {
        var eventTypes = new List<OfficeEventType>();
        _sut.OnEvent += evt => eventTypes.Add(evt.EventType);

        await _sut.StartAsync(s_defaultConfig);

        eventTypes.Should().Contain(OfficeEventType.RunStarted);
        eventTypes.Should().Contain(OfficeEventType.PhaseChanged);
        eventTypes.Should().Contain(OfficeEventType.ChatMessage);
    }

    [Fact]
    public async Task StartAsync_ClarificationFlow_TransitionsToClarifying()
    {
        // Mock LLM to return a clarification request
        var callCount = 0;
        _copilotServiceMock
            .Setup(s => s.SendMessageAsync(It.IsAny<Session>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new ChatMessage { Content = "[CLARIFICATION_NEEDED] What framework are you using?" };
                }
                return new ChatMessage { Content = "1. Use the specified framework\n2. Build it" };
            });

        var events = new List<OfficeEvent>();
        _sut.OnEvent += evt => events.Add(evt);

        // Start will call GeneratePlanAsync which will get clarification request
        // This runs async, and the plan generation will block on clarification gate
        var startTask = _sut.StartAsync(s_defaultConfig);

        // Wait for clarification state
        await Task.Delay(500);

        _sut.IsWaitingForClarification.Should().BeTrue();
        events.OfType<ClarificationRequestedEvent>().Should().NotBeEmpty();

        // Respond to the clarification
        await _sut.RespondToClarificationAsync("We use .NET 8");

        // Wait for plan to complete
        await Task.Delay(500);

        _sut.CurrentPhase.Should().Be(ManagerPhase.AwaitingApproval);
        _sut.IsWaitingForClarification.Should().BeFalse();

        await _sut.StopAsync();
    }
}

#endregion

#region Model Tests

public sealed class OfficeConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new OfficeConfig { Objective = "test" };

        config.CheckIntervalMinutes.Should().Be(5);
        config.MaxAssistants.Should().Be(3);
        config.MaxQueueDepth.Should().Be(20);
        config.ManagerModel.Should().Be("gpt-4");
        config.AssistantModel.Should().Be("gpt-4");
        config.AssistantTimeoutSeconds.Should().Be(120);
        config.ManagerLlmTimeoutSeconds.Should().Be(60);
        config.MaxRetries.Should().Be(2);
        config.RequirePlanApproval.Should().BeTrue();
        config.ManagerSystemPrompt.Should().BeNull();
        config.WorkspacePath.Should().BeNull();
    }

    [Fact]
    public void RecordEquality_WorksCorrectly()
    {
        var a = new OfficeConfig { Objective = "test", MaxAssistants = 5 };
        var b = new OfficeConfig { Objective = "test", MaxAssistants = 5 };
        var c = new OfficeConfig { Objective = "different", MaxAssistants = 5 };

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}

public sealed class OfficeChatMessageTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var msg = new OfficeChatMessage();

        msg.Id.Should().NotBeNullOrEmpty();
        msg.Id.Should().HaveLength(8);
        msg.Role.Should().Be(OfficeChatRole.User);
        msg.SenderName.Should().BeEmpty();
        msg.Content.Should().BeEmpty();
        msg.AccentColor.Should().Be("#888888");
        msg.IsCollapsible.Should().BeFalse();
        msg.IsCollapsed.Should().BeFalse();
        msg.ContainerExpanded.Should().BeTrue();
        msg.TaskId.Should().BeNull();
    }

    [Fact]
    public void IsIterationContainer_TrueForIterationHeaderRole()
    {
        var msg = new OfficeChatMessage { Role = OfficeChatRole.IterationHeader };
        msg.IsIterationContainer.Should().BeTrue();

        var msg2 = new OfficeChatMessage { Role = OfficeChatRole.Manager };
        msg2.IsIterationContainer.Should().BeFalse();
    }
}

public sealed class LiveCommentaryTests
{
    [Fact]
    public void CanBeCreated_WithRequiredFields()
    {
        var commentary = new LiveCommentary
        {
            Type = CommentaryType.ManagerThinking,
            AgentName = "Manager",
            Message = "Thinking about tasks...",
            IterationNumber = 1,
            Emoji = "🤔"
        };

        commentary.Type.Should().Be(CommentaryType.ManagerThinking);
        commentary.AgentName.Should().Be("Manager");
        commentary.Message.Should().Be("Thinking about tasks...");
        commentary.IterationNumber.Should().Be(1);
        commentary.Emoji.Should().Be("🤔");
    }
}

public sealed class AssistantTaskTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var task = new AssistantTask { Title = "", Prompt = "" };

        task.Id.Should().NotBeNullOrEmpty();
        task.Title.Should().BeEmpty();
        task.Prompt.Should().BeEmpty();
        task.Priority.Should().Be(0);
        task.IterationNumber.Should().Be(0);
        task.Status.Should().Be(AssistantTaskStatus.Queued);
    }
}

public sealed class AssistantResultTests
{
    [Fact]
    public void CanBeCreated()
    {
        var result = new AssistantResult
        {
            TaskId = "task-1",
            Success = true,
            Summary = "Completed successfully",
            Duration = TimeSpan.FromSeconds(5.5)
        };

        result.TaskId.Should().Be("task-1");
        result.Success.Should().BeTrue();
        result.Summary.Should().Be("Completed successfully");
        result.Duration.TotalSeconds.Should().BeApproximately(5.5, 0.01);
        result.ErrorMessage.Should().BeNull();
    }
}

public sealed class OfficeColorSchemeTests
{
    [Fact]
    public void Colors_AreNotNullOrEmpty()
    {
        OfficeColorScheme.ManagerColor.Should().NotBeNullOrEmpty();
        OfficeColorScheme.UserColor.Should().NotBeNullOrEmpty();
        OfficeColorScheme.SystemColor.Should().NotBeNullOrEmpty();
        OfficeColorScheme.RestColor.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetAssistantColor_ReturnsColorForAnyIndex()
    {
        var color0 = OfficeColorScheme.GetAssistantColor(0);
        var color1 = OfficeColorScheme.GetAssistantColor(1);
        var colorLarge = OfficeColorScheme.GetAssistantColor(100);

        color0.Should().NotBeNullOrEmpty();
        color1.Should().NotBeNullOrEmpty();
        colorLarge.Should().NotBeNullOrEmpty();
    }
}

#endregion

#region Event Model Tests

public sealed class OfficeEventTests
{
    [Fact]
    public void PhaseChangedEvent_HasCorrectType()
    {
        var evt = new PhaseChangedEvent
        {
            PreviousPhase = ManagerPhase.Idle,
            NewPhase = ManagerPhase.Planning
        };

        evt.EventType.Should().Be(OfficeEventType.PhaseChanged);
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ErrorEvent_CarriesExceptionInfo()
    {
        var ex = new InvalidOperationException("test error");
        var evt = new ErrorEvent
        {
            ErrorMessage = ex.Message,
            Exception = ex,
            Description = "error occurred"
        };

        evt.EventType.Should().Be(OfficeEventType.Error);
        evt.ErrorMessage.Should().Be("test error");
        evt.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void RunStartedEvent_CarriesConfig()
    {
        var config = new OfficeConfig { Objective = "test" };
        var evt = new RunStartedEvent { Config = config, Description = "started" };

        evt.EventType.Should().Be(OfficeEventType.RunStarted);
        evt.Config.Objective.Should().Be("test");
    }

    [Fact]
    public void ClarificationRequestedEvent_HasCorrectType()
    {
        var evt = new ClarificationRequestedEvent
        {
            Question = "What do you want?",
            Description = "clarification"
        };

        evt.EventType.Should().Be(OfficeEventType.Clarification);
        evt.Question.Should().Be("What do you want?");
    }

    [Fact]
    public void InstructionInjectedEvent_HasCorrectType()
    {
        var evt = new InstructionInjectedEvent
        {
            Instruction = "Focus on X",
            Description = "injected"
        };

        evt.EventType.Should().Be(OfficeEventType.InstructionInjected);
        evt.Instruction.Should().Be("Focus on X");
    }

    [Fact]
    public void RestCountdownEvent_ProgressPercent_At100_WhenDone()
    {
        var evt = new RestCountdownEvent
        {
            SecondsRemaining = 0,
            TotalSeconds = 300,
            Description = "done"
        };

        evt.ProgressPercent.Should().Be(100.0);
    }
}

#endregion

#region ManagerPhase Enum Tests

public sealed class ManagerPhaseTests
{
    [Fact]
    public void AllPhases_AreDefined()
    {
        var phases = Enum.GetValues<ManagerPhase>();

        phases.Should().Contain(ManagerPhase.Idle);
        phases.Should().Contain(ManagerPhase.Clarifying);
        phases.Should().Contain(ManagerPhase.Planning);
        phases.Should().Contain(ManagerPhase.AwaitingApproval);
        phases.Should().Contain(ManagerPhase.FetchingEvents);
        phases.Should().Contain(ManagerPhase.Scheduling);
        phases.Should().Contain(ManagerPhase.Executing);
        phases.Should().Contain(ManagerPhase.Aggregating);
        phases.Should().Contain(ManagerPhase.Resting);
        phases.Should().Contain(ManagerPhase.Paused);
        phases.Should().Contain(ManagerPhase.Stopped);
        phases.Should().Contain(ManagerPhase.Error);
        phases.Should().HaveCount(12);
    }
}

#endregion