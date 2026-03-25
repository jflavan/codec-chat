using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Services;

public class SamlServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly IConfiguration _config;
    private readonly SamlService _service;

    private static readonly string TestEntityId = "https://sp.example.com/saml/metadata";
    private static readonly string TestApiBaseUrl = "https://api.example.com";

    public SamlServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Saml:EntityId"] = TestEntityId,
                ["Api:BaseUrl"] = TestApiBaseUrl
            })
            .Build();

        _service = new SamlService(_db, _config, Mock.Of<ILogger<SamlService>>());
    }

    public void Dispose() => _db.Dispose();

    private static SamlIdentityProvider CreateTestIdp(
        bool isEnabled = true,
        bool allowJit = true,
        string? certPem = null)
    {
        return new SamlIdentityProvider
        {
            Id = Guid.NewGuid(),
            EntityId = "https://idp.example.com/saml/metadata",
            DisplayName = "Test IdP",
            SingleSignOnUrl = "https://idp.example.com/sso",
            CertificatePem = certPem ?? GenerateSelfSignedCertPem(),
            IsEnabled = isEnabled,
            AllowJitProvisioning = allowJit
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

    private static (X509Certificate2 cert, RSA key) GenerateTestCertWithKey()
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Test IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return (cert, rsa);
    }

    // ---- GenerateAuthnRequest ----

    [Fact]
    public void GenerateAuthnRequest_ReturnsRedirectUrlWithSamlRequest()
    {
        var idp = CreateTestIdp();

        var (redirectUrl, requestId) = _service.GenerateAuthnRequest(idp);

        redirectUrl.Should().StartWith(idp.SingleSignOnUrl);
        redirectUrl.Should().Contain("SAMLRequest=");
        requestId.Should().StartWith("_codec_");
    }

    [Fact]
    public void GenerateAuthnRequest_UsesAmpersandWhenUrlHasQueryString()
    {
        var idp = CreateTestIdp();
        idp.SingleSignOnUrl = "https://idp.example.com/sso?tenant=abc";

        var (redirectUrl, _) = _service.GenerateAuthnRequest(idp);

        redirectUrl.Should().Contain("?tenant=abc&SAMLRequest=");
    }

    [Fact]
    public void GenerateAuthnRequest_GeneratesUniqueRequestIds()
    {
        var idp = CreateTestIdp();

        var (_, id1) = _service.GenerateAuthnRequest(idp);
        var (_, id2) = _service.GenerateAuthnRequest(idp);

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void GenerateAuthnRequest_ProducesDeflatedBase64EncodedRequest()
    {
        var idp = CreateTestIdp();

        var (redirectUrl, _) = _service.GenerateAuthnRequest(idp);

        // Extract the SAMLRequest parameter
        var uri = new Uri(redirectUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var samlRequest = query["SAMLRequest"];
        samlRequest.Should().NotBeNullOrEmpty();

        // Should be valid base64 that deflate-decompresses to XML
        var compressed = Convert.FromBase64String(samlRequest!);
        using var input = new MemoryStream(compressed);
        using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        var xml = Encoding.UTF8.GetString(output.ToArray());
        xml.Should().Contain("AuthnRequest");
        xml.Should().Contain(TestEntityId);
    }

    // ---- ValidateSamlResponse ----

    [Fact]
    public void ValidateSamlResponse_InvalidBase64_ReturnsNull()
    {
        var idp = CreateTestIdp();

        var result = _service.ValidateSamlResponse("not-valid-base64!!!", idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_InvalidXml_ReturnsNull()
    {
        var idp = CreateTestIdp();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("not xml at all"));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_NonSuccessStatus_ReturnsNull()
    {
        var idp = CreateTestIdp();
        var xml = BuildSamlResponseXml(statusCode: "urn:oasis:names:tc:SAML:2.0:status:Requester");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_ValidSignedResponse_ExtractsAttributes()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        var xml = BuildSamlResponseXml(
            nameId: "user@example.com",
            email: "user@example.com",
            displayName: "Test User",
            audience: TestEntityId);
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().NotBeNull();
        result!.NameId.Should().Be("user@example.com");
        result.Email.Should().Be("user@example.com");
        result.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public void ValidateSamlResponse_WrongSigningKey_ReturnsNull()
    {
        var (cert, _) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        // Sign with a different key
        using var differentKey = RSA.Create(2048);
        var xml = BuildSamlResponseXml(nameId: "user@test.com", audience: TestEntityId);
        var signedXml = SignXml(xml, differentKey);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_AudienceMismatch_ReturnsNull()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        var xml = BuildSamlResponseXml(
            nameId: "user@test.com",
            audience: "https://wrong-audience.example.com");
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_ExpiredAssertion_ReturnsNull()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        var xml = BuildSamlResponseXml(
            nameId: "user@test.com",
            audience: TestEntityId,
            notBefore: DateTime.UtcNow.AddHours(-2),
            notOnOrAfter: DateTime.UtcNow.AddHours(-1));
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_FutureAssertion_ReturnsNull()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        var xml = BuildSamlResponseXml(
            nameId: "user@test.com",
            audience: TestEntityId,
            notBefore: DateTime.UtcNow.AddHours(2),
            notOnOrAfter: DateTime.UtcNow.AddHours(3));
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_NoSignature_ReturnsNull()
    {
        var idp = CreateTestIdp();
        var xml = BuildSamlResponseXml(nameId: "user@test.com", audience: TestEntityId);
        // Don't sign it
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_NoNameId_ReturnsNull()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        // Build response with empty NameID
        var xml = BuildSamlResponseXml(nameId: "", audience: TestEntityId);
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSamlResponse_ExtractsClaimsStyleAttributes()
    {
        var (cert, key) = GenerateTestCertWithKey();
        var idp = CreateTestIdp(certPem: cert.ExportCertificatePem());

        // Use claims-style attribute names
        var attributes = new Dictionary<string, string>
        {
            ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"] = "claims@test.com",
            ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"] = "Claims User",
            ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname"] = "Claims",
            ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname"] = "User"
        };

        var xml = BuildSamlResponseXml(
            nameId: "claims-user",
            audience: TestEntityId,
            customAttributes: attributes);
        var signedXml = SignXml(xml, key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        var result = _service.ValidateSamlResponse(encoded, idp);

        result.Should().NotBeNull();
        result!.Email.Should().Be("claims@test.com");
        result.DisplayName.Should().Be("Claims User");
        result.FirstName.Should().Be("Claims");
        result.LastName.Should().Be("User");
    }

    // ---- GetOrCreateSamlUserAsync ----

    [Fact]
    public async Task GetOrCreateSamlUserAsync_ExistingUser_ReturnsWithIsNewFalse()
    {
        var idp = CreateTestIdp();
        _db.SamlIdentityProviders.Add(idp);

        var existingUser = new User
        {
            SamlNameId = "saml-user-1",
            SamlIdentityProviderId = idp.Id,
            Email = "existing@test.com",
            DisplayName = "Existing User"
        };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "saml-user-1",
            Email = "existing@test.com",
            DisplayName = "Existing User"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().NotBeNull();
        result!.Value.isNewUser.Should().BeFalse();
        result.Value.user.Id.Should().Be(existingUser.Id);
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_ExistingUser_UpdatesChangedProfile()
    {
        var idp = CreateTestIdp();
        _db.SamlIdentityProviders.Add(idp);

        var existingUser = new User
        {
            SamlNameId = "saml-user-1",
            SamlIdentityProviderId = idp.Id,
            Email = "old@test.com",
            DisplayName = "Old Name"
        };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "saml-user-1",
            Email = "new@test.com",
            DisplayName = "New Name"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().NotBeNull();
        result!.Value.user.Email.Should().Be("new@test.com");
        result.Value.user.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_EmailMatch_ThrowsRequiresExplicitLink()
    {
        var idp = CreateTestIdp();
        _db.SamlIdentityProviders.Add(idp);

        var emailUser = new User
        {
            Email = "match@test.com",
            DisplayName = "Email User",
            PasswordHash = "some-hash"
        };
        _db.Users.Add(emailUser);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "saml-linked",
            Email = "match@test.com"
        };

        var act = async () => await _service.GetOrCreateSamlUserAsync(assertion, idp);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*link*");
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_JitEnabled_CreatesNewUser()
    {
        var idp = CreateTestIdp(allowJit: true);
        _db.SamlIdentityProviders.Add(idp);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "new-saml-user",
            Email = "new@test.com",
            DisplayName = "New SAML User"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().NotBeNull();
        result!.Value.isNewUser.Should().BeTrue();
        result.Value.user.SamlNameId.Should().Be("new-saml-user");
        result.Value.user.Email.Should().Be("new@test.com");
        result.Value.user.DisplayName.Should().Be("New SAML User");
        result.Value.user.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_JitDisabled_ReturnsNull()
    {
        var idp = CreateTestIdp(allowJit: false);
        _db.SamlIdentityProviders.Add(idp);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "new-saml-user",
            Email = "new@test.com"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_NoDisplayName_UsesNameId()
    {
        var idp = CreateTestIdp(allowJit: true);
        _db.SamlIdentityProviders.Add(idp);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "nameid-fallback",
            Email = "no-name@test.com"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().NotBeNull();
        result!.Value.user.DisplayName.Should().Be("nameid-fallback");
    }

    [Fact]
    public async Task GetOrCreateSamlUserAsync_FirstLastName_CombinesForDisplayName()
    {
        var idp = CreateTestIdp(allowJit: true);
        _db.SamlIdentityProviders.Add(idp);
        await _db.SaveChangesAsync();

        var assertion = new SamlAssertionResult
        {
            NameId = "first-last-user",
            Email = "fl@test.com",
            FirstName = "Jane",
            LastName = "Doe"
        };

        var result = await _service.GetOrCreateSamlUserAsync(assertion, idp);

        result.Should().NotBeNull();
        result!.Value.user.DisplayName.Should().Be("Jane Doe");
    }

    // ---- ParseIdpMetadata ----

    [Fact]
    public void ParseIdpMetadata_ValidMetadata_ExtractsConfiguration()
    {
        var certPem = GenerateSelfSignedCertPem();
        var certBase64 = ExtractBase64FromPem(certPem);

        var metadata = BuildIdpMetadataXml(
            entityId: "https://idp.example.com",
            ssoUrl: "https://idp.example.com/sso",
            certBase64: certBase64);

        var result = _service.ParseIdpMetadata(metadata);

        result.Should().NotBeNull();
        result!.EntityId.Should().Be("https://idp.example.com");
        result.SingleSignOnUrl.Should().Be("https://idp.example.com/sso");
        result.CertificatePem.Should().Contain("BEGIN CERTIFICATE");
    }

    [Fact]
    public void ParseIdpMetadata_InvalidXml_ReturnsNull()
    {
        var result = _service.ParseIdpMetadata("not valid xml");

        result.Should().BeNull();
    }

    [Fact]
    public void ParseIdpMetadata_MissingEntityId_ReturnsNull()
    {
        var metadata = """
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect"
                                        Location="https://idp.example.com/sso"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;

        var result = _service.ParseIdpMetadata(metadata);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseIdpMetadata_MissingSsoUrl_ReturnsNull()
    {
        var certBase64 = ExtractBase64FromPem(GenerateSelfSignedCertPem());
        var metadata = $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                                 entityID="https://idp.example.com">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo>
                    <ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data>
                  </ds:KeyInfo>
                </md:KeyDescriptor>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;

        var result = _service.ParseIdpMetadata(metadata);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseIdpMetadata_HttpPostBinding_FallsBackCorrectly()
    {
        var certBase64 = ExtractBase64FromPem(GenerateSelfSignedCertPem());
        var metadata = $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                                 entityID="https://idp.example.com">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo>
                    <ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data>
                  </ds:KeyInfo>
                </md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                                        Location="https://idp.example.com/sso-post"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;

        var result = _service.ParseIdpMetadata(metadata);

        result.Should().NotBeNull();
        result!.SingleSignOnUrl.Should().Be("https://idp.example.com/sso-post");
    }

    // ---- GenerateSpMetadata ----

    [Fact]
    public void GenerateSpMetadata_ContainsEntityIdAndAcsUrl()
    {
        var metadata = _service.GenerateSpMetadata();

        metadata.Should().Contain($"entityID=\"{TestEntityId}\"");
        metadata.Should().Contain($"Location=\"{TestApiBaseUrl}/auth/saml/acs\"");
        metadata.Should().Contain("urn:oasis:names:tc:SAML:2.0:nameid-format:persistent");
    }

    [Fact]
    public void GenerateSpMetadata_DefaultsEntityIdFromApiBaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "https://myapp.example.com"
            })
            .Build();
        var service = new SamlService(_db, config, Mock.Of<ILogger<SamlService>>());

        var metadata = service.GenerateSpMetadata();

        metadata.Should().Contain("entityID=\"https://myapp.example.com/auth/saml/metadata\"");
    }

    // ---- Helper methods ----

    private static string BuildSamlResponseXml(
        string statusCode = "urn:oasis:names:tc:SAML:2.0:status:Success",
        string nameId = "user@test.com",
        string? email = null,
        string? displayName = null,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? notOnOrAfter = null,
        Dictionary<string, string>? customAttributes = null)
    {
        var conditionsBlock = "";
        if (audience is not null || notBefore.HasValue || notOnOrAfter.HasValue)
        {
            var nb = notBefore ?? DateTime.UtcNow.AddMinutes(-5);
            var noa = notOnOrAfter ?? DateTime.UtcNow.AddMinutes(5);
            var audienceBlock = audience is not null
                ? $"<saml:AudienceRestriction><saml:Audience>{audience}</saml:Audience></saml:AudienceRestriction>"
                : "";
            conditionsBlock = $"""
                <saml:Conditions NotBefore="{nb:o}" NotOnOrAfter="{noa:o}">
                  {audienceBlock}
                </saml:Conditions>
                """;
        }

        var attributes = customAttributes ?? new Dictionary<string, string>();
        if (email is not null && !attributes.ContainsKey("email"))
            attributes["email"] = email;
        if (displayName is not null && !attributes.ContainsKey("displayName"))
            attributes["displayName"] = displayName;

        var attrStatements = "";
        if (attributes.Count > 0)
        {
            var sb = new StringBuilder("<saml:AttributeStatement>");
            foreach (var (key, value) in attributes)
            {
                sb.Append($"""
                    <saml:Attribute Name="{key}">
                      <saml:AttributeValue>{value}</saml:AttributeValue>
                    </saml:Attribute>
                    """);
            }
            sb.Append("</saml:AttributeStatement>");
            attrStatements = sb.ToString();
        }

        return $"""
            <samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol"
                            xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion"
                            ID="_response_1" Version="2.0" IssueInstant="{DateTime.UtcNow:o}">
              <samlp:Status>
                <samlp:StatusCode Value="{statusCode}"/>
              </samlp:Status>
              <saml:Assertion Version="2.0" ID="_assertion_1" IssueInstant="{DateTime.UtcNow:o}">
                <saml:Issuer>https://idp.example.com</saml:Issuer>
                {conditionsBlock}
                <saml:Subject>
                  <saml:NameID Format="urn:oasis:names:tc:SAML:2.0:nameid-format:persistent">{nameId}</saml:NameID>
                </saml:Subject>
                {attrStatements}
              </saml:Assertion>
            </samlp:Response>
            """;
    }

    private static string SignXml(string xml, RSA key)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var signedXml = new SignedXml(doc)
        {
            SigningKey = key
        };

        var reference = new Reference { Uri = "" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        signedXml.ComputeSignature();
        var signatureElement = signedXml.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signatureElement, true));

        return doc.OuterXml;
    }

    private static string BuildIdpMetadataXml(string entityId, string ssoUrl, string certBase64)
    {
        return $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                                 entityID="{entityId}">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo>
                    <ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data>
                  </ds:KeyInfo>
                </md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect"
                                        Location="{ssoUrl}"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;
    }

    private static string ExtractBase64FromPem(string pem)
    {
        return pem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();
    }
}
