using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PriceAggregator.Api.Helpers;

public static class AuthHelpers
{
    public static int? GetUserId(ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return idClaim is not null && int.TryParse(idClaim, out var id) ? id : null;
    }
}
