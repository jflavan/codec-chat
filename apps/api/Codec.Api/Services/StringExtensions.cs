namespace Codec.Api.Services;

public static class StringExtensions
{
    /// <summary>
    /// Escapes special characters for use in PostgreSQL ILIKE/LIKE patterns.
    /// </summary>
    public static string EscapeForLike(this string value)
    {
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
