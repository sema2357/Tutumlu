using PriceAggregator.Api.Endpoints;

namespace PriceAggregator.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, string connectionString)
    {
        app.MapAuthEndpoints();
        app.MapFavoritesEndpoints();
        app.MapPurchaseHistoryEndpoints();
        app.MapNotificationEndpoints();
        app.MapBrandEndpoints(connectionString);
        app.MapStoreEndpoints(connectionString);
        app.MapCategoryEndpoints(connectionString);
        app.MapAttributeEndpoints(connectionString);
        app.MapSearchEndpoints(connectionString);
        app.MapProductEndpoints(connectionString);

        return app;
    }
}
