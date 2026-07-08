namespace ElBruno.CopilotHarness.Router.Api.Admin;

public static partial class AdminEndpoints
{
    private static void MapSettingsEndpoints(RouteGroupBuilder group)
    {
        // Demo routing footer ON/OFF — runtime toggle so a presenter can flip it live without a restart.
        group.MapGet("/settings/response-annotation", (IResponseAnnotationState state) =>
            Results.Ok(new ResponseAnnotationSettingDto(state.Enabled)));

        group.MapPut("/settings/response-annotation", (ResponseAnnotationSettingDto request, IResponseAnnotationState state) =>
        {
            state.Enabled = request.Enabled;
            return Results.Ok(new ResponseAnnotationSettingDto(state.Enabled));
        });
    }
}
