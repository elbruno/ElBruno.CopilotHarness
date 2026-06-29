using System.Text.Json;
using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Api;

public sealed class PersistentExecutionTraceStore(
    HarnessDbContext dbContext,
    ILogger<PersistentExecutionTraceStore> logger) : IExecutionTraceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Store(RoutingExecutionTrace trace)
    {
        var entity = new RoutingExecutionTraceEntity
        {
            TraceId = trace.TraceId,
            WorkflowEngine = trace.WorkflowEngine,
            PayloadJson = JsonSerializer.Serialize(trace, JsonOptions),
            CreatedAtUtc = trace.CreatedAtUtc
        };

        dbContext.RoutingExecutionTraces.Add(entity);
        dbContext.SaveChanges();

        logger.LogInformation(
            "Stored routing execution trace {TraceId} for workflow {WorkflowEngine}.",
            trace.TraceId,
            trace.WorkflowEngine);
    }

    public bool TryGet(string traceId, out RoutingExecutionTrace trace)
    {
        var entity = dbContext.RoutingExecutionTraces
            .AsNoTracking()
            .FirstOrDefault(item => item.TraceId == traceId);

        if (entity is null)
        {
            trace = null!;
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RoutingExecutionTrace>(entity.PayloadJson, JsonOptions);
            if (parsed is null)
            {
                trace = null!;
                return false;
            }

            trace = parsed;
            return true;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Routing execution trace {TraceId} could not be deserialized.", traceId);
            trace = null!;
            return false;
        }
    }

    public IReadOnlyList<RoutingExecutionTrace> GetRecent(int limit)
    {
        var normalizedLimit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var entities = dbContext.RoutingExecutionTraces
            .AsNoTracking()
            .OrderByDescending(item => item.Id)
            .Take(normalizedLimit)
            .ToList();

        var traces = new List<RoutingExecutionTrace>(entities.Count);
        foreach (var entity in entities)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<RoutingExecutionTrace>(entity.PayloadJson, JsonOptions);
                if (parsed is not null)
                {
                    traces.Add(parsed);
                }
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Routing execution trace {TraceId} could not be deserialized.", entity.TraceId);
            }
        }

        return traces
            .OrderByDescending(trace => trace.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToList();
    }

    public bool Remove(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return false;
        }

        var removed = dbContext.RoutingExecutionTraces
            .Where(item => item.TraceId == traceId)
            .ExecuteDelete();

        if (removed > 0)
        {
            logger.LogInformation("Removed routing execution trace {TraceId}.", traceId);
        }

        return removed > 0;
    }

    public int RemoveMany(IEnumerable<string> traceIds)
    {
        if (traceIds is null)
        {
            return 0;
        }

        var ids = traceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        var removed = dbContext.RoutingExecutionTraces
            .Where(item => ids.Contains(item.TraceId))
            .ExecuteDelete();

        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} routing execution traces.", removed);
        }

        return removed;
    }

    public void Clear()
    {
        var removed = dbContext.RoutingExecutionTraces.ExecuteDelete();
        logger.LogInformation("Cleared {Count} routing execution traces.", removed);
    }
}
