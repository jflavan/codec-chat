using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Codec.Api.Hubs;

namespace Codec.Api.Services;

public class DiscordImportService
{
    private const int MaxParallelChannels = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<DiscordImportService> _logger;

    public DiscordImportService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ChatHub> hub,
        ILogger<DiscordImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    public async Task RunImportAsync(Guid importId, string botToken, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodecDbContext>();
        var discordClient = scope.ServiceProvider.GetRequiredService<DiscordApiClient>();
        discordClient.SetBotToken(botToken);

        var import = await db.DiscordImports.FindAsync([importId], ct);
        if (import is null) return;

        if (import.Status == DiscordImportStatus.Cancelled) return;

        import.Status = DiscordImportStatus.InProgress;
        import.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var serverId = import.ServerId;
            var guildId = import.DiscordGuildId;
            var group = _hub.Clients.Group($"server-{serverId}");

            // 1. Roles
            await group.SendAsync("ImportProgress", new { stage = "Roles", completed = 0, total = 0, percentComplete = 0f }, ct);
            var roleMap = await ImportRolesAsync(db, discordClient, serverId, guildId, importId, ct);
            await db.SaveChangesAsync(ct);

            // Fetch guild channels once for categories, channels, and permission overrides
            var allGuildChannels = await discordClient.GetGuildChannelsAsync(guildId, ct);

            // 2. Categories
            await group.SendAsync("ImportProgress", new { stage = "Categories", completed = 0, total = 0, percentComplete = 10f }, ct);
            var categoryMap = await ImportCategoriesAsync(db, serverId, importId, allGuildChannels, ct);
            await db.SaveChangesAsync(ct);

            // 3. Channels
            await group.SendAsync("ImportProgress", new { stage = "Channels", completed = 0, total = 0, percentComplete = 20f }, ct);
            var channelMap = await ImportChannelsAsync(db, serverId, importId, allGuildChannels, categoryMap, ct);
            import.ImportedChannels = channelMap.Count;
            await db.SaveChangesAsync(ct);

            // 4. Channel permission overrides
            await ImportChannelPermissionOverridesAsync(db, allGuildChannels, channelMap, roleMap, ct);
            await db.SaveChangesAsync(ct);

            // 5. Custom emojis
            await group.SendAsync("ImportProgress", new { stage = "Emojis", completed = 0, total = 0, percentComplete = 30f }, ct);
            await ImportEmojisAsync(db, discordClient, serverId, guildId, importId, ct);
            await db.SaveChangesAsync(ct);

            // 6. Members
            await group.SendAsync("ImportProgress", new { stage = "Members", completed = 0, total = 0, percentComplete = 40f }, ct);
            var memberCount = await ImportMembersAsync(db, discordClient, serverId, guildId, ct);
            import.ImportedMembers = memberCount;
            await db.SaveChangesAsync(ct);

            // 7. Messages (parallel across channels)
            var textChannelIds = channelMap.ToList();
            var totalMessages = 0;
            var completedChannels = 0;
            var semaphore = new SemaphoreSlim(MaxParallelChannels);
            var messageLock = new object();

            var channelTasks = textChannelIds.Select(async kvp =>
            {
                var (discordChannelId, codecChannelId) = kvp;
                await semaphore.WaitAsync(ct);
                try
                {
                    // Each channel gets its own DbContext scope (EF contexts aren't thread-safe)
                    using var channelScope = _scopeFactory.CreateScope();
                    var channelDb = channelScope.ServiceProvider.GetRequiredService<CodecDbContext>();
                    var channelDiscord = channelScope.ServiceProvider.GetRequiredService<DiscordApiClient>();
                    channelDiscord.SetBotToken(botToken);

                    var count = await ImportChannelMessagesAsync(
                        channelDb, channelDiscord, serverId, codecChannelId, discordChannelId,
                        importId, group, ct);

                    lock (messageLock)
                    {
                        totalMessages += count;
                        completedChannels++;
                    }

                    await group.SendAsync("ImportProgress", new
                    {
                        stage = $"Messages ({completedChannels}/{textChannelIds.Count})",
                        completed = totalMessages,
                        total = 0,
                        percentComplete = 40f + ((float)completedChannels / textChannelIds.Count * 50f)
                    }, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(channelTasks);
            import.ImportedMessages = totalMessages;
            await db.SaveChangesAsync(ct);

            // 8. Pinned messages
            await group.SendAsync("ImportProgress", new { stage = "Pins", completed = 0, total = 0, percentComplete = 95f }, ct);
            await ImportPinnedMessagesAsync(db, discordClient, serverId, channelMap, importId, ct);
            await db.SaveChangesAsync(ct);

            // Complete
            import.Status = DiscordImportStatus.Completed;
            import.CompletedAt = DateTimeOffset.UtcNow;
            import.LastSyncedAt = DateTimeOffset.UtcNow;
            import.EncryptedBotToken = null;
            await db.SaveChangesAsync(ct);

            await group.SendAsync("ImportCompleted", new
            {
                importedChannels = import.ImportedChannels,
                importedMessages = import.ImportedMessages,
                importedMembers = import.ImportedMembers
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Discord import {ImportId} was cancelled", importId);
            import.Status = DiscordImportStatus.Cancelled;
            import.EncryptedBotToken = null;
            await db.SaveChangesAsync(CancellationToken.None);

            var group = _hub.Clients.Group($"server-{import.ServerId}");
            await group.SendAsync("ImportFailed", new { errorMessage = "Import was cancelled." }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord import {ImportId} failed", importId);
            import.Status = DiscordImportStatus.Failed;
            import.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            import.EncryptedBotToken = null;
            await db.SaveChangesAsync(CancellationToken.None);

            var group = _hub.Clients.Group($"server-{import.ServerId}");
            await group.SendAsync("ImportFailed", new { errorMessage = "Import failed. An administrator can check the import status for details." }, CancellationToken.None);
        }
    }

    private async Task<Dictionary<string, Guid>> ImportRolesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId, CancellationToken ct)
    {
        var discordRoles = await discord.GetGuildRolesAsync(guildId, ct);
        var roleMap = new Dictionary<string, Guid>();

        var everyoneRole = await db.ServerRoles
            .FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == "@everyone" && r.IsSystemRole, ct);

        foreach (var dr in discordRoles.OrderBy(r => r.Position))
        {
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dr.Id && m.EntityType == DiscordEntityType.Role, ct);
            if (existing is not null)
            {
                roleMap[dr.Id] = existing.CodecEntityId;
                continue;
            }

            if (dr.Name == "@everyone" && everyoneRole is not null)
            {
                everyoneRole.Permissions = DiscordPermissionMapper.MapPermissions(dr.Permissions);
                roleMap[dr.Id] = everyoneRole.Id;
                db.DiscordEntityMappings.Add(new DiscordEntityMapping
                {
                    Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                    DiscordEntityId = dr.Id, EntityType = DiscordEntityType.Role, CodecEntityId = everyoneRole.Id
                });
                continue;
            }

            if (dr.Managed) continue;

            var roleName = dr.Name;
            var nameExists = await db.ServerRoles.AnyAsync(r => r.ServerId == serverId && r.Name == roleName, ct);
            if (nameExists) roleName = $"{roleName} (imported)";

            var role = new ServerRoleEntity
            {
                Id = Guid.NewGuid(), ServerId = serverId, Name = roleName,
                Color = dr.Color != 0 ? $"#{dr.Color:X6}" : null,
                Position = dr.Position + 10,
                Permissions = DiscordPermissionMapper.MapPermissions(dr.Permissions),
                IsSystemRole = false, IsHoisted = dr.Hoist, IsMentionable = dr.Mentionable
            };
            db.ServerRoles.Add(role);
            roleMap[dr.Id] = role.Id;
            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                DiscordEntityId = dr.Id, EntityType = DiscordEntityType.Role, CodecEntityId = role.Id
            });
        }

        return roleMap;
    }

    private async Task<Dictionary<string, Guid>> ImportCategoriesAsync(
        CodecDbContext db, Guid serverId, Guid importId, List<DiscordChannel> allChannels, CancellationToken ct)
    {
        var categories = allChannels.Where(c => c.Type == 4).OrderBy(c => c.Position).ToList();
        var categoryMap = new Dictionary<string, Guid>();

        foreach (var dc in categories)
        {
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dc.Id && m.EntityType == DiscordEntityType.Category, ct);
            if (existing is not null)
            {
                categoryMap[dc.Id] = existing.CodecEntityId;
                continue;
            }

            var category = new ChannelCategory
            {
                Id = Guid.NewGuid(), ServerId = serverId,
                Name = dc.Name ?? "Unnamed", Position = dc.Position ?? 0
            };
            db.ChannelCategories.Add(category);
            categoryMap[dc.Id] = category.Id;
            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                DiscordEntityId = dc.Id, EntityType = DiscordEntityType.Category, CodecEntityId = category.Id
            });
        }

        return categoryMap;
    }

    private async Task<Dictionary<string, Guid>> ImportChannelsAsync(
        CodecDbContext db, Guid serverId, Guid importId, List<DiscordChannel> allChannels,
        Dictionary<string, Guid> categoryMap, CancellationToken ct)
    {
        var channels = allChannels.Where(c => c.Type is 0 or 2).OrderBy(c => c.Position).ToList();
        var channelMap = new Dictionary<string, Guid>();

        foreach (var dc in channels)
        {
            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dc.Id && m.EntityType == DiscordEntityType.Channel, ct);
            if (existing is not null)
            {
                channelMap[dc.Id] = existing.CodecEntityId;
                continue;
            }

            Guid? categoryId = null;
            if (dc.ParentId is not null && categoryMap.TryGetValue(dc.ParentId, out var catId))
                categoryId = catId;

            var channel = new Channel
            {
                Id = Guid.NewGuid(), ServerId = serverId, Name = dc.Name ?? "unnamed",
                Type = dc.Type == 2 ? ChannelType.Voice : ChannelType.Text,
                Position = dc.Position ?? 0, CategoryId = categoryId
            };
            db.Channels.Add(channel);
            channelMap[dc.Id] = channel.Id;
            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                DiscordEntityId = dc.Id, EntityType = DiscordEntityType.Channel, CodecEntityId = channel.Id
            });
        }

        return channelMap;
    }

    private async Task ImportChannelPermissionOverridesAsync(
        CodecDbContext db, List<DiscordChannel> allChannels,
        Dictionary<string, Guid> channelMap, Dictionary<string, Guid> roleMap, CancellationToken ct)
    {
        foreach (var dc in allChannels.Where(c => c.Type is 0 or 2))
        {
            if (dc.PermissionOverwrites is null || !channelMap.TryGetValue(dc.Id, out var codecChannelId))
                continue;

            foreach (var overwrite in dc.PermissionOverwrites)
            {
                if (overwrite.Type != 0) continue; // role overrides only
                if (!roleMap.TryGetValue(overwrite.Id, out var codecRoleId)) continue;

                var exists = await db.ChannelPermissionOverrides
                    .AnyAsync(o => o.ChannelId == codecChannelId && o.RoleId == codecRoleId, ct);
                if (exists) continue;

                var allow = long.TryParse(overwrite.Allow, out var a) ? a : 0;
                var deny = long.TryParse(overwrite.Deny, out var d) ? d : 0;

                db.ChannelPermissionOverrides.Add(new ChannelPermissionOverride
                {
                    Id = Guid.NewGuid(), ChannelId = codecChannelId, RoleId = codecRoleId,
                    Allow = DiscordPermissionMapper.MapPermissions(allow),
                    Deny = DiscordPermissionMapper.MapPermissions(deny)
                });
            }
        }
    }

    private async Task ImportEmojisAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, Guid importId, CancellationToken ct)
    {
        var emojis = await discord.GetGuildEmojisAsync(guildId, ct);
        var usedNames = new HashSet<string>(
            await db.CustomEmojis.Where(e => e.ServerId == serverId).Select(e => e.Name).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        foreach (var de in emojis)
        {
            if (de.Id is null || de.Name is null) continue;

            var existing = await db.DiscordEntityMappings
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == de.Id && m.EntityType == DiscordEntityType.Emoji, ct);
            if (existing is not null) continue;

            var emojiName = de.Name;
            var suffix = 0;
            while (usedNames.Contains(emojiName))
            {
                suffix++;
                emojiName = $"{de.Name}_{suffix}";
            }
            usedNames.Add(emojiName);

            var ext = de.Animated == true ? "gif" : "png";
            var emojiUrl = $"https://cdn.discordapp.com/emojis/{de.Id}.{ext}";
            var contentType = ext == "gif" ? "image/gif" : "image/png";

            var emoji = new CustomEmoji
            {
                Id = Guid.NewGuid(), ServerId = serverId, Name = emojiName,
                ImageUrl = emojiUrl, ContentType = contentType, IsAnimated = de.Animated == true
            };
            db.CustomEmojis.Add(emoji);
            db.DiscordEntityMappings.Add(new DiscordEntityMapping
            {
                Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                DiscordEntityId = de.Id, EntityType = DiscordEntityType.Emoji, CodecEntityId = emoji.Id
            });
        }
    }

    private async Task<int> ImportMembersAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, string guildId, CancellationToken ct)
    {
        var count = 0;
        string? after = null;

        while (true)
        {
            var members = await discord.GetGuildMembersAsync(guildId, 1000, after, ct);
            if (members.Count == 0) break;

            foreach (var dm in members)
            {
                if (dm.User is null) continue;

                var exists = await db.DiscordUserMappings
                    .AnyAsync(m => m.ServerId == serverId && m.DiscordUserId == dm.User.Id, ct);
                if (exists) continue;

                var displayName = dm.User.GlobalName ?? dm.User.Username;
                string? avatarUrl = dm.User.Avatar is not null
                    ? $"https://cdn.discordapp.com/avatars/{dm.User.Id}/{dm.User.Avatar}.png"
                    : null;

                db.DiscordUserMappings.Add(new DiscordUserMapping
                {
                    Id = Guid.NewGuid(), ServerId = serverId,
                    DiscordUserId = dm.User.Id, DiscordUsername = displayName, DiscordAvatarUrl = avatarUrl
                });
                count++;
            }

            await db.SaveChangesAsync(ct);
            after = members[^1].User?.Id;
            if (members.Count < 1000) break;
        }

        return count;
    }

    private async Task<int> ImportChannelMessagesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, Guid codecChannelId, string discordChannelId,
        Guid importId, IClientProxy group, CancellationToken ct)
    {
        var count = 0;
        // Start from snowflake "0" to get the oldest messages first.
        // Discord's `after` param returns messages newer than the given ID in ascending order.
        // Without `after`, Discord returns the newest messages (descending), which breaks pagination.
        string? after = "0";

        while (true)
        {
            List<DiscordMessage> messages;
            try
            {
                messages = await discord.GetChannelMessagesAsync(discordChannelId, 100, after, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Bot lacks access to channel {ChannelId}, skipping", discordChannelId);
                break;
            }

            if (messages.Count == 0) break;

            foreach (var dm in messages)
            {
                if (dm.Type is not (0 or 19)) continue;

                var existing = await db.DiscordEntityMappings
                    .AnyAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.Id && m.EntityType == DiscordEntityType.Message, ct);
                if (existing) continue;

                Guid? replyToMessageId = null;
                if (dm.MessageReference?.MessageId is not null)
                {
                    var replyMapping = await db.DiscordEntityMappings
                        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.MessageReference.MessageId && m.EntityType == DiscordEntityType.Message, ct);
                    replyToMessageId = replyMapping?.CodecEntityId;
                }

                // Use Discord CDN URLs directly — no re-hosting needed
                string? fileUrl = null, fileName = null, fileContentType = null, imageUrl = null;
                long? fileSize = null;
                if (dm.Attachments is { Count: > 0 })
                {
                    var att = dm.Attachments[0];
                    if (att.ContentType?.StartsWith("image/") == true)
                        imageUrl = att.Url;
                    else
                    {
                        fileUrl = att.Url;
                        fileName = att.Filename;
                        fileContentType = att.ContentType;
                        fileSize = att.Size;
                    }
                }

                var message = new Message
                {
                    Id = Guid.NewGuid(), ChannelId = codecChannelId, AuthorUserId = null,
                    AuthorName = dm.Author.GlobalName ?? dm.Author.Username,
                    ImportedAuthorName = dm.Author.GlobalName ?? dm.Author.Username,
                    ImportedDiscordUserId = dm.Author.Id,
                    ImportedAuthorAvatarUrl = dm.Author.Avatar is not null
                        ? $"https://cdn.discordapp.com/avatars/{dm.Author.Id}/{dm.Author.Avatar}.png" : null,
                    Body = dm.Content ?? string.Empty,
                    ImageUrl = imageUrl, FileUrl = fileUrl, FileName = fileName,
                    FileSize = fileSize, FileContentType = fileContentType,
                    ReplyToMessageId = replyToMessageId,
                    CreatedAt = DateTimeOffset.Parse(dm.Timestamp),
                    EditedAt = dm.EditedTimestamp is not null ? DateTimeOffset.Parse(dm.EditedTimestamp) : null
                };
                db.Messages.Add(message);
                db.DiscordEntityMappings.Add(new DiscordEntityMapping
                {
                    Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                    DiscordEntityId = dm.Id, EntityType = DiscordEntityType.Message, CodecEntityId = message.Id
                });
                count++;
            }

            await db.SaveChangesAsync(ct);

            if (count % 100 == 0)
            {
                await group.SendAsync("ImportProgress", new
                {
                    stage = "Messages", completed = count, total = 0, percentComplete = 0f
                }, ct);
            }

            after = messages[^1].Id;
            if (messages.Count < 100) break;
        }

        return count;
    }

    private async Task ImportPinnedMessagesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId,
        Dictionary<string, Guid> channelMap, Guid importId, CancellationToken ct)
    {
        foreach (var (discordChannelId, codecChannelId) in channelMap)
        {
            List<DiscordMessage> pins;
            try
            {
                pins = await discord.GetPinnedMessagesAsync(discordChannelId, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                continue;
            }

            foreach (var pin in pins)
            {
                var messageMapping = await db.DiscordEntityMappings
                    .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == pin.Id && m.EntityType == DiscordEntityType.Message, ct);
                if (messageMapping is null) continue;

                var alreadyPinned = await db.PinnedMessages
                    .AnyAsync(p => p.ChannelId == codecChannelId && p.MessageId == messageMapping.CodecEntityId, ct);
                if (alreadyPinned) continue;

                db.PinnedMessages.Add(new PinnedMessage
                {
                    Id = Guid.NewGuid(), MessageId = messageMapping.CodecEntityId,
                    ChannelId = codecChannelId, PinnedByUserId = null, PinnedAt = DateTimeOffset.UtcNow
                });
            }
        }
    }
}
