using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using PriceAggregator.Core.Notifications;

namespace PriceAggregator.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal principal, NotificationRepository notificationRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await notificationRepository.SyncFavoriteNotificationsForUserAsync(userId.Value);

            var notifications = await notificationRepository.GetByUserIdAsync(userId.Value);
            return Results.Ok(notifications);
        });

        group.MapGet("/unread-count", async (ClaimsPrincipal principal, NotificationRepository notificationRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            var count = await notificationRepository.GetUnreadCountAsync(userId.Value);
            return Results.Ok(new { unreadCount = count });
        });

        group.MapPost("/{id:int}/read", async (int id, ClaimsPrincipal principal, NotificationRepository notificationRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await notificationRepository.MarkAsReadAsync(userId.Value, id);
            return Results.Ok();
        });

        group.MapPost("/read-all", async (ClaimsPrincipal principal, NotificationRepository notificationRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await notificationRepository.MarkAllAsReadAsync(userId.Value);
            return Results.Ok();
        });

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, NotificationRepository notificationRepository) =>
        {
            var userId = GetUserId(principal);
            if (userId is null) return Results.Unauthorized();

            await notificationRepository.DeleteAsync(userId.Value, id);
            return Results.Ok();
        });
    }

    private static int? GetUserId(ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return idClaim is not null && int.TryParse(idClaim, out var id) ? id : null;
    }
}
