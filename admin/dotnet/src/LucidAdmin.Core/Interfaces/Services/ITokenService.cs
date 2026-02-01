using LucidAdmin.Core.Entities;

namespace LucidAdmin.Core.Interfaces.Services;

public interface ITokenService
{
    string GenerateToken(User user);
    bool ValidateToken(string token);
    Guid? GetUserIdFromToken(string token);
}
