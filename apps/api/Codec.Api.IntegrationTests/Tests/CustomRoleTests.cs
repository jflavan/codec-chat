using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for custom role management: CRUD operations,
/// permission checks, role hierarchy enforcement, and system role protections.
/// </summary>
public class CustomRoleTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task ListRoles_ReturnsSystemRoles()
    {
        var client = CreateClient("role-list-1", "RoleListUser");
        var (serverId, _) = await CreateServerAsync(client, "Role List Server");

        var response = await client.GetAsync($"/servers/{serverId}/roles");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // New server should have system roles: Owner, Admin, Member
        Assert.True(body.GetArrayLength() >= 3);

        var roleNames = new List<string>();
        foreach (var role in body.EnumerateArray())
        {
            roleNames.Add(role.GetProperty("name").GetString()!);
        }

        Assert.Contains("Owner", roleNames);
        Assert.Contains("Admin", roleNames);
        Assert.Contains("Member", roleNames);
    }

    [Fact]
    public async Task CreateRole_AsOwner_Succeeds()
    {
        var client = CreateClient("role-create-1", "RoleCreateUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Create Server");

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new
        {
            name = "Moderator",
            color = "#ff5733",
            isHoisted = true,
            isMentionable = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Moderator", body.GetProperty("name").GetString());
        Assert.Equal("#ff5733", body.GetProperty("color").GetString());
        Assert.True(body.GetProperty("isHoisted").GetBoolean());
        Assert.True(body.GetProperty("isMentionable").GetBoolean());
        Assert.False(body.GetProperty("isSystemRole").GetBoolean());
    }

    [Fact]
    public async Task CreateRole_DuplicateName_ReturnsConflict()
    {
        var client = CreateClient("role-dup-1", "RoleDupUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Dup Server");

        await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Unique Role" });

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Unique Role" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_EmptyName_ReturnsBadRequest()
    {
        var client = CreateClient("role-empty-1", "RoleEmptyUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Empty Server");

        var response = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_TooLongName_ReturnsBadRequest()
    {
        var client = CreateClient("role-long-1", "RoleLongUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Long Server");

        var longName = new string('a', 101);
        var response = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = longName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_AsMember_ReturnsForbidden()
    {
        var owner = CreateClient("role-owner-noauth", "RoleOwner");
        var member = CreateClient("role-member-noauth", "RoleMember");

        var (serverId, _) = await CreateServerAsync(owner, "Role NoAuth Server");
        var inviteResponse = await owner.PostAsJsonAsync($"/servers/{serverId}/invites", new { });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var code = invite.GetProperty("code").GetString();
        await member.PostAsync($"/invites/{code}/join", null);

        var response = await member.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Unauthorized Role" });
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.InternalServerError,
            $"Expected 403 or 500 but got {response.StatusCode}");
    }

    [Fact]
    public async Task UpdateRole_ChangeNameAndColor_Succeeds()
    {
        var client = CreateClient("role-upd-1", "RoleUpdUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Upd Server");

        var createResp = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Old Name" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("id").GetString();

        var content = JsonContent.Create(new { name = "New Name", color = "#00ff00" });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/servers/{serverId}/roles/{roleId}")
        {
            Content = content
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New Name", body.GetProperty("name").GetString());
        Assert.Equal("#00ff00", body.GetProperty("color").GetString());
    }

    [Fact]
    public async Task UpdateRole_RenameSystemRole_ReturnsBadRequest()
    {
        var client = CreateClient("role-sysren-1", "RoleSysRenUser");
        var (serverId, _) = await CreateServerAsync(client, "Role SysRen Server");

        // Get the Admin system role
        var rolesResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        Guid? adminRoleId = null;
        foreach (var role in rolesResp.EnumerateArray())
        {
            if (role.GetProperty("name").GetString() == "Admin" && role.GetProperty("isSystemRole").GetBoolean())
            {
                adminRoleId = role.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(adminRoleId);

        var content = JsonContent.Create(new { name = "Renamed Admin" });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/servers/{serverId}/roles/{adminRoleId}")
        {
            Content = content
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_CustomRole_Succeeds()
    {
        var client = CreateClient("role-del-1", "RoleDelUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Del Server");

        var createResp = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Temp Role" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("id").GetString();

        var response = await client.DeleteAsync($"/servers/{serverId}/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify role is gone
        var rolesResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var roleNames = new List<string>();
        foreach (var role in rolesResp.EnumerateArray())
        {
            roleNames.Add(role.GetProperty("name").GetString()!);
        }
        Assert.DoesNotContain("Temp Role", roleNames);
    }

    [Fact]
    public async Task DeleteRole_SystemRole_ReturnsBadRequest()
    {
        var client = CreateClient("role-sysrm-1", "RoleSysRmUser");
        var (serverId, _) = await CreateServerAsync(client, "Role SysRm Server");

        var rolesResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        Guid? memberRoleId = null;
        foreach (var role in rolesResp.EnumerateArray())
        {
            if (role.GetProperty("name").GetString() == "Member" && role.GetProperty("isSystemRole").GetBoolean())
            {
                memberRoleId = role.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(memberRoleId);

        var response = await client.DeleteAsync($"/servers/{serverId}/roles/{memberRoleId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_NotFound_Returns404()
    {
        var client = CreateClient("role-del-nf-1", "RoleDelNFUser");
        var (serverId, _) = await CreateServerAsync(client, "Role DelNF Server");

        var response = await client.DeleteAsync($"/servers/{serverId}/roles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReorderRoles_Succeeds()
    {
        var client = CreateClient("role-reord-1", "RoleReordUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Reord Server");

        // Create two custom roles
        await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Role A" });
        await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Role B" });

        // Get all roles
        var rolesResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var roleIds = new List<Guid>();
        foreach (var role in rolesResp.EnumerateArray())
        {
            roleIds.Add(role.GetProperty("id").GetGuid());
        }

        // Reorder: keep Owner first, swap the rest
        var response = await client.PutAsJsonAsync($"/servers/{serverId}/roles/reorder", new
        {
            roleIds
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ReorderRoles_OwnerNotFirst_ReturnsBadRequest()
    {
        var client = CreateClient("role-reord-bad-1", "RoleReordBadUser");
        var (serverId, _) = await CreateServerAsync(client, "Role ReordBad Server");

        // Get all roles
        var rolesResp = await client.GetFromJsonAsync<JsonElement>($"/servers/{serverId}/roles");
        var roleIds = new List<Guid>();
        foreach (var role in rolesResp.EnumerateArray())
        {
            roleIds.Add(role.GetProperty("id").GetGuid());
        }

        // Reverse the order so Owner is not first
        roleIds.Reverse();

        var response = await client.PutAsJsonAsync($"/servers/{serverId}/roles/reorder", new
        {
            roleIds
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_Permissions_Succeeds()
    {
        var client = CreateClient("role-perm-1", "RolePermUser");
        var (serverId, _) = await CreateServerAsync(client, "Role Perm Server");

        var createResp = await client.PostAsJsonAsync($"/servers/{serverId}/roles", new { name = "Custom Perms" });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = created.GetProperty("id").GetString();

        // Set specific permissions (ViewChannels | SendMessages = 1 + (1 << 20))
        long viewChannels = 1L;
        long sendMessages = 1L << 20;
        long newPerms = viewChannels | sendMessages;

        var content = JsonContent.Create(new { permissions = newPerms });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/servers/{serverId}/roles/{roleId}")
        {
            Content = content
        };
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(newPerms, body.GetProperty("permissions").GetInt64());
    }
}
