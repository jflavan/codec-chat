using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Codec.Api.Filters;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages voice channel state and provides LiveKit access tokens for WebRTC.
/// </summary>
[ApiController]
[Authorize]
[RequireEmailVerified]
[EnableRateLimiting("fixed")]
[Route("voice")]
public class VoiceController(CodecDbContext db, IUserService userService, IConfiguration config) : ControllerBase
{
    /// <summary>
    /// Returns the current users in a voice channel and their mute/deafen state.
    /// Requires server membership or global admin.
    /// </summary>
    [HttpGet("/channels/{channelId:guid}/voice-states")]
    public async Task<IActionResult> GetVoiceStates(Guid channelId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var channel = await db.Channels
            .AsNoTracking()
            .Include(c => c.Server)
            .FirstOrDefaultAsync(c => c.Id == channelId);

        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        if (channel.Type is not ChannelType.Voice)
        {
            return BadRequest(new { error = "Channel is not a voice channel." });
        }

        await userService.EnsureMemberAsync(channel.ServerId, appUser.Id, appUser.IsGlobalAdmin);

        var states = await db.VoiceStates
            .AsNoTracking()
            .Where(vs => vs.ChannelId == channelId)
            .Select(vs => new
            {
                vs.UserId,
                DisplayName = vs.User!.Nickname ?? vs.User.DisplayName,
                AvatarUrl = vs.User.CustomAvatarPath ?? vs.User.AvatarUrl,
                vs.IsMuted,
                vs.IsDeafened,
                vs.IsVideoEnabled,
                vs.IsScreenSharing,
                vs.JoinedAt
            })
            .ToListAsync();

        return Ok(states);
    }

    /// <summary>
    /// Updates the current user's mute/deafen state in the database.
    /// Returns the updated state.
    /// </summary>
    [HttpPatch("state")]
    public async Task<IActionResult> UpdateVoiceState([FromBody] UpdateVoiceStateRequest request)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var voiceState = await db.VoiceStates
            .FirstOrDefaultAsync(vs => vs.UserId == appUser.Id);

        if (voiceState is null)
        {
            return BadRequest(new { error = "You are not currently in a voice channel." });
        }

        voiceState.IsMuted = request.IsMuted;
        voiceState.IsDeafened = request.IsDeafened;
        await db.SaveChangesAsync();

        return Ok(new { voiceState.UserId, voiceState.IsMuted, voiceState.IsDeafened });
    }

    /// <summary>
    /// Returns the caller's active or ringing VoiceCall, if any.
    /// Used on page load/reconnect to restore call state.
    /// </summary>
    [HttpGet("active-call")]
    public async Task<IActionResult> GetActiveCall()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var call = await db.VoiceCalls
            .AsNoTracking()
            .Where(c => (c.CallerUserId == appUser.Id || c.RecipientUserId == appUser.Id)
                && (c.Status == VoiceCallStatus.Ringing || c.Status == VoiceCallStatus.Active))
            .Select(c => new
            {
                c.Id,
                c.DmChannelId,
                c.CallerUserId,
                c.RecipientUserId,
                status = c.Status.ToString().ToLowerInvariant(),
                c.StartedAt,
                c.AnsweredAt
            })
            .FirstOrDefaultAsync();

        if (call is null)
            return NoContent();

        var otherUserId = call.CallerUserId == appUser.Id ? call.RecipientUserId : call.CallerUserId;
        var otherUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == otherUserId);

        return Ok(new
        {
            call.Id,
            call.DmChannelId,
            call.CallerUserId,
            call.RecipientUserId,
            call.status,
            call.StartedAt,
            call.AnsweredAt,
            otherUserId,
            otherDisplayName = otherUser.Nickname ?? otherUser.DisplayName,
            otherAvatarUrl = otherUser.CustomAvatarPath ?? otherUser.AvatarUrl
        });
    }

    /// <summary>
    /// Issues a LiveKit access token for the specified room.
    /// Validates that the user is authorized to join the room (server voice channel
    /// membership or active DM call participant).
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> GetToken([FromQuery] string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return BadRequest(new { error = "roomName is required." });

        // Validate roomName format: must be a GUID (channel) or "call-{GUID}" (DM call).
        Guid callId = default;
        var isChannelRoom = Guid.TryParse(roomName, out var channelGuid);
        var isCallRoom = !isChannelRoom && roomName.StartsWith("call-")
            && Guid.TryParse(roomName["call-".Length..], out callId);
        if (!isChannelRoom && !isCallRoom)
            return BadRequest(new { error = "roomName must be a channel GUID or 'call-{callId}'." });

        var apiKey = config["LiveKit:ApiKey"];
        var apiSecret = config["LiveKit:ApiSecret"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            throw new InvalidOperationException("LiveKit:ApiKey and LiveKit:ApiSecret must be configured.");

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        // Authorize: the user must have an active VoiceState for this room.
        var hasVoiceState = false;
        if (isChannelRoom)
        {
            hasVoiceState = await db.VoiceStates.AsNoTracking()
                .AnyAsync(vs => vs.UserId == appUser.Id && vs.ChannelId == channelGuid);
        }
        else
        {
            var call = await db.VoiceCalls.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == callId && c.Status == VoiceCallStatus.Active);
            if (call is not null)
            {
                hasVoiceState = await db.VoiceStates.AsNoTracking()
                    .AnyAsync(vs => vs.UserId == appUser.Id && vs.DmChannelId == call.DmChannelId);
            }
        }

        if (!hasVoiceState && !appUser.IsGlobalAdmin)
            return Forbid();

        var token = new AccessToken(apiKey, apiSecret)
            .WithIdentity(appUser.Id.ToString())
            .WithName(appUser.EffectiveDisplayName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanPublish = true,
                CanSubscribe = true
            })
            .WithTtl(TimeSpan.FromMinutes(15));

        return Ok(new { token = token.ToJwt() });
    }
}

/// <summary>Request body for PATCH /voice/state.</summary>
public record UpdateVoiceStateRequest(bool IsMuted, bool IsDeafened);
