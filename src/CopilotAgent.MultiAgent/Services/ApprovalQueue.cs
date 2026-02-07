using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Centralized approval queue that serializes concurrent tool approval requests
/// from parallel workers through a single gate, preventing dialog storms.
/// Uses SemaphoreSlim(1,1) to ensure only one approval dialog is shown at a time.
/// </summary>
public sealed class ApprovalQueue : IApprovalQueue, IDisposable
{
    private readonly IToolApprovalService _toolApprovalService;
    private readonly ILogger<ApprovalQueue> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingCount;
    private bool _disposed;

    public ApprovalQueue(
        IToolApprovalService toolApprovalService,
        ILogger<ApprovalQueue> logger)
    {
        _toolApprovalService = toolApprovalService ?? throw new ArgumentNullException(nameof(toolApprovalService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <inheritdoc />
    public event EventHandler<int>? PendingCountChanged;

    /// <inheritdoc />
    public async Task<ToolApprovalResponse> EnqueueApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        // Check if already approved via cached rules (no gate needed)
        if (_toolApprovalService.IsApproved(request.SessionId, request.ToolName, request.ToolArgs))
        {
            _logger.LogDebug(
                "Tool '{ToolName}' for session '{SessionId}' is pre-approved, skipping queue.",
                request.ToolName, request.SessionId);

            return new ToolApprovalResponse { Approved = true, Scope = ApprovalScope.Session };
        }

        IncrementPending();
        try
        {
            // Serialize: only one approval dialog at a time
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _logger.LogDebug(
                    "Processing approval for tool '{ToolName}' (session: {SessionId}, pending: {Pending})",
                    request.ToolName, request.SessionId, PendingCount);

                // Re-check after acquiring the gate — a previous approval may have
                // created a rule that now covers this request.
                if (_toolApprovalService.IsApproved(request.SessionId, request.ToolName, request.ToolArgs))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' became pre-approved while queued, skipping dialog.",
                        request.ToolName);

                    return new ToolApprovalResponse { Approved = true, Scope = ApprovalScope.Session };
                }

                var response = await _toolApprovalService
                    .RequestApprovalAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Approval result for '{ToolName}': Approved={Approved}, Scope={Scope}",
                    request.ToolName, response.Approved, response.Scope);

                return response;
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            DecrementPending();
        }
    }

    private void IncrementPending()
    {
        var newCount = Interlocked.Increment(ref _pendingCount);
        PendingCountChanged?.Invoke(this, newCount);
    }

    private void DecrementPending()
    {
        var newCount = Interlocked.Decrement(ref _pendingCount);
        PendingCountChanged?.Invoke(this, newCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}