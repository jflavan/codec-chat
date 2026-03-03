using System.Collections.Concurrent;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Codec.Api.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

/// <summary>
/// Manages 30-second call ringing timeouts and periodic cleanup of stale calls.
/// </summary>
public class VoiceCallTimeoutService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ChatHub> hubContext,
    ILogger<VoiceCallTimeoutService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _callTimers = new();

    /// <summary>
    /// Starts a 30-second timeout for a ringing call. If the call is still Ringing
    /// when the timer fires, it is ended as Missed and both parties are notified.
    /// </summary>
    public void StartTimeout(Guid callId, Guid callerUserId, Guid recipientUserId, Guid dmChannelId)
    {
        var cts = new CancellationTokenSource();
        _callTimers[callId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // Call was answered/declined/ended before timeout
            }
            finally
            {
                _callTimers.TryRemove(callId, out _);
            }

            await HandleTimeout(callId, callerUserId, recipientUserId, dmChannelId);
        });
    }

    /// <summary>Cancels the timeout for a call (called on accept/decline/end).</summary>
    public void CancelTimeout(Guid callId)
    {
        if (_callTimers.TryRemove(callId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task HandleTimeout(Guid callId, Guid callerUserId, Guid recipientUserId, Guid dmChannelId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

            var call = await db.VoiceCalls.FirstOrDefaultAsync(c => c.Id == callId);
            if (call is null || call.Status != VoiceCallStatus.Ringing) return;

            call.Status = VoiceCallStatus.Ended;
            call.EndReason = VoiceCallEndReason.Missed;
            call.EndedAt = DateTimeOffset.UtcNow;

            // Persist system message.
            var callerUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == callerUserId);
            var systemMessage = new DirectMessage
            {
                Id = Guid.NewGuid(),
                DmChannelId = dmChannelId,
                AuthorUserId = callerUserId,
                AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
                Body = "missed",
                MessageType = MessageType.VoiceCallEvent,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.DirectMessages.Add(systemMessage);

            await db.SaveChangesAsync();

            var missedPayload = new { callId, dmChannelId };
            await hubContext.Clients.Group($"user-{callerUserId}").SendAsync("CallMissed", missedPayload);
            await hubContext.Clients.Group($"user-{recipientUserId}").SendAsync("CallMissed", missedPayload);

            // Broadcast as ReceiveDm so the system message appears in chat.
            var msgPayload = new
            {
                systemMessage.Id, systemMessage.DmChannelId, systemMessage.AuthorUserId,
                systemMessage.AuthorName, systemMessage.Body,
                imageUrl = (string?)null, systemMessage.CreatedAt,
                editedAt = (DateTimeOffset?)null,
                authorAvatarUrl = callerUser.CustomAvatarPath ?? callerUser.AvatarUrl,
                linkPreviews = Array.Empty<object>(),
                replyContext = (object?)null,
                messageType = (int)systemMessage.MessageType
            };
            await hubContext.Clients.Group($"user-{callerUserId}").SendAsync("ReceiveDm", msgPayload);
            await hubContext.Clients.Group($"user-{recipientUserId}").SendAsync("ReceiveDm", msgPayload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle call timeout for call {CallId}", callId);
        }
    }

    /// <summary>Background loop: every 60s, clean up stale Ringing calls older than 30s.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
                var staleCalls = await db.VoiceCalls
                    .Where(c => c.Status == VoiceCallStatus.Ringing && c.StartedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var call in staleCalls)
                {
                    call.Status = VoiceCallStatus.Ended;
                    call.EndReason = VoiceCallEndReason.Missed;
                    call.EndedAt = DateTimeOffset.UtcNow;

                    var callerUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == call.CallerUserId, stoppingToken);
                    if (callerUser is not null)
                    {
                        db.DirectMessages.Add(new DirectMessage
                        {
                            Id = Guid.NewGuid(),
                            DmChannelId = call.DmChannelId,
                            AuthorUserId = call.CallerUserId,
                            AuthorName = callerUser.Nickname ?? callerUser.DisplayName,
                            Body = "missed",
                            MessageType = MessageType.VoiceCallEvent,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }

                    var payload = new { callId = call.Id, dmChannelId = call.DmChannelId };
                    await hubContext.Clients.Group($"user-{call.CallerUserId}").SendAsync("CallMissed", payload, stoppingToken);
                    await hubContext.Clients.Group($"user-{call.RecipientUserId}").SendAsync("CallMissed", payload, stoppingToken);
                }

                if (staleCalls.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Cleaned up {Count} stale ringing call(s)", staleCalls.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during stale call cleanup");
            }
        }
    }
}
