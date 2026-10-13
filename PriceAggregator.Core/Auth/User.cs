namespace PriceAggregator.Core.Auth;

public record User(
    int Id, string Email, string PasswordHash, string? DisplayName, DateTime CreatedAt,
    string? Name = null, string? Surname = null, string? Phone = null, DateTime? BirthDate = null, string? City = null,
    string Role = "User");
