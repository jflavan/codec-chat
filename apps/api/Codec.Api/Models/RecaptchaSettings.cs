namespace Codec.Api.Models;

public class RecaptchaSettings
{
    public string SecretKey { get; set; } = "";
    public string SiteKey { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public double ScoreThreshold { get; set; } = 0.5;
    public bool Enabled { get; set; } = true;
}
