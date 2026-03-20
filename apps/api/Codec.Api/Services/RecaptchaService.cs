using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codec.Api.Models;
using Microsoft.Extensions.Options;

namespace Codec.Api.Services;

public class RecaptchaService(HttpClient httpClient, IOptions<RecaptchaSettings> options, ILogger<RecaptchaService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public virtual async Task<(bool Success, double Score, string? Error)> VerifyAsync(string token, string expectedAction)
    {
        try
        {
            var settings = options.Value;
            var url = $"https://recaptchaenterprise.googleapis.com/v1/projects/{settings.ProjectId}/assessments?key={settings.SecretKey}";

            var requestBody = new
            {
                @event = new
                {
                    token,
                    siteKey = settings.SiteKey,
                    expectedAction
                }
            };

            var response = await httpClient.PostAsJsonAsync(url, requestBody, JsonOptions);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AssessmentResponse>(JsonOptions);

            if (result is null)
            {
                logger.LogWarning("reCAPTCHA Enterprise returned null response");
                return (false, 0, "Invalid reCAPTCHA response.");
            }

            if (result.TokenProperties is null || !result.TokenProperties.Valid)
            {
                logger.LogWarning("reCAPTCHA token invalid. Reason: {Reason}", result.TokenProperties?.InvalidReason);
                return (false, 0, "reCAPTCHA verification failed.");
            }

            if (!string.Equals(result.TokenProperties.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("reCAPTCHA action mismatch. Expected: {Expected}, Got: {Actual}", expectedAction, result.TokenProperties.Action);
                return (false, result.RiskAnalysis?.Score ?? 0, "reCAPTCHA action mismatch.");
            }

            var score = result.RiskAnalysis?.Score ?? 0;
            if (score < settings.ScoreThreshold)
            {
                logger.LogInformation("reCAPTCHA score {Score} below threshold {Threshold}", score, settings.ScoreThreshold);
                return (false, score, "reCAPTCHA score too low.");
            }

            return (true, score, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "reCAPTCHA verification request failed");
            return (false, 0, "reCAPTCHA verification unavailable.");
        }
    }

    private class AssessmentResponse
    {
        public TokenPropertiesData? TokenProperties { get; set; }
        public RiskAnalysisData? RiskAnalysis { get; set; }
    }

    private class TokenPropertiesData
    {
        public bool Valid { get; set; }
        public string? InvalidReason { get; set; }
        public string? Action { get; set; }
    }

    private class RiskAnalysisData
    {
        public double Score { get; set; }
    }
}
