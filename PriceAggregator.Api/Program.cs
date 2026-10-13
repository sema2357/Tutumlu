using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using PriceAggregator.Api.Extensions;
using PriceAggregator.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCorsPolicy();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRedisAndCaching(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHttpJsonOptions();

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

var frontendPath = Path.Combine(builder.Environment.ContentRootPath, "Frontend");
if (!Directory.Exists(frontendPath))
{
    var altPath = Path.Combine(builder.Environment.ContentRootPath, "PriceAggregator.Api", "Frontend");
    if (Directory.Exists(altPath)) frontendPath = altPath;
}

if (Directory.Exists(frontendPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(frontendPath)
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(frontendPath),
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            ctx.Context.Response.Headers.Append("Pragma", "no-cache");
            ctx.Context.Response.Headers.Append("Expires", "0");
        }
    });
}

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

EnsureIndexesCreated(connectionString);
WarmupDatabase(connectionString);

app.MapApiEndpoints(connectionString);

app.Run();

static void EnsureIndexesCreated(string connStr)
{
    try
    {
        using var db = new SqlConnection(connStr);
        db.Execute(@"
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Offers_ProductId_Price')
                CREATE INDEX IX_Offers_ProductId_Price ON Offers(ProductId, Price) INCLUDE (RegularPrice);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_BrandId')
                CREATE INDEX IX_Products_BrandId ON Products(BrandId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_CategoryId')
                CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductAttributes_ProductId')
                CREATE INDEX IX_ProductAttributes_ProductId ON ProductAttributes(ProductId);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Categories_SectorId')
                CREATE INDEX IX_Categories_SectorId ON Categories(SectorId);
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DatabaseSetup] EnsureIndexesCreated failed: {ex.Message}");
    }
}

static void WarmupDatabase(string connStr)
{
    _ = Task.Run(async () =>
    {
        try
        {
            using var db = new SqlConnection(connStr);
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products", commandTimeout: 60);
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Offers", commandTimeout: 60);
        }
        catch
        {
        }
    });
}

