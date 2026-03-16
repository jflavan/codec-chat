using System.Security.Cryptography;
using System.Text;
using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class EmailVerificationService(
    CodecDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    public async Task<string> GenerateAndSendVerificationAsync(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        user.EmailVerificationToken = tokenHash;
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        user.EmailVerificationTokenSentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var frontendBaseUrl = configuration["Frontend:BaseUrl"]
            ?? configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()
            ?? "http://localhost:5174";

        var verifyUrl = $"{frontendBaseUrl.TrimEnd('/')}/verify?token={Uri.EscapeDataString(rawToken)}";

        var htmlBody = $"""
            <div style="font-family: sans-serif; max-width: 480px; margin: 0 auto;">
                <h2>Verify your Codec email</h2>
                <p>Click the button below to verify your email address:</p>
                <a href="{verifyUrl}"
                   style="display: inline-block; padding: 12px 24px; background: #5865F2; color: white;
                          text-decoration: none; border-radius: 4px; font-weight: 600;">
                    Verify Email
                </a>
                <p style="margin-top: 24px; color: #666; font-size: 14px;">
                    Or copy this link: {verifyUrl}
                </p>
                <p style="color: #999; font-size: 12px;">This link expires in 24 hours.</p>
            </div>
            """;

        await emailSender.SendEmailAsync(user.Email!, "Verify your Codec email", htmlBody);

        return rawToken;
    }

    public async Task<User?> VerifyTokenAsync(string rawToken)
    {
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == tokenHash);
        if (user is null) return null;
        if (user.EmailVerificationTokenExpiresAt < DateTimeOffset.UtcNow) return null;
        if (user.EmailVerified) return null;

        user.EmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.EmailVerificationTokenSentAt = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return user;
    }

    public bool CanResend(User user)
    {
        if (user.EmailVerified) return false;
        if (user.EmailVerificationTokenSentAt is not null
            && user.EmailVerificationTokenSentAt.Value.Add(ResendCooldown) > DateTimeOffset.UtcNow)
            return false;
        return true;
    }
}
