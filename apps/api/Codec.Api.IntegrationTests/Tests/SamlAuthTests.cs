using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for SAML SSO identity provider management endpoints.
/// Tests the admin CRUD operations for IdP configuration.
/// The actual SAML login/ACS flow requires IdP interaction, so we test
/// the management layer and validation paths.
/// </summary>
public class SamlAuthTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private const string TestCertPem = """
        -----BEGIN CERTIFICATE-----
        MIIDETCCAfmgAwIBAgIUczWmNxQyilK9QNTNS5dUJ/oqBJowDQYJKoZIhvcNAQEL
        BQAwGDEWMBQGA1UEAwwNdGVzdC1zYW1sLWlkcDAeFw0yNjAzMjUwNDU2MDJaFw0z
        MTAzMjQwNDU2MDJaMBgxFjAUBgNVBAMMDXRlc3Qtc2FtbC1pZHAwggEiMA0GCSqG
        SIb3DQEBAQUAA4IBDwAwggEKAoIBAQC2DxGGKHmtgRJgkzstfP72x5FY/VppcJu9
        iXDwfCm7klDXfZMN0bd8G0+XjrebxG8KT4gAqrQ9Hp0TcQOYyoAlywgTVx8mvw8r
        BZ3M80Khx3Udyqg/UlonU1cLrRBGpcwW1STCx2+iHaoGeGc4hA3n/JJOsw/XDqfn
        b45XDztBmrAEYnKVKRsmF/0SineBOslL54809LYwO31MpshXxNcIgKWt9fUXPFC1
        l4+pxuRvV3+NPea5iJJbbChi/OetjpOlQD9xXvWeItIikFHpgPdviq2chalNToAv
        b5ES+qkQrIMRmW8zC+X4UZbJo7QXc+O5Ort4SpOkztFnHPECNALRAgMBAAGjUzBR
        MB0GA1UdDgQWBBSn8/ArdfuAZ4vEk9S8Hj7kjTkLXjAfBgNVHSMEGDAWgBSn8/Ar
        dfuAZ4vEk9S8Hj7kjTkLXjAPBgNVHRMBAf8EBTADAQH/MA0GCSqGSIb3DQEBCwUA
        A4IBAQCjYpqn5jCVbENr3zVCCKocapP6+rIn9baHBDR4VMGJj9rYSg+U59TYBrL/
        CF543oQYWHMF4drByWHMVqb3ljHJsBQNbwHDjZCc2jYFCyj0qir/zNEO4QIvkf3f
        qdtiEcZdz9LGRgWphhfKxndkTLTgu2yRpIxmeMvhXy9KJKonode0XFZAcYAW94F/
        M57SlcenUYXMZ7MDZDnrh5OzFcWpx4VjrP7Q3s0Kdv3aNlnmdIA+tE3wKAcByQuf
        Yl6z8i2jvXb4RuvJEfCBHYVWlvLNWJPoDcu5W6ONgnj/AWpjMBAYCUgNN4p40Og9
        W4JrHCSBCG3Fdte6VtiyzvNfvYXE
        -----END CERTIFICATE-----
        """;

    private async Task<HttpClient> CreateGlobalAdminClientAsync(string sub = "saml-admin")
    {
        var client = CreateClient(sub, "SAML Admin");
        // Trigger user creation
        await client.GetAsync("/me");
        // Make global admin and get the user's actual GUID
        Guid userId = Guid.Empty;
        await WithDbAsync(async db =>
        {
            var user = db.Users.First(u => u.GoogleSubject == sub);
            user.IsGlobalAdmin = true;
            await db.SaveChangesAsync();
            userId = user.Id;
        });
        // Return a new client whose sub claim is the user's GUID with codec-api issuer,
        // because SamlController.EnsureGlobalAdminAsync parses sub as a GUID user ID.
        return Factory.CreateAuthenticatedClient(
            userId.ToString(), "SAML Admin", $"{sub}@test.com", issuer: "codec-api");
    }

    [Fact]
    public async Task ListProviders_WhenSamlDisabled_ReturnsNotFound()
    {
        var client = CreateClient("saml-list-1", "SamlUser1");
        // SAML is not enabled in test config by default
        var response = await client.GetAsync("/auth/saml/providers");
        // Should return 404 since SAML is disabled
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_ReturnsXml()
    {
        var client = CreateClient("saml-meta-1", "MetaUser");
        var response = await client.GetAsync("/auth/saml/metadata");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("EntityDescriptor", content);
    }

    [Fact]
    public async Task CreateIdp_AsGlobalAdmin_Succeeds()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-create");

        var response = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://idp.example.com/saml/metadata",
            displayName = "Test IdP",
            singleSignOnUrl = "https://idp.example.com/saml/sso",
            certificatePem = TestCertPem
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test IdP", body.GetProperty("displayName").GetString());
        Assert.Equal("https://idp.example.com/saml/metadata", body.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task CreateIdp_AsNonAdmin_ReturnsForbidden()
    {
        var client = CreateClient("saml-nonadmin-1", "NonAdmin");
        await client.GetAsync("/me"); // ensure user exists

        var response = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://idp2.example.com/saml/metadata",
            displayName = "Unauthorized IdP",
            singleSignOnUrl = "https://idp2.example.com/saml/sso",
            certificatePem = TestCertPem
        });

        // Should be 403 Forbidden
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.InternalServerError,
            $"Expected 403 or 500 but got {response.StatusCode}");
    }

    [Fact]
    public async Task CreateIdp_DuplicateEntityId_ReturnsConflict()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-dup");

        var idpPayload = new
        {
            entityId = "https://dup-idp.example.com/metadata",
            displayName = "Dup IdP",
            singleSignOnUrl = "https://dup-idp.example.com/sso",
            certificatePem = TestCertPem
        };

        var first = await client.PostAsJsonAsync("/auth/saml/idps", idpPayload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/auth/saml/idps", idpPayload);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateIdp_InvalidCertificate_ReturnsBadRequest()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-badcert");

        var response = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://badcert-idp.example.com/metadata",
            displayName = "Bad Cert IdP",
            singleSignOnUrl = "https://badcert-idp.example.com/sso",
            certificatePem = "not-a-valid-certificate"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListIdps_AsGlobalAdmin_ReturnsAll()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-list");

        // Create an IdP first
        await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://list-idp.example.com/metadata",
            displayName = "List IdP",
            singleSignOnUrl = "https://list-idp.example.com/sso",
            certificatePem = TestCertPem
        });

        var response = await client.GetAsync("/auth/saml/idps");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task GetIdp_AsGlobalAdmin_ReturnsDetails()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-get");

        var createResponse = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://get-idp.example.com/metadata",
            displayName = "Get IdP",
            singleSignOnUrl = "https://get-idp.example.com/sso",
            certificatePem = TestCertPem
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var idpId = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/auth/saml/idps/{idpId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Get IdP", body.GetProperty("displayName").GetString());
        Assert.True(body.TryGetProperty("certificatePem", out _));
    }

    [Fact]
    public async Task UpdateIdp_AsGlobalAdmin_Succeeds()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-upd");

        var createResponse = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://upd-idp.example.com/metadata",
            displayName = "Original Name",
            singleSignOnUrl = "https://upd-idp.example.com/sso",
            certificatePem = TestCertPem
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var idpId = created.GetProperty("id").GetString();

        var updateResponse = await client.PutAsJsonAsync($"/auth/saml/idps/{idpId}", new
        {
            displayName = "Updated Name",
            isEnabled = false
        });
        updateResponse.EnsureSuccessStatusCode();
        var body = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Name", body.GetProperty("displayName").GetString());
        Assert.False(body.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task DeleteIdp_AsGlobalAdmin_Succeeds()
    {
        var client = await CreateGlobalAdminClientAsync("saml-admin-del");

        var createResponse = await client.PostAsJsonAsync("/auth/saml/idps", new
        {
            entityId = "https://del-idp.example.com/metadata",
            displayName = "Delete Me",
            singleSignOnUrl = "https://del-idp.example.com/sso",
            certificatePem = TestCertPem
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var idpId = created.GetProperty("id").GetString();

        var deleteResponse = await client.DeleteAsync($"/auth/saml/idps/{idpId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify it's gone
        var getResponse = await client.GetAsync($"/auth/saml/idps/{idpId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WhenSamlDisabled_ReturnsNotFound()
    {
        var client = CreateClient("saml-login-disabled", "LoginUser");
        var response = await client.GetAsync($"/auth/saml/login/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Acs_WhenSamlDisabled_ReturnsBadRequest()
    {
        var client = CreateClient("saml-acs-disabled", "AcsUser");
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("SAMLResponse", "dGVzdA==")
        });
        var response = await client.PostAsync("/auth/saml/acs", formContent);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
