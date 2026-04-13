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

        // Atomically transition Pending → InProgress to avoid race with CancelImport
        var updated = await db.DiscordImports
            .Where(i => i.Id == importId && i.Status == DiscordImportStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, DiscordImportStatus.InProgress)
                .SetProperty(i => i.StartedAt, DateTimeOffset.UtcNow), ct);

        if (updated == 0) return;

        var import = await db.DiscordImports.FindAsync([importId], ct);
        if (import is null) return;

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

                    int localTotal, localCompleted;
                    lock (messageLock)
                    {
                        totalMessages += count;
                        completedChannels++;
                        localTotal = totalMessages;
                        localCompleted = completedChannels;
                    }

                    await group.SendAsync("ImportProgress", new
                    {
                        stage = $"Messages ({localCompleted}/{textChannelIds.Count})",
                        completed = localTotal,
                        total = 0,
                        percentComplete = 40f + ((float)localCompleted / textChannelIds.Count * 50f)
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
            await group.SendAsync("ImportProgress", new { stage = "Pins", completed = 0, total = 0, percentComplete = 90f }, ct);
            await ImportPinnedMessagesAsync(db, discordClient, serverId, channelMap, importId, ct);
            await db.SaveChangesAsync(ct);

            // 9. Backfill reply-to references (needed because newest-first import means
            //    the replied-to message may not exist when the reply is first imported)
            await group.SendAsync("ImportProgress", new { stage = "Resolving replies", completed = 0, total = 0, percentComplete = 95f }, ct);
            await BackfillReplyReferencesAsync(db, serverId, importId, ct);
            await db.SaveChangesAsync(ct);

            // Milestone: text import complete — fire ImportCompleted so UI shows success
            await group.SendAsync("ImportCompleted", new
            {
                importedChannels = import.ImportedChannels,
                importedMessages = import.ImportedMessages,
                importedMembers = import.ImportedMembers
            }, ct);

            // Transition to media re-hosting phase
            import.Status = DiscordImportStatus.RehostingMedia;
            await db.SaveChangesAsync(ct);

            try
            {
                // 10. Re-host emoji images
                await group.SendAsync("ImportProgress", new { stage = "Re-hosting emojis", completed = 0, total = 0, percentComplete = 96f }, ct);
                await RehostEmojisAsync(db, serverId, importId, group, ct);
                await db.SaveChangesAsync(ct);

                // 11. Re-host message image attachments (newest first)
                await group.SendAsync("ImportProgress", new { stage = "Re-hosting images", completed = 0, total = 0, percentComplete = 97f }, ct);
                await RehostAttachmentsAsync(db, serverId, group, ct);

                // 12. Re-host non-image file attachments (newest first)
                await group.SendAsync("ImportProgress", new { stage = "Re-hosting files", completed = 0, total = 0, percentComplete = 98.5f }, ct);
                await RehostFileAttachmentsAsync(db, serverId, group, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Let the outer catch handle cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media re-hosting failed for import {ImportId}, completing import without full re-hosting", importId);
            }

            // Check for cancellation one final time before committing success
            ct.ThrowIfCancellationRequested();

            // Complete — the text import succeeded even if re-hosting partially failed
            import.Status = DiscordImportStatus.Completed;
            import.CompletedAt = DateTimeOffset.UtcNow;
            import.LastSyncedAt = DateTimeOffset.UtcNow;
            import.EncryptedBotToken = null;
            await db.SaveChangesAsync(ct);

            // Notify clients that re-hosting is done and import is fully complete
            await group.SendAsync("ImportRehostCompleted", new
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
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist cancellation status for import {ImportId}", importId);
            }

            var group = _hub.Clients.Group($"server-{import.ServerId}");
            await group.SendAsync("ImportFailed", new { errorMessage = "Import was cancelled." }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord import {ImportId} failed", importId);
            import.Status = DiscordImportStatus.Failed;
            import.ErrorMessage = "Import failed unexpectedly. Check server logs for details.";
            import.EncryptedBotToken = null;
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist failure status for import {ImportId}", importId);
            }

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
            var lastUserId = members.LastOrDefault(m => m.User is not null)?.User?.Id;
            if (lastUserId is null) break;
            after = lastUserId;
            if (members.Count < 1000) break;
        }

        return count;
    }

    private async Task<int> ImportChannelMessagesAsync(
        CodecDbContext db, DiscordApiClient discord, Guid serverId, Guid codecChannelId, string discordChannelId,
        Guid importId, IClientProxy serverGroup, CancellationToken ct)
    {
        var count = 0;

        // Check if we already have imported messages for this channel (resync / retry).
        // If so, use `after` pagination to fetch only newer messages from Discord.
        // Discord snowflake IDs are monotonically increasing, so MAX(DiscordEntityId) = newest.
        var newestImportedDiscordId = await db.DiscordEntityMappings
            .Where(m => m.ServerId == serverId && m.EntityType == DiscordEntityType.Message)
            .Join(db.Messages.Where(msg => msg.ChannelId == codecChannelId),
                mapping => mapping.CodecEntityId,
                message => message.Id,
                (mapping, _) => mapping.DiscordEntityId)
            .MaxAsync(id => (string?)id, ct);

        // When resuming: use `after` to fetch only messages newer than the last imported one.
        // For initial import: use `before` pagination (newest-first) so users see recent messages first.
        var useAfterPagination = newestImportedDiscordId is not null;
        string? before = null;
        string? after = newestImportedDiscordId;

        var channelGroup = _hub.Clients.Group($"channel-{codecChannelId}");

        while (true)
        {
            List<DiscordMessage> messages;
            try
            {
                messages = await discord.GetChannelMessagesAsync(discordChannelId, 100,
                    before: useAfterPagination ? null : before,
                    after: useAfterPagination ? after : null,
                    ct: ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Bot lacks access to channel {ChannelId}, skipping", discordChannelId);
                break;
            }

            if (messages.Count == 0) break;

            var batchCount = 0;
            foreach (var dm in messages)
            {
                if (dm.Type is not (0 or 19)) continue;

                var existing = await db.DiscordEntityMappings
                    .AnyAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.Id && m.EntityType == DiscordEntityType.Message, ct);
                if (existing) continue;

                Guid? replyToMessageId = null;
                string? unresolvedReplyDiscordId = null;
                if (dm.MessageReference?.MessageId is not null)
                {
                    var replyMapping = await db.DiscordEntityMappings
                        .FirstOrDefaultAsync(m => m.ServerId == serverId && m.DiscordEntityId == dm.MessageReference.MessageId && m.EntityType == DiscordEntityType.Message, ct);
                    replyToMessageId = replyMapping?.CodecEntityId;
                    if (replyToMessageId is null)
                        unresolvedReplyDiscordId = dm.MessageReference.MessageId;
                }

                // Use Discord CDN URLs directly — no re-hosting needed
                string? fileUrl = null, fileName = null, fileContentType = null, imageUrl = null;
                long? fileSize = null;
                if (dm.Attachments is { Count: > 0 })
                {
                    var att = dm.Attachments[0];
                    var ext = Path.GetExtension(att.Filename)?.ToLowerInvariant();
                    var isImage = att.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                        || ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".ico" or ".avif";
                    if (isImage)
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
                if (unresolvedReplyDiscordId is not null)
                {
                    db.DiscordEntityMappings.Add(new DiscordEntityMapping
                    {
                        Id = Guid.NewGuid(), DiscordImportId = importId, ServerId = serverId,
                        DiscordEntityId = unresolvedReplyDiscordId, EntityType = DiscordEntityType.PendingReply,
                        CodecEntityId = message.Id
                    });
                }
                count++;
                batchCount++;
            }

            await db.SaveChangesAsync(ct);

            // Notify channel subscribers that new messages are available
            if (batchCount > 0)
            {
                await channelGroup.SendAsync("ImportMessagesAvailable", new
                {
                    channelId = codecChannelId,
                    count = batchCount
                }, ct);
            }

            if (count % 500 == 0)
            {
                await serverGroup.SendAsync("ImportProgress", new
                {
                    stage = "Messages", completed = count, total = 0, percentComplete = 0f
                }, ct);
            }

            // Advance pagination cursor.
            // `after` mode: Discord returns ascending (oldest first), use last (newest) as next cursor.
            // `before` mode: Discord returns descending (newest first), use last (oldest) as next cursor.
            if (useAfterPagination)
                after = messages[^1].Id;
            else
                before = messages[^1].Id;
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

    private async Task BackfillReplyReferencesAsync(
        CodecDbContext db, Guid serverId, Guid importId, CancellationToken ct)
    {
        // PendingReply mappings: DiscordEntityId = Discord reply-to message ID,
        // CodecEntityId = Codec message that needs its ReplyToMessageId set.
        var pendingReplies = await db.DiscordEntityMappings
            .Where(m => m.DiscordImportId == importId && m.EntityType == DiscordEntityType.PendingReply)
            .ToListAsync(ct);

        if (pendingReplies.Count == 0) return;

        // Batch-load all message mappings for this import to avoid N+1 queries
        var messageMappings = await db.DiscordEntityMappings
            .Where(m => m.ServerId == serverId && m.EntityType == DiscordEntityType.Message)
            .ToDictionaryAsync(m => m.DiscordEntityId, m => m.CodecEntityId, ct);

        var resolvedCount = 0;
        foreach (var pending in pendingReplies)
        {
            if (messageMappings.TryGetValue(pending.DiscordEntityId, out var replyToCodecId))
            {
                var message = await db.Messages.FindAsync([pending.CodecEntityId], ct);
                if (message is not null && message.ReplyToMessageId is null)
                {
                    message.ReplyToMessageId = replyToCodecId;
                    resolvedCount++;
                }
            }

            db.DiscordEntityMappings.Remove(pending);
        }

        _logger.LogInformation("Backfilled {Resolved}/{Total} reply references for import {ImportId}",
            resolvedCount, pendingReplies.Count, importId);
    }

    internal async Task RehostEmojisAsync(
        CodecDbContext db, Guid serverId, Guid importId, IClientProxy group, CancellationToken ct)
    {
        var emojiMappings = await db.DiscordEntityMappings
            .Where(m => m.DiscordImportId == importId && m.EntityType == DiscordEntityType.Emoji)
            .Select(m => m.CodecEntityId)
            .ToListAsync(ct);

        var emojis = await db.CustomEmojis
            .Where(e => emojiMappings.Contains(e.Id) && e.ImageUrl.Contains("cdn.discordapp.com"))
            .ToListAsync(ct);

        if (emojis.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var rehostService = scope.ServiceProvider.GetRequiredService<DiscordMediaRehostService>();

        var completed = 0;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;

        foreach (var emoji in emojis)
        {
            ct.ThrowIfCancellationRequested();

            var result = await rehostService.RehostImageAsync(
                emoji.ImageUrl, "emojis", 512 * 1024, null, ct);

            switch (result.Outcome)
            {
                case RehostOutcome.Success:
                    emoji.ImageUrl = result.Url!;
                    consecutiveFailures = 0;
                    break;
                case RehostOutcome.Failed:
                    consecutiveFailures++;
                    _logger.LogWarning("Failed to re-host emoji {EmojiId} from {Url}", emoji.Id, emoji.ImageUrl);
                    if (consecutiveFailures >= maxConsecutiveFailures)
                        throw new InvalidOperationException("Too many consecutive emoji re-host failures. Aborting media re-hosting.");
                    break;
                case RehostOutcome.Skipped:
                    break;
            }

            completed++;
            if (completed % 10 == 0)
            {
                await db.SaveChangesAsync(ct);
                await group.SendAsync("ImportProgress", new
                {
                    stage = "Re-hosting emojis",
                    completed,
                    total = emojis.Count,
                    percentComplete = 96f + ((float)completed / emojis.Count * 1f)
                }, ct);
            }
        }

        // Save any remaining changes from the final partial batch
        await db.SaveChangesAsync(ct);
    }

    internal async Task RehostAttachmentsAsync(
        CodecDbContext db, Guid serverId, IClientProxy group, CancellationToken ct)
    {
        var channelIds = await db.Channels
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var totalCount = await db.Messages
            .Where(m => channelIds.Contains(m.ChannelId) && m.ImageUrl != null && m.ImageUrl.Contains("cdn.discordapp.com"))
            .CountAsync(ct);

        if (totalCount == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var rehostService = scope.ServiceProvider.GetRequiredService<DiscordMediaRehostService>();

        var completed = 0;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;
        const int batchSize = 50;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var messages = await db.Messages
                .Where(m => channelIds.Contains(m.ChannelId) && m.ImageUrl != null && m.ImageUrl.Contains("cdn.discordapp.com"))
                .OrderByDescending(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);

            if (messages.Count == 0) break;

            foreach (var message in messages)
            {
                ct.ThrowIfCancellationRequested();

                var result = await rehostService.RehostImageAsync(
                    message.ImageUrl!, "images", 10 * 1024 * 1024, 4096, ct);

                switch (result.Outcome)
                {
                    case RehostOutcome.Success:
                        message.ImageUrl = result.Url!;
                        consecutiveFailures = 0;
                        break;
                    case RehostOutcome.Failed:
                        message.ImageUrl = null;
                        consecutiveFailures++;
                        _logger.LogWarning("Failed to re-host image for message {MessageId}, clearing ImageUrl", message.Id);
                        if (consecutiveFailures >= maxConsecutiveFailures)
                            throw new InvalidOperationException("Too many consecutive attachment re-host failures. Aborting media re-hosting.");
                        break;
                    case RehostOutcome.Skipped:
                        message.ImageUrl = null;
                        break;
                }

                completed++;
            }

            await db.SaveChangesAsync(ct);

            await group.SendAsync("ImportProgress", new
            {
                stage = $"Re-hosting images ({completed}/{totalCount})",
                completed,
                total = totalCount,
                percentComplete = 97f + ((float)completed / totalCount * 1.5f)
            }, ct);
        }
    }

    internal async Task RehostFileAttachmentsAsync(
        CodecDbContext db, Guid serverId, IClientProxy group, CancellationToken ct)
    {
        var channelIds = await db.Channels
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var totalCount = await db.Messages
            .Where(m => channelIds.Contains(m.ChannelId) && m.FileUrl != null && m.FileUrl.Contains("cdn.discordapp.com"))
            .CountAsync(ct);

        if (totalCount == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var rehostService = scope.ServiceProvider.GetRequiredService<DiscordMediaRehostService>();

        var completed = 0;
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 10;
        const int batchSize = 50;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var messages = await db.Messages
                .Where(m => channelIds.Contains(m.ChannelId) && m.FileUrl != null && m.FileUrl.Contains("cdn.discordapp.com"))
                .OrderByDescending(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);

            if (messages.Count == 0) break;

            foreach (var message in messages)
            {
                ct.ThrowIfCancellationRequested();

                var result = await rehostService.RehostFileAsync(
                    message.FileUrl!, "files", 50 * 1024 * 1024, ct);

                switch (result.Outcome)
                {
                    case RehostOutcome.Success:
                        message.FileUrl = result.Url!;
                        consecutiveFailures = 0;
                        break;
                    case RehostOutcome.Failed:
                        message.FileUrl = null;
                        consecutiveFailures++;
                        _logger.LogWarning("Failed to re-host file for message {MessageId}, clearing FileUrl", message.Id);
                        if (consecutiveFailures >= maxConsecutiveFailures)
                            throw new InvalidOperationException("Too many consecutive file re-host failures. Aborting media re-hosting.");
                        break;
                    case RehostOutcome.Skipped:
                        message.FileUrl = null;
                        break;
                }

                completed++;
            }

            await db.SaveChangesAsync(ct);

            await group.SendAsync("ImportProgress", new
            {
                stage = $"Re-hosting files ({completed}/{totalCount})",
                completed,
                total = totalCount,
                percentComplete = 98.5f + ((float)completed / totalCount * 1.5f)
            }, ct);
        }
    }
}
