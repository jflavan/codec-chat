using System.Security.Claims;
using System.Threading.Channels;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

[ApiController]
[Route("servers/{serverId}/discord-import")]
[Authorize]
public class DiscordImportController : ControllerBase
{
    private readonly CodecDbContext _db;
    private readonly Channel<Guid> _importQueue;
    private readonly DiscordApiClient _discordClient;
    private readonly ILogger<DiscordImportController> _logger;

    public DiscordImportController(
        CodecDbContext db,
        Channel<Guid> importQueue,
        DiscordApiClient discordClient,
        ILogger<DiscordImportController> logger)
    {
        _db = db;
        _importQueue = importQueue;
        _discordClient = discordClient;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst("sub")!.Value);

    private async Task<bool> HasManageServerPermission(Guid serverId)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);
        if (user?.IsGlobalAdmin == true) return true;

        var memberRoles = await _db.ServerMemberRoles
            .Where(mr => mr.UserId == userId && mr.Role!.ServerId == serverId)
            .Select(mr => mr.Role!.Permissions)
            .ToListAsync();

        var combined = memberRoles.Aggregate(Permission.None, (a, b) => a | b);
        return combined.Has(Permission.ManageServer);
    }

    [HttpPost]
    public async Task<IActionResult> StartImport(Guid serverId, [FromBody] StartDiscordImportRequest request)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var existing = await _db.DiscordImports
            .FirstOrDefaultAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress));
        if (existing is not null)
            return Conflict(new { error = "An import is already in progress for this server." });

        _discordClient.SetBotToken(request.BotToken);
        DiscordGuild guild;
        try
        {
            guild = await _discordClient.GetGuildAsync(request.DiscordGuildId);
        }
        catch (HttpRequestException)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID. Ensure the bot has been added to the Discord server." });
        }

        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = request.BotToken,
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = GetUserId()
        };

        _db.DiscordImports.Add(import);
        await _db.SaveChangesAsync();

        await _importQueue.Writer.WriteAsync(import.Id);

        _logger.LogInformation("Discord import {ImportId} queued for server {ServerId} from guild {GuildName}",
            import.Id, serverId, guild.Name);

        return Accepted(new { id = import.Id });
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var import = await _db.DiscordImports
            .Where(d => d.ServerId == serverId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (import is null)
            return NotFound(new { error = "No import found for this server." });

        return Ok(new
        {
            id = import.Id,
            status = import.Status.ToString(),
            importedChannels = import.ImportedChannels,
            importedMessages = import.ImportedMessages,
            importedMembers = import.ImportedMembers,
            startedAt = import.StartedAt,
            completedAt = import.CompletedAt,
            errorMessage = import.ErrorMessage,
            discordGuildId = import.DiscordGuildId
        });
    }

    [HttpPost("resync")]
    public async Task<IActionResult> Resync(Guid serverId, [FromBody] StartDiscordImportRequest request)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var lastImport = await _db.DiscordImports
            .Where(d => d.ServerId == serverId && d.Status == DiscordImportStatus.Completed)
            .OrderByDescending(d => d.CompletedAt)
            .FirstOrDefaultAsync();

        if (lastImport is null)
            return BadRequest(new { error = "No completed import found to re-sync." });

        _discordClient.SetBotToken(request.BotToken);
        try
        {
            await _discordClient.GetGuildAsync(request.DiscordGuildId);
        }
        catch (HttpRequestException)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID." });
        }

        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = request.BotToken,
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = GetUserId(),
            LastSyncedAt = lastImport.LastSyncedAt
        };

        _db.DiscordImports.Add(import);
        await _db.SaveChangesAsync();

        await _importQueue.Writer.WriteAsync(import.Id);

        return Accepted(new { id = import.Id });
    }

    [HttpDelete]
    public async Task<IActionResult> CancelImport(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var import = await _db.DiscordImports
            .FirstOrDefaultAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress));

        if (import is null)
            return NotFound(new { error = "No in-progress import to cancel." });

        import.Status = DiscordImportStatus.Cancelled;
        import.EncryptedBotToken = null;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetUserMappings(Guid serverId)
    {
        if (!await HasManageServerPermission(serverId))
            return Forbid();

        var mappings = await _db.DiscordUserMappings
            .Where(m => m.ServerId == serverId)
            .OrderBy(m => m.DiscordUsername)
            .Select(m => new
            {
                discordUserId = m.DiscordUserId,
                discordUsername = m.DiscordUsername,
                discordAvatarUrl = m.DiscordAvatarUrl,
                codecUserId = m.CodecUserId,
                claimedAt = m.ClaimedAt
            })
            .ToListAsync();

        return Ok(mappings);
    }

    [HttpPost("claim")]
    public async Task<IActionResult> ClaimIdentity(Guid serverId, [FromBody] ClaimDiscordIdentityRequest request)
    {
        var userId = GetUserId();

        var isMember = await _db.ServerMembers.AnyAsync(m => m.ServerId == serverId && m.UserId == userId);
        if (!isMember)
            return Forbid();

        var mapping = await _db.DiscordUserMappings
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordUserId == request.DiscordUserId);

        if (mapping is null)
            return NotFound(new { error = "Discord user mapping not found." });

        if (mapping.CodecUserId is not null)
            return Conflict(new { error = "This Discord identity has already been claimed." });

        var user = await _db.Users.FindAsync(userId);
        if (user?.DiscordSubject is not null && user.DiscordSubject != request.DiscordUserId)
            return BadRequest(new { error = "Your linked Discord account doesn't match this identity." });

        mapping.CodecUserId = userId;
        mapping.ClaimedAt = DateTimeOffset.UtcNow;

        var channelIds = await _db.Channels
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync();

        await _db.Messages
            .Where(m => channelIds.Contains(m.ChannelId) &&
                        m.ImportedAuthorName != null &&
                        m.AuthorUserId == null &&
                        m.AuthorName == mapping.DiscordUsername)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AuthorUserId, userId)
                .SetProperty(m => m.AuthorName, user!.DisplayName)
                .SetProperty(m => m.ImportedAuthorName, (string?)null)
                .SetProperty(m => m.ImportedAuthorAvatarUrl, (string?)null));

        await _db.SaveChangesAsync();

        return Ok(new { claimed = true });
    }
}
