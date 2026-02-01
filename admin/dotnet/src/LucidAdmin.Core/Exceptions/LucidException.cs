namespace LucidAdmin.Core.Exceptions;

public class LucidException : Exception
{
    public LucidException(string message) : base(message) { }
    public LucidException(string message, Exception inner) : base(message, inner) { }
}
