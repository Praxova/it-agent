using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IEnumerable<User>> GetEnabledAsync(CancellationToken ct = default);
    Task UpdateLastLoginAsync(Guid id, CancellationToken ct = default);
    Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default);
    Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default);
}
