namespace CopilotAgent.MultiAgent.Services;

using CopilotAgent.Core.Models;

/// <summary>
/// Centralized approval queue that serializes tool approval requests from
/// parallel workers through a single gate, preventing dialog storms.
/// All worker tool approvals are funneled through this queue, which
/// delegates to <see cref="IToolApprovalService"/> one at a time.
/// </summary>
public interface IApprovalQueue
{
    /// <summary>
    /// Enqueue a tool approval request. The request will be processed
    /// serially (one at a time) to prevent multiple approval dialogs
    /// from appearing simultaneously when parallel workers request approvals.
    /// </summary>
    /// <param name="request">The tool approval request from a worker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The approval response from the user or auto-approval rules.</returns>
    Task<ToolApprovalResponse> EnqueueApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The number of approval requests currently waiting in the queue.
    /// </summary>
    int PendingCount { get; }

    /// <summary>
    /// Raised when the pending approval count changes.
    /// The event argument is the new pending count.
    /// </summary>
    event EventHandler<int>? PendingCountChanged;
}