using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public record CreateSamlIdpRequest
{
    [Required, MaxLength(500)]
    public string EntityId { get; init; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, Url, MaxLength(2000)]
    public string SingleSignOnUrl { get; init; } = string.Empty;

    [Required]
    public string CertificatePem { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;
    public bool AllowJitProvisioning { get; init; } = true;
}

public record UpdateSamlIdpRequest
{
    [MaxLength(200)]
    public string? DisplayName { get; init; }

    [Url, MaxLength(2000)]
    public string? SingleSignOnUrl { get; init; }

    public string? CertificatePem { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? AllowJitProvisioning { get; init; }
}

public record ImportSamlIdpMetadataRequest
{
    [Required]
    public string MetadataXml { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; init; }

    public bool AllowJitProvisioning { get; init; } = true;
}

public record SamlExchangeRequest
{
    [Required]
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Internal cache entry for the single-use SAML authorization code pattern.
/// </summary>
public record SamlAuthCode
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public bool IsNewUser { get; init; }
}

public record SamlSettings
{
    /// <summary>
    /// SP entity ID (audience). Defaults to "{Api:BaseUrl}/auth/saml/metadata".
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Whether SAML SSO is enabled globally.
    /// </summary>
    public bool Enabled { get; init; }
}
