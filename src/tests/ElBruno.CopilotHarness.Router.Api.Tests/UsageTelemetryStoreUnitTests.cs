using ElBruno.CopilotHarness.Router.Core.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElBruno.CopilotHarness.Router.Api.Tests;

public sealed class UsageTelemetryStoreUnitTests
{
    [Fact]
    public void Validator_RejectsInvalidPayload()
    {
        var record = new UsageTelemetryEventRecord(
            IdempotencyKey: "",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Proxy: "foundry",
            Provider: "azure_openai",
            RequestModel: "gpt-5-mini",
            ResponseModel: null,
            TraceId: null,
            SpanId: null,
            Operation: "chat",
            StatusCode: 200,
            Succeeded: true,
            InputTokens: 1,
            OutputTokens: 1,
            TotalTokens: 2);

        Assert.Throws<ArgumentException>(() => UsageTelemetryValidator.Validate(record));
    }

    [Fact]
    public async Task IngestAsync_IsIdempotentByIdempotencyKey()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var store = new UsageTelemetryStore(fixture.Context);
        var record = CreateRecord("dup-key");

        var first = await store.IngestAsync(record, CancellationToken.None);
        var second = await store.IngestAsync(record, CancellationToken.None);
        var summary = await store.GetSummaryAsync(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            null,
            null,
            null,
            CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.False(first.Duplicate);
        Assert.True(second.Accepted);
        Assert.True(second.Duplicate);
        Assert.Equal(1, summary.EventCount);
        Assert.Equal(34, summary.TotalTokens);
    }

    [Fact]
    public async Task ApplyRetentionPolicy_RemovesOnlyExpiredRows()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var store = new UsageTelemetryStore(fixture.Context);

        await store.IngestAsync(CreateRecord("old-row", DateTimeOffset.UtcNow.AddDays(-45)), CancellationToken.None);
        await store.IngestAsync(CreateRecord("new-row", DateTimeOffset.UtcNow.AddDays(-1)), CancellationToken.None);

        var deleted = await store.ApplyRetentionPolicyAsync(
            new UsageTelemetryRetentionPolicy(Enabled: true, RetentionDays: 30, MaxRowsPerRun: 100),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var summary = await store.GetSummaryAsync(
            DateTimeOffset.UtcNow.AddDays(-60),
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(1, summary.EventCount);
        Assert.Equal("new-row", summary.Rows.Single().Model);
    }

    private static UsageTelemetryEventRecord CreateRecord(string key, DateTimeOffset? occurredAtUtc = null) =>
        new(
            IdempotencyKey: key,
            OccurredAtUtc: occurredAtUtc ?? DateTimeOffset.UtcNow,
            Proxy: "foundry-proxy",
            Provider: "azure_openai",
            RequestModel: "gpt-5-mini",
            ResponseModel: key == "new-row" ? key : "gpt-5-mini",
            TraceId: "trace-1",
            SpanId: "span-1",
            Operation: "chat",
            StatusCode: 200,
            Succeeded: true,
            InputTokens: 21,
            OutputTokens: 13,
            TotalTokens: 34);

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, HarnessDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }
        public HarnessDbContext Context { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HarnessDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new HarnessDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
