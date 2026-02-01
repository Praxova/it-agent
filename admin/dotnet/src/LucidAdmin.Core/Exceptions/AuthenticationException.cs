namespace LucidAdmin.Core.Exceptions;

public class AuthenticationException : LucidException
{
    public AuthenticationException(string message) : base(message) { }
}
