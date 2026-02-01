namespace LucidAdmin.Core.Exceptions;

public class EntityNotFoundException : LucidException
{
    public string EntityType { get; }
    public Guid EntityId { get; }

    public EntityNotFoundException(string entityType, Guid entityId)
        : base($"{entityType} with ID {entityId} was not found.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}
