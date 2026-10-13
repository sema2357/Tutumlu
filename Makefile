.PHONY: run dev all worker scraper build clean

run:
	dotnet run --project PriceAggregator.Api --launch-profile http

dev:
	dotnet run --project PriceAggregator.Api --launch-profile http

all:
	./run.sh all

worker:
	dotnet run --project PriceAggregator.Worker

scraper:
	dotnet run --project PriceAggregator.Scraper

build:
	dotnet build

clean:
	dotnet clean
