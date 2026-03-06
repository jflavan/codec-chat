namespace Codec.Api.Services.Exceptions;

public class NotFoundException(string message = "The requested resource was not found.")
    : CodecException(404, message);
