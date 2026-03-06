namespace Codec.Api.Services.Exceptions;

public abstract class CodecException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
