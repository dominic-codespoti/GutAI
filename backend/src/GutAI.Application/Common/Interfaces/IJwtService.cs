using System.Security.Claims;

namespace GutAI.Application.Common.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string email);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
}
