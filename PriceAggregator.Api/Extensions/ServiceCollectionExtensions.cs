using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PriceAggregator.Api.Services;
using PriceAggregator.Core;
using PriceAggregator.Core.Categorization;
using PriceAggregator.Core.Favorites;
using PriceAggregator.Core.Notifications;
using PriceAggregator.Core.PurchaseHistory;
using PriceAggregator.Core.Scrapers;
using StackExchange.Redis;

namespace PriceAggregator.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            });
        });
        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret not configured.");
        var jwtIssuer = configuration["Jwt:Issuer"] ?? "PriceAggregator";
        var jwtAudience = configuration["Jwt:Audience"] ?? "PriceAggregator";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddRedisAndCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConn = configuration.GetConnectionString("Redis");
        bool useRedis = false;

        if (!string.IsNullOrEmpty(redisConn))
        {
            try
            {
                var configOptions = ConfigurationOptions.Parse(redisConn);
                configOptions.ConnectTimeout = 1000;
                configOptions.SyncTimeout = 1000;
                configOptions.AbortOnConnectFail = false;

                var redisMultiplexer = ConnectionMultiplexer.Connect(configOptions);
                if (redisMultiplexer.IsConnected)
                {
                    services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
                    services.AddStackExchangeRedisCache(options =>
                    {
                        options.ConfigurationOptions = configOptions;
                        options.InstanceName = "PriceAggregator_";
                    });
                    useRedis = true;
                }
            }
            catch
            {
            }
        }

        if (!useRedis)
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, CacheService>();
        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection")!;

        services.AddSingleton<IProductScraper>(_ => new MigrosScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new MacroCenterScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new MarketFiyatiScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new HakmarExpressScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new CagriMarketScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new EvideaScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new MionScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new IkeaScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new TeknosaScraper(new HttpClient()));
        services.AddSingleton<IProductScraper>(_ => new AvansasScraper(new HttpClient()));

        services.AddScoped<ProductRepository>(_ => new ProductRepository(connStr));
        services.AddScoped<MatchingService>(_ => new MatchingService(connStr));
        services.AddScoped<UserRepository>(_ => new UserRepository(connStr));
        services.AddScoped<FavoritesRepository>(_ => new FavoritesRepository(connStr));
        services.AddScoped<PurchaseHistoryRepository>(_ => new PurchaseHistoryRepository(connStr));
        services.AddScoped<NotificationRepository>(_ => new NotificationRepository(connStr));
        services.AddScoped<LiveSearchService>();
        services.AddSingleton<ElasticsearchService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        services.AddHostedService<DailySearchSnapshotWorker>();
        services.AddHostedService<DailyProductMatchReviewWorker>();

        return services;
    }

    public static IServiceCollection AddHttpJsonOptions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });
        return services;
    }
}
