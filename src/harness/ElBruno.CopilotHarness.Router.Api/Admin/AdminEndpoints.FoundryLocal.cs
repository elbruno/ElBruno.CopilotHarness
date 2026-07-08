namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapFoundryLocalEndpoints(RouteGroupBuilder group)
    {
        // ── SDK status ──────────────────────────────────────────────────────────

        group.MapGet("/foundrylocal/status", (FoundryLocalSdkService sdk) =>
            Results.Ok(new FoundryLocalSdkStatusDto(
                IsInitialized: sdk.IsInitialized,
                WebServiceUrl: sdk.WebServiceUrl,
                InitError: sdk.InitError)));

        // ── Initialize (lazy) ───────────────────────────────────────────────────

        group.MapPost("/foundrylocal/init", async (
            FoundryLocalSdkService sdk,
            CancellationToken cancellationToken) =>
        {
            await sdk.EnsureInitializedAsync(cancellationToken);
            return Results.Ok(new FoundryLocalSdkStatusDto(
                IsInitialized: sdk.IsInitialized,
                WebServiceUrl: sdk.WebServiceUrl,
                InitError: sdk.InitError));
        });

        // ── Model catalog ───────────────────────────────────────────────────────

        group.MapGet("/foundrylocal/catalog", async (
            FoundryLocalSdkService sdk,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var models = await sdk.GetCatalogAsync(cancellationToken);
                return Results.Ok(models.Select(m => new FoundryCatalogModelDto(
                    Alias: m.Alias,
                    DisplayName: m.DisplayName,
                    Description: m.Description,
                    ModelId: m.ModelId,
                    IsCached: m.IsCached,
                    IsLoaded: m.IsLoaded,
                    DownloadProgress: m.DownloadProgress)).ToList());
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Failed to retrieve Foundry Local catalog",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        // ── Model download (fire-and-forget, progress queryable) ────────────────

        group.MapPost("/foundrylocal/catalog/{alias}/download", async (
            string alias,
            FoundryLocalSdkService sdk,
            ILogger<FoundryLocalSdkService> logger) =>
        {
            // Fire-and-forget: the download runs in the background. Poll
            // GET /foundrylocal/catalog/{alias}/progress to track it.
            _ = Task.Run(async () =>
            {
                try
                {
                    await sdk.DownloadModelAsync(alias, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background download of '{Alias}' failed.", alias);
                }
            });

            return Results.Accepted(
                $"/admin/foundrylocal/catalog/{Uri.EscapeDataString(alias)}/progress",
                new { alias, status = "downloading" });
        });

        // ── Per-model download progress ─────────────────────────────────────────

        group.MapGet("/foundrylocal/catalog/{alias}/progress", (
            string alias,
            FoundryLocalSdkService sdk) =>
        {
            var progress = sdk.GetDownloadProgress(alias);
            if (progress is null)
            {
                return Results.Ok(new { alias, status = "idle", progress = (float?)null });
            }

            var status = progress >= 100f ? "complete" : "downloading";
            return Results.Ok(new { alias, status, progress });
        });
    }
}
