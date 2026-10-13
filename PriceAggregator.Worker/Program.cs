using PriceAggregator.Core;
using PriceAggregator.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Registration order here is the order Worker iterates scrapers within each sector.
builder.Services.AddSingleton<IProductScraper>(_ => new MigrosScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new MacroCenterScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new MarketFiyatiScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new HakmarExpressScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new CagriMarketScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new EvideaScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new MionScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new IkeaScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new TeknosaScraper(new HttpClient()));
builder.Services.AddSingleton<IProductScraper>(_ => new AvansasScraper(new HttpClient()));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
