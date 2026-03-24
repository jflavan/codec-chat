using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Codec.Api.Services;

/// <summary>
/// Handles SAML 2.0 SP operations: AuthnRequest generation, Response validation,
/// IdP metadata parsing, and JIT user provisioning.
/// </summary>
public class SamlService(
    CodecDbContext db,
    IConfiguration configuration,
    ILogger<SamlService> logger)
{
    private string SpEntityId =>
        configuration["Saml:EntityId"]
        ?? $"{configuration["Api:BaseUrl"]?.TrimEnd('/')}/auth/saml/metadata";

    private string AcsUrl =>
        $"{configuration["Api:BaseUrl"]?.TrimEnd('/')}/auth/saml/acs";

    /// <summary>
    /// Generates a SAML 2.0 AuthnRequest and returns the IdP redirect URL.
    /// </summary>
    public (string redirectUrl, string requestId) GenerateAuthnRequest(SamlIdentityProvider idp)
    {
        var requestId = $"_codec_{Guid.NewGuid():N}";
        var issueInstant = DateTime.UtcNow.ToString("o");

        var authnRequest = $"""
            <samlp:AuthnRequest xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol"
                                xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion"
                                ID="{requestId}"
                                Version="2.0"
                                IssueInstant="{issueInstant}"
                                Destination="{SecurityElement.Escape(idp.SingleSignOnUrl)}"
                                AssertionConsumerServiceURL="{SecurityElement.Escape(AcsUrl)}"
                                ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST">
              <saml:Issuer>{SecurityElement.Escape(SpEntityId)}</saml:Issuer>
              <samlp:NameIDPolicy Format="urn:oasis:names:tc:SAML:2.0:nameid-format:persistent"
                                  AllowCreate="true"/>
            </samlp:AuthnRequest>
            """;

        // Deflate + Base64 encode for HTTP-Redirect binding
        var deflated = DeflateCompress(Encoding.UTF8.GetBytes(authnRequest));
        var encoded = Convert.ToBase64String(deflated);
        var urlEncoded = Uri.EscapeDataString(encoded);

        var separator = idp.SingleSignOnUrl.Contains('?') ? "&" : "?";
        var redirectUrl = $"{idp.SingleSignOnUrl}{separator}SAMLRequest={urlEncoded}";

        return (redirectUrl, requestId);
    }

    /// <summary>
    /// Validates a SAML 2.0 Response and extracts the authenticated user's attributes.
    /// </summary>
    public SamlAssertionResult? ValidateSamlResponse(string samlResponseBase64, SamlIdentityProvider idp)
    {
        byte[] responseBytes;
        try
        {
            responseBytes = Convert.FromBase64String(samlResponseBase64);
        }
        catch (FormatException)
        {
            logger.LogWarning("SAML response is not valid Base64");
            return null;
        }

        var doc = new XmlDocument { PreserveWhitespace = true };
        try
        {
            // Restrict XML parsing to prevent XXE attacks
            using var reader = XmlReader.Create(
                new MemoryStream(responseBytes),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 1_000_000
                });
            doc.Load(reader);
        }
        catch (XmlException ex)
        {
            logger.LogWarning(ex, "SAML response is not valid XML");
            return null;
        }

        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        nsmgr.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        // Check top-level status
        var statusCode = doc.SelectSingleNode("//samlp:Response/samlp:Status/samlp:StatusCode/@Value", nsmgr)?.Value;
        if (statusCode != "urn:oasis:names:tc:SAML:2.0:status:Success")
        {
            logger.LogWarning("SAML response status is not Success: {Status}", statusCode);
            return null;
        }

        // Validate XML signature
        if (!ValidateSignature(doc, idp))
        {
            logger.LogWarning("SAML response signature validation failed for IdP {IdpId}", idp.Id);
            return null;
        }

        // Extract assertion
        var assertion = doc.SelectSingleNode("//saml:Assertion", nsmgr);
        if (assertion is null)
        {
            logger.LogWarning("SAML response contains no assertion");
            return null;
        }

        // Validate audience restriction
        var audience = assertion.SelectSingleNode(
            "saml:Conditions/saml:AudienceRestriction/saml:Audience", nsmgr)?.InnerText;
        if (audience is not null && audience != SpEntityId)
        {
            logger.LogWarning("SAML audience mismatch: expected {Expected}, got {Got}", SpEntityId, audience);
            return null;
        }

        // Validate time conditions
        var conditions = assertion.SelectSingleNode("saml:Conditions", nsmgr);
        if (conditions is not null)
        {
            var now = DateTime.UtcNow;
            var notBefore = conditions.Attributes?["NotBefore"]?.Value;
            var notOnOrAfter = conditions.Attributes?["NotOnOrAfter"]?.Value;

            // Allow 5-minute clock skew
            var skew = TimeSpan.FromMinutes(5);
            if (notBefore is not null && DateTime.Parse(notBefore).ToUniversalTime() > now.Add(skew))
            {
                logger.LogWarning("SAML assertion not yet valid");
                return null;
            }
            if (notOnOrAfter is not null && DateTime.Parse(notOnOrAfter).ToUniversalTime().Add(skew) < now)
            {
                logger.LogWarning("SAML assertion has expired");
                return null;
            }
        }

        // Extract NameID
        var nameId = assertion.SelectSingleNode("saml:Subject/saml:NameID", nsmgr)?.InnerText;
        if (string.IsNullOrWhiteSpace(nameId))
        {
            logger.LogWarning("SAML assertion contains no NameID");
            return null;
        }

        // Extract attributes
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attrNodes = assertion.SelectNodes("saml:AttributeStatement/saml:Attribute", nsmgr);
        if (attrNodes is not null)
        {
            foreach (XmlNode attr in attrNodes)
            {
                var name = attr.Attributes?["Name"]?.Value;
                var value = attr.SelectSingleNode("saml:AttributeValue", nsmgr)?.InnerText;
                if (name is not null && value is not null)
                    attributes[name] = value;
            }
        }

        return new SamlAssertionResult
        {
            NameId = nameId,
            Email = attributes.GetValueOrDefault("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                    ?? attributes.GetValueOrDefault("email")
                    ?? attributes.GetValueOrDefault("Email"),
            DisplayName = attributes.GetValueOrDefault("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")
                          ?? attributes.GetValueOrDefault("displayName")
                          ?? attributes.GetValueOrDefault("name"),
            FirstName = attributes.GetValueOrDefault("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname")
                        ?? attributes.GetValueOrDefault("firstName")
                        ?? attributes.GetValueOrDefault("givenName"),
            LastName = attributes.GetValueOrDefault("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname")
                       ?? attributes.GetValueOrDefault("lastName")
                       ?? attributes.GetValueOrDefault("sn"),
            Attributes = attributes
        };
    }

    /// <summary>
    /// Finds or JIT-provisions a user from a validated SAML assertion.
    /// </summary>
    public async Task<(User user, bool isNewUser)?> GetOrCreateSamlUserAsync(
        SamlAssertionResult assertion, SamlIdentityProvider idp)
    {
        // Look up by SAML NameID + IdP
        var existing = await db.Users.FirstOrDefaultAsync(
            u => u.SamlNameId == assertion.NameId && u.SamlIdentityProviderId == idp.Id);

        if (existing is not null)
        {
            // Update profile if changed
            var email = assertion.Email?.Trim().ToLowerInvariant();
            var displayName = assertion.DisplayName
                              ?? $"{assertion.FirstName} {assertion.LastName}".Trim();

            var hasChanges = false;
            if (email is not null && existing.Email != email)
            {
                existing.Email = email;
                hasChanges = true;
            }
            if (!string.IsNullOrWhiteSpace(displayName) && existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                hasChanges = true;
            }
            if (hasChanges)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            return (existing, false);
        }

        // Try matching by email if the user was created via another auth method
        if (assertion.Email is not null)
        {
            var emailNorm = assertion.Email.Trim().ToLowerInvariant();
            var emailUser = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNorm);
            if (emailUser is not null)
            {
                // Link SAML identity to existing account
                emailUser.SamlNameId = assertion.NameId;
                emailUser.SamlIdentityProviderId = idp.Id;
                emailUser.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                return (emailUser, false);
            }
        }

        // JIT provision new user
        if (!idp.AllowJitProvisioning)
        {
            logger.LogWarning("JIT provisioning disabled for IdP {IdpId}, user {NameId} not found",
                idp.Id, assertion.NameId);
            return null;
        }

        var newDisplayName = assertion.DisplayName
                             ?? $"{assertion.FirstName} {assertion.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(newDisplayName))
            newDisplayName = assertion.NameId;

        var user = new User
        {
            SamlNameId = assertion.NameId,
            SamlIdentityProviderId = idp.Id,
            Email = assertion.Email?.Trim().ToLowerInvariant(),
            DisplayName = newDisplayName,
            EmailVerified = true // SAML IdP is trusted for email verification
        };

        db.Users.Add(user);

        // Auto-join default server
        var defaultServerExists = await db.Servers
            .AsNoTracking()
            .AnyAsync(s => s.Id == Server.DefaultServerId);

        if (defaultServerExists)
        {
            db.ServerMembers.Add(new ServerMember
            {
                ServerId = Server.DefaultServerId,
                User = user,
                Role = ServerRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Race condition — re-fetch
            db.Entry(user).State = EntityState.Detached;
            var raceUser = await db.Users.FirstAsync(
                u => u.SamlNameId == assertion.NameId && u.SamlIdentityProviderId == idp.Id);
            return (raceUser, false);
        }

        return (user, true);
    }

    /// <summary>
    /// Parses IdP metadata XML and extracts configuration.
    /// </summary>
    public SamlIdentityProvider? ParseIdpMetadata(string metadataXml)
    {
        var doc = new XmlDocument();
        try
        {
            using var reader = XmlReader.Create(
                new StringReader(metadataXml),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            doc.Load(reader);
        }
        catch (XmlException ex)
        {
            logger.LogWarning(ex, "Failed to parse IdP metadata XML");
            return null;
        }

        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("md", "urn:oasis:names:tc:SAML:2.0:metadata");
        nsmgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var entityDescriptor = doc.SelectSingleNode("//md:EntityDescriptor", nsmgr);
        var entityId = entityDescriptor?.Attributes?["entityID"]?.Value;
        if (string.IsNullOrWhiteSpace(entityId))
            return null;

        var ssoNode = doc.SelectSingleNode(
            "//md:IDPSSODescriptor/md:SingleSignOnService[@Binding='urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect']",
            nsmgr);
        ssoNode ??= doc.SelectSingleNode(
            "//md:IDPSSODescriptor/md:SingleSignOnService[@Binding='urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST']",
            nsmgr);
        var ssoUrl = ssoNode?.Attributes?["Location"]?.Value;

        var certNode = doc.SelectSingleNode(
            "//md:IDPSSODescriptor/md:KeyDescriptor[@use='signing']/ds:KeyInfo/ds:X509Data/ds:X509Certificate",
            nsmgr);
        certNode ??= doc.SelectSingleNode(
            "//md:IDPSSODescriptor/md:KeyDescriptor/ds:KeyInfo/ds:X509Data/ds:X509Certificate",
            nsmgr);
        var certBase64 = certNode?.InnerText?.Trim();

        if (ssoUrl is null || certBase64 is null)
            return null;

        // Convert raw Base64 cert to PEM format
        var pem = $"-----BEGIN CERTIFICATE-----\n{certBase64}\n-----END CERTIFICATE-----";

        return new SamlIdentityProvider
        {
            EntityId = entityId,
            SingleSignOnUrl = ssoUrl,
            CertificatePem = pem
        };
    }

    /// <summary>
    /// Generates SP metadata XML for this service provider.
    /// </summary>
    public string GenerateSpMetadata()
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata"
                                 entityID="{SecurityElement.Escape(SpEntityId)}">
              <md:SPSSODescriptor AuthnRequestsSigned="false"
                                   WantAssertionsSigned="true"
                                   protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:persistent</md:NameIDFormat>
                <md:AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                                              Location="{SecurityElement.Escape(AcsUrl)}"
                                              index="0"
                                              isDefault="true"/>
              </md:SPSSODescriptor>
            </md:EntityDescriptor>
            """;
    }

    private bool ValidateSignature(XmlDocument doc, SamlIdentityProvider idp)
    {
        X509Certificate2 cert;
        try
        {
            cert = X509Certificate2.CreateFromPem(idp.CertificatePem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load IdP certificate for {IdpId}", idp.Id);
            return false;
        }

        var signedXml = new SignedXml(doc);
        var signatureNode = doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        if (signatureNode.Count == 0)
        {
            logger.LogWarning("No XML signature found in SAML response");
            return false;
        }

        signedXml.LoadXml((XmlElement)signatureNode[0]!);

        using var rsaKey = cert.GetRSAPublicKey();
        if (rsaKey is not null)
        {
            return signedXml.CheckSignature(rsaKey);
        }

        using var ecdsaKey = cert.GetECDsaPublicKey();
        if (ecdsaKey is not null)
        {
            return signedXml.CheckSignature(ecdsaKey);
        }

        logger.LogWarning("IdP certificate has no supported public key algorithm");
        return false;
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}

/// <summary>
/// Represents the extracted claims from a validated SAML assertion.
/// </summary>
public record SamlAssertionResult
{
    public required string NameId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();
}
