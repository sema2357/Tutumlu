using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PriceAggregator.Api.Services;
using PriceAggregator.Core;

namespace PriceAggregator.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest request, UserRepository userRepository, IJwtTokenService tokenService) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? "";
            var password = request.Password ?? "";
            var name = request.Name?.Trim() ?? "";
            var surname = request.Surname?.Trim() ?? "";
            var phone = request.Phone?.Trim() ?? "";
            var city = request.City?.Trim() ?? "";

            if (!tokenService.IsValidEmail(email)) return Results.BadRequest(new { error = "Geçerli bir e-posta adresi girin (örnek: ad@alanadi.com)." });
            if (password.Length < 6) return Results.BadRequest(new { error = "Şifre en az 6 karakter olmalı." });
            if (name.Length == 0) return Results.BadRequest(new { error = "Ad alanı zorunludur." });
            if (surname.Length == 0) return Results.BadRequest(new { error = "Soyad alanı zorunludur." });
            if (!IsValidPhone(phone)) return Results.BadRequest(new { error = "Geçerli bir telefon numarası girin (örnek: 05xxxxxxxxx)." });
            if (request.BirthDate is null) return Results.BadRequest(new { error = "Doğum tarihi zorunludur." });
            if (request.BirthDate.Value.Date > DateTime.UtcNow.Date) return Results.BadRequest(new { error = "Doğum tarihi bugünden sonraki bir tarih olamaz." });
            if (city.Length == 0) return Results.BadRequest(new { error = "Şehir alanı zorunludur." });

            var user = await userRepository.CreateAsync(email, PasswordHasher.Hash(password), name, surname, phone, request.BirthDate, city);
            if (user is null) return Results.Conflict(new { error = "Bu e-posta adresi zaten kayıtlı." });

            var token = tokenService.CreateToken(user);
            return Results.Ok(new { token, user = ToDto(user) });
        });

        group.MapPost("/login", async (LoginRequest request, UserRepository userRepository, IJwtTokenService tokenService) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? "";
            var user = await userRepository.GetByEmailAsync(email);

            if (user is null || !PasswordHasher.Verify(request.Password ?? "", user.PasswordHash))
                return Results.Unauthorized();

            var token = tokenService.CreateToken(user);
            return Results.Ok(new { token, user = ToDto(user) });
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, UserRepository userRepository) =>
        {
            var user = await GetCurrentUserAsync(principal, userRepository);
            if (user is null) return Results.Unauthorized();

            return Results.Ok(ToDto(user));
        }).RequireAuthorization();

        group.MapPut("/me", async (UpdateProfileRequest request, ClaimsPrincipal principal, UserRepository userRepository, IJwtTokenService tokenService) =>
        {
            var currentUser = await GetCurrentUserAsync(principal, userRepository);
            if (currentUser is null) return Results.Unauthorized();

            var email = request.Email?.Trim().ToLowerInvariant() ?? "";
            if (!tokenService.IsValidEmail(email)) return Results.BadRequest(new { error = "Geçerli bir e-posta adresi girin (örnek: ad@alanadi.com)." });
            if (request.BirthDate is not null && request.BirthDate.Value.Date > DateTime.UtcNow.Date)
                return Results.BadRequest(new { error = "Doğum tarihi bugünden sonraki bir tarih olamaz." });

            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name!.Trim();
            var surname = string.IsNullOrWhiteSpace(request.Surname) ? null : request.Surname!.Trim();
            var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone!.Trim();
            var city = string.IsNullOrWhiteSpace(request.City) ? null : request.City!.Trim();

            if (phone is not null && !IsValidPhone(phone))
                return Results.BadRequest(new { error = "Geçerli bir telefon numarası girin (örnek: 05xxxxxxxxx)." });

            var updated = await userRepository.UpdateProfileAsync(currentUser.Id, email, name, surname, phone, request.BirthDate, city);
            if (updated is null) return Results.Conflict(new { error = "Bu e-posta adresi zaten kayıtlı." });

            return Results.Ok(ToDto(updated));
        }).RequireAuthorization();

        group.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal principal, UserRepository userRepository) =>
        {
            var currentUser = await GetCurrentUserAsync(principal, userRepository);
            if (currentUser is null) return Results.Unauthorized();

            var oldPassword = request.OldPassword ?? "";
            var newPassword = request.NewPassword ?? "";
            var confirmPassword = request.ConfirmPassword ?? "";

            if (!PasswordHasher.Verify(oldPassword, currentUser.PasswordHash))
                return Results.BadRequest(new { error = "Mevcut şifreniz yanlış." });

            if (newPassword.Length < 6)
                return Results.BadRequest(new { error = "Yeni şifre en az 6 karakter olmalıdır." });

            if (newPassword == oldPassword)
                return Results.BadRequest(new { error = "Yeni şifreniz mevcut şifrenizle aynı olamaz." });

            if (newPassword != confirmPassword)
                return Results.BadRequest(new { error = "Yeni şifreler eşleşmiyor." });

            await userRepository.UpdatePasswordHashAsync(currentUser.Id, PasswordHasher.Hash(newPassword));

            return Results.Ok(new { message = "Şifreniz başarıyla güncellendi." });
        }).RequireAuthorization();
    }

    private static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length is 10 or 11 && ((digits.StartsWith("5") && digits.Length == 10) || (digits.StartsWith("05") && digits.Length == 11));
    }

    private static async Task<User?> GetCurrentUserAsync(ClaimsPrincipal principal, UserRepository userRepository)
    {
        var idClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (idClaim is null || !int.TryParse(idClaim, out var id)) return null;

        return await userRepository.GetByIdAsync(id);
    }

    private static object ToDto(User user) => new
    {
        user.Id,
        user.Email,
        user.DisplayName,
        user.Name,
        user.Surname,
        user.Phone,
        user.BirthDate,
        user.City,
        user.Role
    };
}

public record RegisterRequest(string? Email, string? Password, string? Name, string? Surname, string? Phone, DateTime? BirthDate, string? City);
public record LoginRequest(string? Email, string? Password);
public record UpdateProfileRequest(string? Email, string? Name, string? Surname, string? Phone, DateTime? BirthDate, string? City);
public record ChangePasswordRequest(string? OldPassword, string? NewPassword, string? ConfirmPassword);
