namespace LucidAdmin.Core.Exceptions;

public class DuplicateEntityException : LucidException
{
    public string EntityType { get; }
    public string DuplicateKey { get; }

    public DuplicateEntityException(string entityType, string duplicateKey)
        : base($"{entityType} with key '{duplicateKey}' already exists.")
    {
        EntityType = entityType;
        DuplicateKey = duplicateKey;
    }
}
