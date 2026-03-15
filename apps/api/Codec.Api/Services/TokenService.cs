using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Codec.Api.Services;

public class TokenService(IConfiguration configuration, CodecDbContext db)
{
    public string GenerateAccessToken(User user)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is required.");
        var issuer = configuration["Jwt:Issuer"] ?? "codec-api";
        var audience = configuration["Jwt:Audience"] ?? "codec-api";
        var expiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var m) ? m : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var avatarUrl = user.CustomAvatarPath ?? user.AvatarUrl;

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email ?? ""),
            new("name", user.EffectiveDisplayName),
        };

        if (avatarUrl is not null)
            claims.Add(new Claim("picture", avatarUrl));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<(string opaqueToken, RefreshToken entity)> GenerateRefreshTokenAsync(User user)
    {
        var opaqueToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken)));

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };

        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        return (opaqueToken, refreshToken);
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string opaqueToken)
    {
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken)));

        var refreshToken = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (refreshToken is null) return null;
        if (refreshToken.RevokedAt is not null) return null;
        if (refreshToken.ExpiresAt < DateTimeOffset.UtcNow) return null;

        return refreshToken;
    }

    public async Task RevokeRefreshTokenAsync(RefreshToken token)
    {
        token.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RevokeAllUserRefreshTokensAsync(Guid userId)
    {
        await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, DateTimeOffset.UtcNow));
    }
}
