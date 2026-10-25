namespace Qwader.Domain.Common;

public abstract class Entity
{
    public int Id { get; set; }
}

/// <summary>Entities whose creation/modification is stamped automatically on SaveChanges.</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    int? CreatedById { get; set; }
    DateTime? UpdatedAt { get; set; }
    int? UpdatedById { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public int? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedById { get; set; }
}
