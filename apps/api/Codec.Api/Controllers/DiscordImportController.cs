using System.Threading.Channels;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IUserService _userService;
    private readonly DiscordImportCancellationRegistry _cancellationRegistry;

    public DiscordImportController(
        CodecDbContext db,
        Channel<Guid> importQueue,
        DiscordApiClient discordClient,
        ILogger<DiscordImportController> logger,
        IDataProtectionProvider dataProtection,
        IUserService userService,
        DiscordImportCancellationRegistry cancellationRegistry)
    {
        _db = db;
        _importQueue = importQueue;
        _discordClient = discordClient;
        _logger = logger;
        _dataProtection = dataProtection;
        _userService = userService;
        _cancellationRegistry = cancellationRegistry;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var (user, _) = await _userService.GetOrCreateUserAsync(User);
        return user;
    }

    [HttpPost]
    public async Task<IActionResult> StartImport(Guid serverId, [FromBody] StartDiscordImportRequest request)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        await _userService.EnsurePermissionAsync(serverId, currentUser.Id, Permission.ManageServer, currentUser.IsGlobalAdmin);

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
        catch (Exception)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID. Ensure the bot has been added to the Discord server." });
        }

        var protector = _dataProtection.CreateProtector("DiscordBotToken");
        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = protector.Protect(request.BotToken),
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = currentUser.Id
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
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        await _userService.EnsurePermissionAsync(serverId, currentUser.Id, Permission.ManageServer, currentUser.IsGlobalAdmin);

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
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        await _userService.EnsurePermissionAsync(serverId, currentUser.Id, Permission.ManageServer, currentUser.IsGlobalAdmin);

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
        catch (Exception)
        {
            return BadRequest(new { error = "Invalid bot token or guild ID." });
        }

        var protector = _dataProtection.CreateProtector("DiscordBotToken");
        var import = new DiscordImport
        {
            Id = Guid.NewGuid(),
            ServerId = serverId,
            DiscordGuildId = request.DiscordGuildId,
            EncryptedBotToken = protector.Protect(request.BotToken),
            Status = DiscordImportStatus.Pending,
            InitiatedByUserId = currentUser.Id,
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
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        await _userService.EnsurePermissionAsync(serverId, currentUser.Id, Permission.ManageServer, currentUser.IsGlobalAdmin);

        var import = await _db.DiscordImports
            .FirstOrDefaultAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress));

        if (import is null)
            return NotFound(new { error = "No in-progress import to cancel." });

        import.Status = DiscordImportStatus.Cancelled;
        import.EncryptedBotToken = null;
        await _db.SaveChangesAsync();

        _cancellationRegistry.Cancel(import.Id);

        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetUserMappings(Guid serverId)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        await _userService.EnsurePermissionAsync(serverId, currentUser.Id, Permission.ManageServer, currentUser.IsGlobalAdmin);

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
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null) return Unauthorized();

        var isMember = await _db.ServerMembers.AnyAsync(m => m.ServerId == serverId && m.UserId == currentUser.Id);
        if (!isMember)
            return Forbid();

        if (currentUser.DiscordSubject is null)
            return BadRequest(new { error = "You must link your Discord account before claiming an identity. Go to Account Settings to connect Discord." });

        if (currentUser.DiscordSubject != request.DiscordUserId)
            return BadRequest(new { error = "Your linked Discord account doesn't match this identity." });

        var mapping = await _db.DiscordUserMappings
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordUserId == request.DiscordUserId);

        if (mapping is null)
            return NotFound(new { error = "Discord user mapping not found." });

        if (mapping.CodecUserId is not null)
            return Conflict(new { error = "This Discord identity has already been claimed." });

        await using var transaction = await _db.Database.BeginTransactionAsync();

        mapping.CodecUserId = currentUser.Id;
        mapping.ClaimedAt = DateTimeOffset.UtcNow;

        var channelIds = await _db.Channels
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync();

        await _db.Messages
            .Where(m => channelIds.Contains(m.ChannelId) &&
                        m.ImportedDiscordUserId != null &&
                        m.AuthorUserId == null &&
                        m.ImportedDiscordUserId == request.DiscordUserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AuthorUserId, currentUser.Id)
                .SetProperty(m => m.AuthorName, currentUser.DisplayName)
                .SetProperty(m => m.ImportedAuthorName, (string?)null)
                .SetProperty(m => m.ImportedAuthorAvatarUrl, (string?)null)
                .SetProperty(m => m.ImportedDiscordUserId, (string?)null));

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new { claimed = true });
    }
}
