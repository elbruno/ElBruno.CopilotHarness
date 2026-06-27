using System.Threading.Channels;

namespace ElBruno.CopilotHarness.Router.Api.BackgroundJobs;

public sealed class ChannelBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<BackgroundJobDelegate> _channel =
        Channel.CreateUnbounded<BackgroundJobDelegate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(BackgroundJobDelegate job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public ValueTask<BackgroundJobDelegate> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
