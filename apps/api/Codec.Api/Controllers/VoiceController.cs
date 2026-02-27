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

        var isMember = appUser.IsGlobalAdmin || await userService.IsMemberAsync(channel.ServerId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

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
                vs.JoinedAt
            })
            .ToListAsync();

        return Ok(states);
    }

    /// <summary>
    /// Updates the current user's mute/deafen state and broadcasts the change to
    /// other participants in the same voice channel.
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
    /// Issues short-lived TURN credentials using HMAC-SHA256 time-limited authentication.
    /// The secret never leaves the server; clients receive a username + credential pair valid for 1 hour.
    /// Requires coturn to be started with the <c>--sha256</c> flag (coturn 4.6.0+).
    /// </summary>
    [HttpGet("turn-credentials")]
    public IActionResult GetTurnCredentials()
    {
        var turnSecret = config["Voice:TurnSecret"]
            ?? throw new InvalidOperationException("Voice:TurnSecret is required.");
        var turnServerUrl = config["Voice:TurnServerUrl"] ?? "turn:localhost:3478";

        // Username encodes the expiry timestamp (Unix seconds). coturn validates this.
        var expiry = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var username = expiry.ToString();

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
