namespace ElBruno.CopilotHarness.Router.Api.BackgroundJobs;

public delegate ValueTask BackgroundJobDelegate(IServiceProvider services, CancellationToken cancellationToken);

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(BackgroundJobDelegate job, CancellationToken cancellationToken = default);

    ValueTask<BackgroundJobDelegate> DequeueAsync(CancellationToken cancellationToken);
}
