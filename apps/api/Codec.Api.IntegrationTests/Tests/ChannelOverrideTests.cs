using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using Codec.Api.Models;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for channel permission overrides: creating, reading, and deleting
/// per-role overrides that control access to individual channels.
/// </summary>
public class ChannelOverrideTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<Guid> GetMemberRoleIdAsync(HttpClient client, Guid serverId)
    {
        var rolesResponse = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        foreach (var role in rolesResponse.EnumerateArray())
        {
            if (role.GetProperty("name").GetString() == "Member" && role.GetProperty("isSystemRole").GetBoolean())
            {
                return role.GetProperty("id").GetGuid();
            }
        }
        throw new InvalidOperationException("Member role not found.");
    }

    [Fact]
    public async Task SetChannelOverride_CreatesOverride()
    {
        var owner = CreateClient("co-set-owner", "SetOverrideOwner");
        var (serverId, _) = await CreateServerAsync(owner, "SetOverride Server");
        var channelId = await CreateChannelAsync(owner, serverId, "override-channel");
        var memberRoleId = await GetMemberRoleIdAsync(owner, serverId);

        // Set a deny override for SendMessages on the Member role
        long denyPerms = (long)Permission.SendMessages;
        var setResponse = await owner.PutAsJsonAsync($"/channels/{channelId}/overrides/{memberRoleId}",
            new { allow = 0L, deny = denyPerms });
        setResponse.EnsureSuccessStatusCode();

        // Verify the override is returned by GET overrides
        var overridesResponse = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/overrides");
        Assert.True(overridesResponse.ValueKind == JsonValueKind.Array,
            "Expected overrides to be an array.");

        var found = false;
        foreach (var o in overridesResponse.EnumerateArray())
        {
            if (o.GetProperty("roleId").GetGuid() == memberRoleId)
            {
                Assert.Equal(0L, o.GetProperty("allow").GetInt64());
                Assert.Equal(denyPerms, o.GetProperty("deny").GetInt64());
                found = true;
                break;
            }
        }

        Assert.True(found, "Override for Member role was not found in GET overrides response.");
    }

    [Fact]
    public async Task DeleteChannelOverride_RemovesOverride()
    {
        var owner = CreateClient("co-del-owner", "DeleteOverrideOwner");
        var (serverId, _) = await CreateServerAsync(owner, "DeleteOverride Server");
        var channelId = await CreateChannelAsync(owner, serverId, "delete-override-channel");
        var memberRoleId = await GetMemberRoleIdAsync(owner, serverId);

        // Create an override first
        var setResponse = await owner.PutAsJsonAsync($"/channels/{channelId}/overrides/{memberRoleId}",
            new { allow = 0L, deny = (long)Permission.SendMessages });
        setResponse.EnsureSuccessStatusCode();

        // Delete the override
        var deleteResponse = await owner.DeleteAsync($"/channels/{channelId}/overrides/{memberRoleId}");
        deleteResponse.EnsureSuccessStatusCode();

        // Verify GET overrides returns empty (no overrides for this channel)
        var overridesResponse = await owner.GetFromJsonAsync<JsonElement>($"/channels/{channelId}/overrides");
        Assert.True(overridesResponse.ValueKind == JsonValueKind.Array,
            "Expected overrides to be an array.");

        var overrideCount = overridesResponse.GetArrayLength();
        Assert.Equal(0, overrideCount);
    }
}
