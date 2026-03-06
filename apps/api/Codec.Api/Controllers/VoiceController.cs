using System.Security.Cryptography;
using System.Text;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages voice channel state and provides TURN credentials for WebRTC.
/// </summary>
[ApiController]
[Authorize]
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
        var appUser = await userService.GetOrCreateUserAsync(User);

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
                vs.ParticipantId,
                DisplayName = vs.User!.Nickname ?? vs.User.DisplayName,
                AvatarUrl = vs.User.CustomAvatarPath ?? vs.User.AvatarUrl,
                vs.IsMuted,
                vs.IsDeafened,
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
        var appUser = await userService.GetOrCreateUserAsync(User);

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
        var appUser = await userService.GetOrCreateUserAsync(User);

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
    /// Issues short-lived TURN credentials using HMAC-SHA256 time-limited authentication.
    /// The secret never leaves the server; clients receive a username + credential pair valid for 1 hour.
    /// Requires coturn 4.6.0+ with the <c>sha256</c> option enabled.
    /// </summary>
    [HttpGet("turn-credentials")]
    public async Task<IActionResult> GetTurnCredentials()
    {
        var turnSecret = config["Voice:TurnSecret"];
        if (string.IsNullOrWhiteSpace(turnSecret))
            throw new InvalidOperationException("Voice:TurnSecret is required.");
        var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";

        var appUser = await userService.GetOrCreateUserAsync(User);

        // Username encodes the expiry timestamp (Unix seconds) and a stable per-user identifier.
        // Using {expiry}:{userId} prevents credential sharing across users within the same
        // validity window while remaining compatible with coturn's time-limited auth scheme.
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var username = $"{expiry}:{appUser.Id}";

        var keyBytes = Encoding.UTF8.GetBytes(turnSecret);
        var msgBytes = Encoding.UTF8.GetBytes(username);
        using var hmac = new HMACSHA256(keyBytes);
        var credential = Convert.ToBase64String(hmac.ComputeHash(msgBytes));

        return Ok(new
        {
            urls = new[] { turnServerUrl },
            username,
            credential
        });
    }
}

/// <summary>Request body for PATCH /voice/state.</summary>
public record UpdateVoiceStateRequest(bool IsMuted, bool IsDeafened);
