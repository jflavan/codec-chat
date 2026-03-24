using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

[ApiController]
[Route("push-subscriptions")]
[Authorize]
public class PushSubscriptionsController(CodecDbContext db, IUserService userService) : ControllerBase
{
    /// <summary>Register or re-activate a push subscription for the current user.</summary>
    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] CreatePushSubscriptionRequest request)
    {
        var (user, _) = await userService.GetOrCreateUserAsync(User);

        // Upsert: if the endpoint already exists, re-activate and update keys.
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint);

        if (existing is not null)
        {
            existing.UserId = user.Id;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.IsActive = true;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            db.PushSubscriptions.Add(new Models.PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth
            });
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Remove a push subscription (user unsubscribed).</summary>
    [HttpDelete]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        var (user, _) = await userService.GetOrCreateUserAsync(User);

        var count = await db.PushSubscriptions
            .Where(s => s.UserId == user.Id && s.Endpoint == request.Endpoint)
            .ExecuteDeleteAsync();

        return count > 0 ? NoContent() : NotFound();
    }

    /// <summary>Get the VAPID public key so the client can subscribe.</summary>
    [HttpGet("vapid-key")]
    [AllowAnonymous]
    public IActionResult GetVapidKey([FromServices] IConfiguration config)
    {
        var publicKey = config["Vapid:PublicKey"];
        if (string.IsNullOrEmpty(publicKey))
            return NotFound(new { detail = "Push notifications are not configured." });

        return Ok(new { publicKey });
    }
}

public class UnsubscribeRequest
{
    public string Endpoint { get; set; } = "";
}
