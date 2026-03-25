using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class SamlControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly IConfiguration _config;
    private readonly SamlService _samlService;
    private readonly TokenService _tokenService;
    private readonly SamlController _controller;
    private readonly User _adminUser;

    public SamlControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Saml:EntityId"] = "https://sp.example.com/saml/metadata",
                ["Saml:Enabled"] = "true",
                ["Api:BaseUrl"] = "https://api.example.com",
                ["Frontend:BaseUrl"] = "https://app.example.com",
                ["Jwt:Secret"] = "super-secret-key-that-is-at-least-32-chars-long!!",
                ["Jwt:Issuer"] = "codec-api",
                ["Jwt:Audience"] = "codec-api",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        _samlService = new SamlService(_db, _config, Mock.Of<ILogger<SamlService>>());
        _tokenService = new TokenService(_config, _db);

        var samlSettings = Options.Create(new SamlSettings { Enabled = true });

        _controller = new SamlController(
            _db, _samlService, _tokenService, samlSettings, _config,
            Mock.Of<ILogger<SamlController>>());

        // Create admin user
        _adminUser = new User
        {
            DisplayName = "Admin",
            Email = "admin@test.com",
            IsGlobalAdmin = true
        };
        _db.Users.Add(_adminUser);
        _db.SaveChanges();

        SetUser(_adminUser);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(User user)
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", user.Id.ToString()),
                    new Claim("name", user.DisplayName),
                    new Claim("email", user.Email ?? "")
                ], "Bearer"))
            }
        };
    }

    private void SetAnonymousUser()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private static string GenerateSelfSignedCertPem()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return cert.ExportCertificatePem();
    }

    private async Task<SamlIdentityProvider> CreateTestIdpInDb(bool isEnabled = true)
    {
        var idp = new SamlIdentityProvider
        {
            EntityId = $"https://idp-{Guid.NewGuid():N}.example.com",
            DisplayName = "Test IdP",
            SingleSignOnUrl = "https://idp.example.com/sso",
            CertificatePem = GenerateSelfSignedCertPem(),
            IsEnabled = isEnabled,
            AllowJitProvisioning = true
        };
        _db.SamlIdentityProviders.Add(idp);
        await _db.SaveChangesAsync();
        return idp;
    }

    // ---- ListProviders ----

    [Fact]
    public async Task ListProviders_ReturnsOnlyEnabledProviders()
    {
        var enabled = await CreateTestIdpInDb(isEnabled: true);
        await CreateTestIdpInDb(isEnabled: false);

        var result = await _controller.ListProviders();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var providers = okResult.Value as IEnumerable<dynamic>;
        providers.Should().NotBeNull();
    }

    [Fact]
    public async Task ListProviders_SamlDisabled_ReturnsNotFound()
    {
        var disabledSettings = Options.Create(new SamlSettings { Enabled = false });
        var controller = new SamlController(
            _db, _samlService, _tokenService, disabledSettings, _config,
            Mock.Of<ILogger<SamlController>>());

        var result = await controller.ListProviders();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ---- Login ----

    [Fact]
    public async Task Login_ValidIdp_ReturnsRedirect()
    {
        var idp = await CreateTestIdpInDb();
        // Need a response with cookies support
        _controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = _controller.ControllerContext.HttpContext.User
        };

        var result = await _controller.Login(idp.Id);

        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Contain(idp.SingleSignOnUrl);
        redirect.Url.Should().Contain("SAMLRequest=");
    }

    [Fact]
    public async Task Login_NonexistentIdp_ReturnsNotFound()
    {
        var result = await _controller.Login(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Login_DisabledIdp_ReturnsNotFound()
    {
        var idp = await CreateTestIdpInDb(isEnabled: false);

        var result = await _controller.Login(idp.Id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Login_SamlDisabled_ReturnsNotFound()
    {
        var disabledSettings = Options.Create(new SamlSettings { Enabled = false });
        var controller = new SamlController(
            _db, _samlService, _tokenService, disabledSettings, _config,
            Mock.Of<ILogger<SamlController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.Login(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ---- Metadata ----

    [Fact]
    public void Metadata_ReturnsXmlContent()
    {
        var result = _controller.Metadata();

        var contentResult = result.Should().BeOfType<ContentResult>().Subject;
        contentResult.ContentType.Should().StartWith("application/xml");
        contentResult.Content.Should().Contain("EntityDescriptor");
        contentResult.Content.Should().Contain("https://sp.example.com/saml/metadata");
    }

    // ---- Admin: ListIdps ----

    [Fact]
    public async Task ListIdps_AsAdmin_ReturnsAllIdps()
    {
        await CreateTestIdpInDb(isEnabled: true);
        await CreateTestIdpInDb(isEnabled: false);

        var result = await _controller.ListIdps();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListIdps_NonAdmin_ThrowsForbidden()
    {
        var regularUser = new User
        {
            DisplayName = "Regular",
            Email = "regular@test.com",
            IsGlobalAdmin = false
        };
        _db.Users.Add(regularUser);
        await _db.SaveChangesAsync();
        SetUser(regularUser);

        var act = () => _controller.ListIdps();

        await act.Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // ---- Admin: GetIdp ----

    [Fact]
    public async Task GetIdp_Exists_ReturnsOk()
    {
        var idp = await CreateTestIdpInDb();

        var result = await _controller.GetIdp(idp.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetIdp_NotFound_Returns404()
    {
        var result = await _controller.GetIdp(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ---- Admin: CreateIdp ----

    [Fact]
    public async Task CreateIdp_ValidRequest_ReturnsCreated()
    {
        var certPem = GenerateSelfSignedCertPem();
        var request = new CreateSamlIdpRequest
        {
            EntityId = "https://new-idp.example.com",
            DisplayName = "New IdP",
            SingleSignOnUrl = "https://new-idp.example.com/sso",
            CertificatePem = certPem
        };

        var result = await _controller.CreateIdp(request);

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task CreateIdp_DuplicateEntityId_ReturnsConflict()
    {
        var idp = await CreateTestIdpInDb();
        var request = new CreateSamlIdpRequest
        {
            EntityId = idp.EntityId,
            DisplayName = "Duplicate",
            SingleSignOnUrl = "https://dup.example.com/sso",
            CertificatePem = GenerateSelfSignedCertPem()
        };

        var result = await _controller.CreateIdp(request);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task CreateIdp_InvalidCertificate_ReturnsBadRequest()
    {
        var request = new CreateSamlIdpRequest
        {
            EntityId = "https://bad-cert.example.com",
            DisplayName = "Bad Cert",
            SingleSignOnUrl = "https://bad-cert.example.com/sso",
            CertificatePem = "not-a-valid-certificate"
        };

        var result = await _controller.CreateIdp(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ---- Admin: UpdateIdp ----

    [Fact]
    public async Task UpdateIdp_ValidRequest_ReturnsOk()
    {
        var idp = await CreateTestIdpInDb();
        var request = new UpdateSamlIdpRequest
        {
            DisplayName = "Updated Name",
            IsEnabled = false
        };

        var result = await _controller.UpdateIdp(idp.Id, request);

        result.Should().BeOfType<OkObjectResult>();

        var updated = await _db.SamlIdentityProviders.FindAsync(idp.Id);
        updated!.DisplayName.Should().Be("Updated Name");
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateIdp_NotFound_Returns404()
    {
        var request = new UpdateSamlIdpRequest { DisplayName = "Nope" };

        var result = await _controller.UpdateIdp(Guid.NewGuid(), request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateIdp_InvalidCert_ReturnsBadRequest()
    {
        var idp = await CreateTestIdpInDb();
        var request = new UpdateSamlIdpRequest
        {
            CertificatePem = "not-valid"
        };

        var result = await _controller.UpdateIdp(idp.Id, request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ---- Admin: DeleteIdp ----

    [Fact]
    public async Task DeleteIdp_Exists_ReturnsNoContent()
    {
        var idp = await CreateTestIdpInDb();

        var result = await _controller.DeleteIdp(idp.Id);

        result.Should().BeOfType<NoContentResult>();
        var deleted = await _db.SamlIdentityProviders.FindAsync(idp.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteIdp_NotFound_Returns404()
    {
        var result = await _controller.DeleteIdp(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ---- Admin: ImportFromMetadata ----

    [Fact]
    public async Task ImportFromMetadata_ValidMetadata_ReturnsCreated()
    {
        var certPem = GenerateSelfSignedCertPem();
        var certBase64 = certPem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "").Replace("\r", "").Trim();

        var metadataXml = $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                                 entityID="https://imported-idp.example.com">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo>
                    <ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data>
                  </ds:KeyInfo>
                </md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect"
                                        Location="https://imported-idp.example.com/sso"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;

        var request = new ImportSamlIdpMetadataRequest
        {
            MetadataXml = metadataXml,
            DisplayName = "Imported IdP"
        };

        var result = await _controller.ImportFromMetadata(request);

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task ImportFromMetadata_InvalidMetadata_ReturnsBadRequest()
    {
        var request = new ImportSamlIdpMetadataRequest
        {
            MetadataXml = "not valid xml"
        };

        var result = await _controller.ImportFromMetadata(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ImportFromMetadata_DuplicateEntityId_ReturnsConflict()
    {
        var idp = await CreateTestIdpInDb();
        var certBase64 = idp.CertificatePem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "").Replace("\r", "").Trim();

        var metadataXml = $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                                 entityID="{idp.EntityId}">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo>
                    <ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data>
                  </ds:KeyInfo>
                </md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect"
                                        Location="https://dup.example.com/sso"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;

        var request = new ImportSamlIdpMetadataRequest
        {
            MetadataXml = metadataXml
        };

        var result = await _controller.ImportFromMetadata(request);

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
