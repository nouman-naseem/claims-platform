namespace Claims.Application.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object? key = null)
        : base(key is null ? $"{entity} was not found." : $"{entity} '{key}' was not found.") { }
}

public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.") : base(message) { }
}

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException() : base("Invalid email or password.") { }
}
