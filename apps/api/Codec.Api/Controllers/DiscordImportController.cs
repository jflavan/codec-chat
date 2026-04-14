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

        // Quick pre-check before expensive Discord API call (DB unique index is the ultimate guard)
        var existing = await _db.DiscordImports
            .AnyAsync(d => d.ServerId == serverId &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress || d.Status == DiscordImportStatus.RehostingMedia));
        if (existing)
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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "An import is already in progress for this server." });
        }

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
            lastSyncedAt = import.LastSyncedAt,
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
            .Where(d => d.ServerId == serverId && (d.Status == DiscordImportStatus.Completed || d.Status == DiscordImportStatus.Failed || d.Status == DiscordImportStatus.RehostingMedia))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastImport is null)
            return BadRequest(new { error = "No completed or stuck import found to re-sync." });

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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "An import is already in progress for this server." });
        }

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
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress || d.Status == DiscordImportStatus.RehostingMedia));

        if (import is null)
            return NotFound(new { error = "No in-progress import to cancel." });

        // Cancel the token first so the worker observes cancellation before we touch the DB.
        // The worker's catch(OperationCanceledException) block handles the status transition.
        _cancellationRegistry.Cancel(import.Id);

        // Use atomic update with status filter to avoid overwriting a terminal state
        // set by the worker between our read and this write.
        await _db.DiscordImports
            .Where(d => d.Id == import.Id &&
                (d.Status == DiscordImportStatus.Pending || d.Status == DiscordImportStatus.InProgress || d.Status == DiscordImportStatus.RehostingMedia))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DiscordImportStatus.Cancelled)
                .SetProperty(d => d.EncryptedBotToken, (string?)null));

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

        // If the user has linked their Discord account, verify it matches the claimed identity.
        // If not linked, allow trust-based claiming (admins can revoke if needed).
        if (currentUser.DiscordSubject is not null && currentUser.DiscordSubject != request.DiscordUserId)
            return BadRequest(new { error = "Your linked Discord account doesn't match this identity." });

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Check inside the transaction to prevent concurrent claims of different identities.
        var alreadyClaimed = await _db.DiscordUserMappings
            .FromSqlRaw(
                """SELECT * FROM "DiscordUserMappings" WHERE "ServerId" = {0} AND "CodecUserId" = {1} FOR UPDATE""",
                serverId, currentUser.Id)
            .AnyAsync();
        if (alreadyClaimed)
        {
            await transaction.RollbackAsync();
            return Conflict(new { error = "You have already claimed a Discord identity in this server." });
        }

        // Lock the target mapping row to prevent the same identity being claimed concurrently.
        var mapping = await _db.DiscordUserMappings
            .FromSqlRaw(
                """SELECT * FROM "DiscordUserMappings" WHERE "ServerId" = {0} AND "DiscordUserId" = {1} FOR UPDATE""",
                serverId, request.DiscordUserId)
            .FirstOrDefaultAsync();

        if (mapping is null)
        {
            await transaction.RollbackAsync();
            return NotFound(new { error = "Discord user mapping not found." });
        }

        if (mapping.CodecUserId is not null)
        {
            await transaction.RollbackAsync();
            return Conflict(new { error = "This Discord identity has already been claimed." });
        }

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
