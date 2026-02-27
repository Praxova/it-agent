using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Interfaces.Repositories;
using LucidAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidAdmin.Infrastructure.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(LucidDbContext context) : base(context) { }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<IEnumerable<User>> GetEnabledAsync(CancellationToken ct = default)
    {
        return await _dbSet
            .Where(u => u.IsEnabled)
            .ToListAsync(ct);
    }

    public async Task UpdateLastLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.LastLogin = DateTime.UtcNow;
            await UpdateAsync(user, ct);
        }
    }

    public async Task IncrementFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.FailedLoginAttempts++;
            // Engage lockout after 5 failures
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
                // Don't reset counter — operator can see how many attempts were made
            }
            await UpdateAsync(user, ct);
        }
    }

    public async Task ResetFailedLoginAsync(Guid id, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(id, ct);
        if (user != null)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await UpdateAsync(user, ct);
        }
    }
}
