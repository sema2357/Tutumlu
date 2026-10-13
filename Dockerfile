# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["PriceAggregator.slnx", "."]
COPY ["PriceAggregator.Api/", "PriceAggregator.Api/"]
COPY ["PriceAggregator.Core/", "PriceAggregator.Core/"]
COPY ["PriceAggregator.Scraper/", "PriceAggregator.Scraper/"]
COPY ["PriceAggregator.Worker/", "PriceAggregator.Worker/"]
COPY ["kategori_urun_listesi.txt", "."]

RUN dotnet restore "PriceAggregator.slnx"
RUN dotnet publish "PriceAggregator.Api/PriceAggregator.Api.csproj" -c Release -o /app/publish/api
RUN dotnet publish "PriceAggregator.Worker/PriceAggregator.Worker.csproj" -c Release -o /app/publish/worker
RUN dotnet publish "PriceAggregator.Scraper/PriceAggregator.Scraper.csproj" -c Release -o /app/publish/scraper

# API Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api-runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
  CMD dotnet --version || exit 1
COPY --from=build /app/publish/api .
COPY --from=build /src/PriceAggregator.Api/Frontend /app/Frontend
COPY --from=build /src/kategori_urun_listesi.txt /app/kategori_urun_listesi.txt
ENTRYPOINT ["dotnet", "PriceAggregator.Api.dll"]

# Worker Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker-runtime
WORKDIR /app
COPY --from=build /app/publish/worker .
COPY --from=build /src/kategori_urun_listesi.txt /app/kategori_urun_listesi.txt
ENTRYPOINT ["dotnet", "PriceAggregator.Worker.dll"]

# Scraper Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS scraper-runtime
WORKDIR /app
COPY --from=build /app/publish/scraper .
COPY --from=build /src/kategori_urun_listesi.txt /app/kategori_urun_listesi.txt
ENTRYPOINT ["dotnet", "PriceAggregator.Scraper.dll"]
