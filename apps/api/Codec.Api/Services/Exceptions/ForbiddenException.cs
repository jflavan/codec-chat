namespace Codec.Api.Services.Exceptions;

public class ForbiddenException(string message = "You do not have permission to perform this action.") : Exception(message);
