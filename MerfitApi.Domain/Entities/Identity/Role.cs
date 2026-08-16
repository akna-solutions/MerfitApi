using Merfit.Domain.Common;

namespace Merfit.Domain.Entities.Identity;

public sealed class Role : BaseEntity
{
    public required string Name { get; set; }
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}
