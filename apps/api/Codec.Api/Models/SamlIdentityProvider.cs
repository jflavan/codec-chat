namespace Codec.Api.Models;

public class SamlIdentityProvider
{
    public Guid Id { get; set; }

    /// <summary>
    /// IdP entity ID (e.g. "https://idp.example.com/saml/metadata").
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name shown on the login page.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// IdP Single Sign-On URL for HTTP-Redirect or HTTP-POST binding.
    /// </summary>
    public string SingleSignOnUrl { get; set; } = string.Empty;

    /// <summary>
    /// PEM-encoded X.509 certificate used to verify SAML response signatures.
    /// </summary>
    public string CertificatePem { get; set; } = string.Empty;

    /// <summary>
    /// Whether this IdP is active and can be used for sign-in.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether to automatically create user accounts on first SAML sign-in.
    /// </summary>
    public bool AllowJitProvisioning { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
