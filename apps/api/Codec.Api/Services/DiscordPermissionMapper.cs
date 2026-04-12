using Codec.Api.Models;

namespace Codec.Api.Services;

public static class DiscordPermissionMapper
{
    private static readonly (long DiscordBit, Permission CodecFlag)[] Mappings =
    [
        (1L << 10,  Permission.ViewChannels),
        (1L << 11,  Permission.SendMessages),
        (1L << 4,   Permission.ManageChannels),
        (1L << 5,   Permission.ManageServer),
        (1L << 28,  Permission.ManageRoles),
        (1L << 2,   Permission.KickMembers),
        (1L << 1,   Permission.BanMembers),
        (1L << 13,  Permission.ManageMessages),
        (1L << 15,  Permission.AttachFiles),
        (1L << 6,   Permission.AddReactions),
        (1L << 17,  Permission.MentionEveryone),
        (1L << 20,  Permission.Connect),
        (1L << 21,  Permission.Speak),
        (1L << 22,  Permission.MuteMembers),
        (1L << 23,  Permission.DeafenMembers),
        (1L << 3,   Permission.Administrator),
        (1L << 14,  Permission.EmbedLinks),
    ];

    public static Permission MapPermissions(long discordPermissions)
    {
        var result = Permission.None;
        foreach (var (discordBit, codecFlag) in Mappings)
        {
            if ((discordPermissions & discordBit) != 0)
                result |= codecFlag;
        }
        return result;
    }
}
