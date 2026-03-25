using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Codec.Api.Controllers;

[ApiController]
[Route("auth/saml")]
[EnableRateLimiting("auth")]
public class SamlController(
    CodecDbContext db,
    SamlService samlService,
    TokenService tokenService,
    IOptions<SamlSettings> samlSettings,
    IConfiguration configuration,
    IMemoryCache memoryCache,
    ILogger<SamlController> logger) : ControllerBase
{
    /// <summary>
    /// Returns the list of enabled SAML identity providers for the login page.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> ListProviders()
    {
        if (!samlSettings.Value.Enabled)
            return NotFound(new { error = "SAML SSO is not enabled." });

        var providers = await db.SamlIdentityProviders
            .AsNoTracking()
            .Where(p => p.IsEnabled)
            .Select(p => new { p.Id, p.DisplayName })
            .ToListAsync();

        return Ok(providers);
    }

    /// <summary>
    /// Initiates SP-initiated SSO by redirecting to the IdP.
    /// </summary>
    [HttpGet("login/{idpId:guid}")]
    public async Task<IActionResult> Login(Guid idpId)
    {
        if (!samlSettings.Value.Enabled)
            return NotFound(new { error = "SAML SSO is not enabled." });

        var idp = await db.SamlIdentityProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == idpId && p.IsEnabled);

        if (idp is null)
            return NotFound(new { error = "Identity provider not found." });

        var (redirectUrl, requestId) = samlService.GenerateAuthnRequest(idp);

        // Store request ID in a short-lived cookie for response correlation
        Response.Cookies.Append("saml_request_id", requestId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/auth/saml"
        });

        // Also store IdP ID for ACS lookup
        Response.Cookies.Append("saml_idp_id", idpId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/auth/saml"
        });

        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Assertion Consumer Service — receives and validates the SAML response from the IdP.
    /// On success, redirects to the frontend with a short-lived authorization code.
    /// </summary>
    [HttpPost("acs")]
    public async Task<IActionResult> AssertionConsumerService()
    {
        if (!samlSettings.Value.Enabled)
            return BadRequest(new { error = "SAML SSO is not enabled." });

        var samlResponse = Request.Form["SAMLResponse"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(samlResponse))
            return BadRequest(new { error = "Missing SAMLResponse." });

        // Retrieve IdP ID from cookie
        if (!Request.Cookies.TryGetValue("saml_idp_id", out var idpIdStr)
            || !Guid.TryParse(idpIdStr, out var idpId))
        {
            return BadRequest(new { error = "Missing SAML session context." });
        }

        // Validate InResponseTo against the stored request ID
        if (!Request.Cookies.TryGetValue("saml_request_id", out var expectedRequestId)
            || string.IsNullOrWhiteSpace(expectedRequestId))
        {
            return BadRequest(new { error = "Missing SAML request ID cookie." });
        }

        // Parse the SAML response to extract InResponseTo
        string? inResponseTo;
        try
        {
            var responseBytes = Convert.FromBase64String(samlResponse);
            var doc = new XmlDocument { PreserveWhitespace = true };
            using var reader = XmlReader.Create(
                new MemoryStream(responseBytes),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
            doc.Load(reader);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
            inResponseTo = doc.SelectSingleNode("//samlp:Response", nsmgr)?.Attributes?["InResponseTo"]?.Value;
        }
        catch
        {
            return BadRequest(new { error = "Failed to parse SAML response." });
        }

        if (inResponseTo != expectedRequestId)
        {
            logger.LogWarning("SAML InResponseTo mismatch: expected {Expected}, got {Got}", expectedRequestId, inResponseTo);
            return BadRequest(new { error = "SAML response does not match the original request." });
        }

        var idp = await db.SamlIdentityProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == idpId && p.IsEnabled);

        if (idp is null)
            return BadRequest(new { error = "Identity provider not found." });

        // Validate SAML response
        var assertion = samlService.ValidateSamlResponse(samlResponse, idp);
        if (assertion is null)
            return Unauthorized(new { error = "SAML assertion validation failed." });

        // Find or provision user
        var result = await samlService.GetOrCreateSamlUserAsync(assertion, idp);
        if (result is null)
            return Unauthorized(new { error = "User provisioning failed. JIT provisioning may be disabled." });

        var (user, isNewUser) = result.Value;

        // Issue codec-api JWT tokens
        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        // Clear SAML cookies
        Response.Cookies.Delete("saml_request_id", new CookieOptions { Path = "/auth/saml" });
        Response.Cookies.Delete("saml_idp_id", new CookieOptions { Path = "/auth/saml" });

        // Store tokens behind a single-use authorization code (never expose tokens in URL)
        var code = Guid.NewGuid().ToString("N");
        var cacheKey = $"saml_code:{code}";
        memoryCache.Set(cacheKey, new SamlAuthCode
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IsNewUser = isNewUser
        }, TimeSpan.FromSeconds(60));

        // Redirect to frontend with only the opaque code
        var frontendBaseUrl = configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5174";
        var redirectUrl = $"{frontendBaseUrl}/auth/saml/callback?code={Uri.EscapeDataString(code)}";

        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Exchanges a single-use SAML authorization code for access and refresh tokens.
    /// </summary>
    [HttpPost("exchange")]
    public IActionResult ExchangeCode([FromBody] SamlExchangeRequest request)
    {
        if (!samlSettings.Value.Enabled)
            return BadRequest(new { error = "SAML SSO is not enabled." });

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Missing authorization code." });

        var cacheKey = $"saml_code:{request.Code}";
        if (!memoryCache.TryGetValue<SamlAuthCode>(cacheKey, out var authCode) || authCode is null)
        {
            return BadRequest(new { error = "Invalid or expired authorization code." });
        }

        // Remove code immediately — single use
        memoryCache.Remove(cacheKey);

        return Ok(new
        {
            accessToken = authCode.AccessToken,
            refreshToken = authCode.RefreshToken,
            isNewUser = authCode.IsNewUser
        });
    }

    /// <summary>
    /// SP metadata endpoint for IdP configuration.
    /// </summary>
    [HttpGet("metadata")]
    [EnableRateLimiting("fixed")]
    public IActionResult Metadata()
    {
        var metadata = samlService.GenerateSpMetadata();
        return Content(metadata, "application/xml", System.Text.Encoding.UTF8);
    }

    // ---- Admin endpoints for IdP management ----

    /// <summary>
    /// Lists all configured SAML identity providers (admin only).
    /// </summary>
    [HttpGet("idps")]
    [Authorize]
    public async Task<IActionResult> ListIdps()
    {
        await EnsureGlobalAdminAsync();

        var idps = await db.SamlIdentityProviders
            .AsNoTracking()
            .OrderBy(p => p.DisplayName)
            .Select(p => new
            {
                p.Id,
                p.EntityId,
                p.DisplayName,
                p.SingleSignOnUrl,
                p.IsEnabled,
                p.AllowJitProvisioning,
                p.CreatedAt,
                p.UpdatedAt
            })
            .ToListAsync();

        return Ok(idps);
    }

    /// <summary>
    /// Gets a specific SAML identity provider configuration (admin only).
    /// </summary>
    [HttpGet("idps/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetIdp(Guid id)
    {
        await EnsureGlobalAdminAsync();

        var idp = await db.SamlIdentityProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (idp is null)
            return NotFound(new { error = "Identity provider not found." });

        return Ok(new
        {
            idp.Id,
            idp.EntityId,
            idp.DisplayName,
            idp.SingleSignOnUrl,
            idp.CertificatePem,
            idp.IsEnabled,
            idp.AllowJitProvisioning,
            idp.CreatedAt,
            idp.UpdatedAt
        });
    }

    /// <summary>
    /// Creates a new SAML identity provider configuration (admin only).
    /// </summary>
    [HttpPost("idps")]
    [Authorize]
    public async Task<IActionResult> CreateIdp([FromBody] CreateSamlIdpRequest request)
    {
        await EnsureGlobalAdminAsync();

        // Validate certificate
        if (!ValidateCertificatePem(request.CertificatePem))
            return BadRequest(new { error = "Invalid X.509 certificate PEM." });

        // Check for duplicate entity ID
        var exists = await db.SamlIdentityProviders.AnyAsync(p => p.EntityId == request.EntityId);
        if (exists)
            return Conflict(new { error = "An identity provider with this entity ID already exists." });

        var idp = new SamlIdentityProvider
        {
            EntityId = request.EntityId,
            DisplayName = request.DisplayName,
            SingleSignOnUrl = request.SingleSignOnUrl,
            CertificatePem = request.CertificatePem,
            IsEnabled = request.IsEnabled,
            AllowJitProvisioning = request.AllowJitProvisioning
        };

        db.SamlIdentityProviders.Add(idp);
        await db.SaveChangesAsync();

        logger.LogInformation("SAML IdP created: {IdpId} ({EntityId})", idp.Id, idp.EntityId);

        return Created($"/auth/saml/idps/{idp.Id}", new
        {
            idp.Id,
            idp.EntityId,
            idp.DisplayName,
            idp.SingleSignOnUrl,
            idp.IsEnabled,
            idp.AllowJitProvisioning,
            idp.CreatedAt
        });
    }

    /// <summary>
    /// Updates a SAML identity provider configuration (admin only).
    /// </summary>
    [HttpPut("idps/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateIdp(Guid id, [FromBody] UpdateSamlIdpRequest request)
    {
        await EnsureGlobalAdminAsync();

        var idp = await db.SamlIdentityProviders.FirstOrDefaultAsync(p => p.Id == id);
        if (idp is null)
            return NotFound(new { error = "Identity provider not found." });

        if (request.CertificatePem is not null && !ValidateCertificatePem(request.CertificatePem))
            return BadRequest(new { error = "Invalid X.509 certificate PEM." });

        if (request.DisplayName is not null) idp.DisplayName = request.DisplayName;
        if (request.SingleSignOnUrl is not null) idp.SingleSignOnUrl = request.SingleSignOnUrl;
        if (request.CertificatePem is not null) idp.CertificatePem = request.CertificatePem;
        if (request.IsEnabled.HasValue) idp.IsEnabled = request.IsEnabled.Value;
        if (request.AllowJitProvisioning.HasValue) idp.AllowJitProvisioning = request.AllowJitProvisioning.Value;

        idp.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("SAML IdP updated: {IdpId}", idp.Id);

        return Ok(new
        {
            idp.Id,
            idp.EntityId,
            idp.DisplayName,
            idp.SingleSignOnUrl,
            idp.IsEnabled,
            idp.AllowJitProvisioning,
            idp.UpdatedAt
        });
    }

    /// <summary>
    /// Deletes a SAML identity provider configuration (admin only).
    /// </summary>
    [HttpDelete("idps/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteIdp(Guid id)
    {
        await EnsureGlobalAdminAsync();

        var idp = await db.SamlIdentityProviders.FirstOrDefaultAsync(p => p.Id == id);
        if (idp is null)
            return NotFound(new { error = "Identity provider not found." });

        db.SamlIdentityProviders.Remove(idp);
        await db.SaveChangesAsync();

        logger.LogInformation("SAML IdP deleted: {IdpId} ({EntityId})", idp.Id, idp.EntityId);

        return NoContent();
    }

    /// <summary>
    /// Imports an IdP configuration from metadata XML (admin only).
    /// </summary>
    [HttpPost("idps/from-metadata")]
    [Authorize]
    public async Task<IActionResult> ImportFromMetadata([FromBody] ImportSamlIdpMetadataRequest request)
    {
        await EnsureGlobalAdminAsync();

        var parsed = samlService.ParseIdpMetadata(request.MetadataXml);
        if (parsed is null)
            return BadRequest(new { error = "Failed to parse IdP metadata. Ensure it contains an EntityDescriptor with SSO service and signing certificate." });

        // Check for duplicate entity ID
        var exists = await db.SamlIdentityProviders.AnyAsync(p => p.EntityId == parsed.EntityId);
        if (exists)
            return Conflict(new { error = "An identity provider with this entity ID already exists." });

        parsed.DisplayName = request.DisplayName ?? parsed.EntityId;
        parsed.AllowJitProvisioning = request.AllowJitProvisioning;

        db.SamlIdentityProviders.Add(parsed);
        await db.SaveChangesAsync();

        logger.LogInformation("SAML IdP imported from metadata: {IdpId} ({EntityId})", parsed.Id, parsed.EntityId);

        return Created($"/auth/saml/idps/{parsed.Id}", new
        {
            parsed.Id,
            parsed.EntityId,
            parsed.DisplayName,
            parsed.SingleSignOnUrl,
            parsed.IsEnabled,
            parsed.AllowJitProvisioning,
            parsed.CreatedAt
        });
    }

    private async Task EnsureGlobalAdminAsync()
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            throw new Services.Exceptions.ForbiddenException();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !user.IsGlobalAdmin)
            throw new Services.Exceptions.ForbiddenException();
    }

    private static bool ValidateCertificatePem(string pem)
    {
        try
        {
            _ = X509Certificate2.CreateFromPem(pem);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
