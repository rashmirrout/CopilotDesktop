using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// JSONL file-based structured log store for orchestration observability.
/// Each plan gets its own .jsonl file. Thread-safe for concurrent writes
/// from parallel workers via per-file write locks.
/// </summary>
public sealed class JsonTaskLogStore : ITaskLogStore
{
    private readonly ILogger<JsonTaskLogStore> _logger;
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonTaskLogStore(ILogger<JsonTaskLogStore> logger, string? logDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logDirectory = logDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CopilotAgent", "orchestration-logs");

        Directory.CreateDirectory(_logDirectory);
    }

    /// <inheritdoc />
    public async Task SaveLogEntryAsync(
        string planId, string? chunkId, LogEntry entry, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(entry);

        // Stamp plan/chunk IDs into the entry for consistency
        entry.PlanId = planId;
        if (chunkId is not null)
            entry.ChunkId = chunkId;

        var filePath = GetLogFilePath(planId);
        var line = JsonSerializer.Serialize(entry, s_jsonOptions);

        var fileLock = _fileLocks.GetOrAdd(planId, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write log entry for plan '{PlanId}'", planId);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<List<LogEntry>> GetChunkLogsAsync(string chunkId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);

        var allEntries = await ReadAllEntriesAsync(ct).ConfigureAwait(false);
        return allEntries
            .Where(e => string.Equals(e.ChunkId, chunkId, StringComparison.Ordinal))
            .OrderBy(e => e.TimestampUtc)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<LogEntry>> GetPlanLogsAsync(string planId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        return await ReadPlanEntriesAsync(planId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<LogEntry>> GetLogsByLevelAsync(
        string planId, OrchestrationLogLevel minLevel, CancellationToken ct = default)
    {
        var entries = await ReadPlanEntriesAsync(planId, ct).ConfigureAwait(false);
        return entries.Where(e => e.Level >= minLevel).ToList();
    }

    /// <inheritdoc />
    public async Task<string> ExportPlanLogsAsJsonAsync(string planId, CancellationToken ct = default)
    {
        var entries = await ReadPlanEntriesAsync(planId, ct).ConfigureAwait(false);
        var exportOptions = new JsonSerializerOptions(s_jsonOptions) { WriteIndented = true };
        return JsonSerializer.Serialize(entries, exportOptions);
    }

    /// <inheritdoc />
    public async Task<List<LogEntry>> GetTimelineAsync(string planId, CancellationToken ct = default)
    {
        // Timeline is just plan logs ordered chronologically — same as GetPlanLogsAsync
        return await ReadPlanEntriesAsync(planId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task PruneLogsAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        try
        {
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.jsonl"))
            {
                ct.ThrowIfCancellationRequested();
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                    _logger.LogInformation("Pruned old log file: {File}", Path.GetFileName(file));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error pruning old log files");
        }

        return Task.CompletedTask;
    }

    // ── Private helpers ─────────────────────────────────────────

    private string GetLogFilePath(string planId)
    {
        // Sanitize planId for use as filename
        var safe = string.Join("_", planId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_logDirectory, $"{safe}.jsonl");
    }

    private async Task<List<LogEntry>> ReadPlanEntriesAsync(string planId, CancellationToken ct)
    {
        var filePath = GetLogFilePath(planId);
        if (!File.Exists(filePath))
            return new List<LogEntry>();

        var entries = new List<LogEntry>();
        var fileLock = _fileLocks.GetOrAdd(planId, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<LogEntry>(line, s_jsonOptions);
                    if (entry is not null)
                        entries.Add(entry);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping malformed log line in {File}", filePath);
                }
            }
        }
        finally
        {
            fileLock.Release();
        }

        return entries.OrderBy(e => e.TimestampUtc).ToList();
    }

    /// <summary>
    /// Read all entries from all JSONL files (used for cross-plan queries like GetChunkLogsAsync).
    /// </summary>
    private async Task<List<LogEntry>> ReadAllEntriesAsync(CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        if (!Directory.Exists(_logDirectory))
            return entries;

        foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.jsonl"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct).ConfigureAwait(false);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<LogEntry>(line, s_jsonOptions);
                        if (entry is not null)
                            entries.Add(entry);
                    }
                    catch (JsonException)
                    {
                        // Skip malformed lines in cross-plan scan
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read log file: {File}", file);
            }
        }

        return entries;
    }
}