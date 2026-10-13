using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PriceAggregator.Core;

namespace PriceAggregator.Api.Services;

public interface IJwtTokenService
{
    string CreateToken(User user);
    bool IsValidEmail(string email);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 255) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            email,
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public string CreateToken(User user)
    {
        var secret = _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "PriceAggregator";
        var audience = _configuration["Jwt:Audience"] ?? "PriceAggregator";
        var expiresMinutes = _configuration.GetValue<int?>("Jwt:ExpiresMinutes") ?? 10080;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("displayName", user.DisplayName ?? ""),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(expiresMinutes), signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
