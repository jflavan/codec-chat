using Codec.Api.Models;
using Codec.Api.Services;

namespace Codec.Api.Tests;

public class DiscordPermissionMapperTests
{
    [Fact]
    public void MapPermissions_ViewChannel_MapsToViewChannels()
    {
        long discordPerms = 1L << 10;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ViewChannels));
    }

    [Fact]
    public void MapPermissions_SendMessages_MapsToSendMessages()
    {
        long discordPerms = 1L << 11;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.SendMessages));
    }

    [Fact]
    public void MapPermissions_Administrator_MapsToAdministrator()
    {
        long discordPerms = 1L << 3;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.Administrator));
    }

    [Fact]
    public void MapPermissions_MultiplePermissions_CombinesCorrectly()
    {
        long discordPerms = (1L << 10) | (1L << 11) | (1L << 15);
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ViewChannels));
        Assert.True(result.HasFlag(Permission.SendMessages));
        Assert.True(result.HasFlag(Permission.AttachFiles));
    }

    [Fact]
    public void MapPermissions_UnknownBits_DroppedSilently()
    {
        long discordPerms = 1L << 31;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.Equal(Permission.None, result);
    }

    [Fact]
    public void MapPermissions_Zero_ReturnsNone()
    {
        var result = DiscordPermissionMapper.MapPermissions(0);
        Assert.Equal(Permission.None, result);
    }

    [Fact]
    public void MapPermissions_ManageGuild_MapsToManageServer()
    {
        long discordPerms = 1L << 5;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ManageServer));
    }

    [Fact]
    public void MapPermissions_ManageRoles_MapsToManageRoles()
    {
        long discordPerms = 1L << 28;
        var result = DiscordPermissionMapper.MapPermissions(discordPerms);
        Assert.True(result.HasFlag(Permission.ManageRoles));
    }
}
