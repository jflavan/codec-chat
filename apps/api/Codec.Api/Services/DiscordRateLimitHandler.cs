using System.Globalization;

namespace Codec.Api.Services;

public class DiscordRateLimitHandler : DelegatingHandler
{
    private const int MaxRetries = 5;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null!;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            response = await base.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode != 429)
                return response;

            if (attempt == MaxRetries)
                break;

            var retryAfter = ParseRetryAfter(response);
            if (retryAfter > TimeSpan.Zero)
                await Task.Delay(retryAfter, cancellationToken);
        }

        return response;
    }

    private static TimeSpan ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (value is not null &&
                double.TryParse(value, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return TimeSpan.FromSeconds(1);
    }
}
