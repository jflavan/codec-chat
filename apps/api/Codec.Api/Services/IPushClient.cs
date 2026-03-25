using Lib.Net.Http.WebPush;

namespace Codec.Api.Services;

/// <summary>
/// Thin wrapper around PushServiceClient to enable unit testing.
/// </summary>
public interface IPushClient
{
    Task RequestPushMessageDeliveryAsync(PushSubscription subscription, PushMessage message, CancellationToken ct = default);
}

public class PushClientAdapter(PushServiceClient client) : IPushClient
{
    public Task RequestPushMessageDeliveryAsync(PushSubscription subscription, PushMessage message, CancellationToken ct = default)
        => client.RequestPushMessageDeliveryAsync(subscription, message, ct);
}
