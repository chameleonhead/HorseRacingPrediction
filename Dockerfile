FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HorseRacingPrediction.sln ./
COPY src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj src/HorseRacingPrediction.Api/
COPY src/HorseRacingPrediction.ApiClient/HorseRacingPrediction.ApiClient.csproj src/HorseRacingPrediction.ApiClient/
COPY src/HorseRacingPrediction.Agents/HorseRacingPrediction.Agents.csproj src/HorseRacingPrediction.Agents/
COPY src/HorseRacingPrediction.Application/HorseRacingPrediction.Application.csproj src/HorseRacingPrediction.Application/
COPY src/HorseRacingPrediction.Domain/HorseRacingPrediction.Domain.csproj src/HorseRacingPrediction.Domain/
COPY src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj src/HorseRacingPrediction.Infrastructure/
COPY src/HorseRacingPrediction.MachineLearning/HorseRacingPrediction.MachineLearning.csproj src/HorseRacingPrediction.MachineLearning/
COPY src/HorseRacingPrediction.Collector/HorseRacingPrediction.Collector.csproj src/HorseRacingPrediction.Collector/
COPY src/HorseRacingPrediction.CollectionOperations/HorseRacingPrediction.CollectionOperations.csproj src/HorseRacingPrediction.CollectionOperations/
COPY src/HorseRacingPrediction.Contracts/HorseRacingPrediction.Contracts.csproj src/HorseRacingPrediction.Contracts/
COPY src/HorseRacingPrediction.Scraping/HorseRacingPrediction.Scraping.csproj src/HorseRacingPrediction.Scraping/
RUN dotnet restore src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj

COPY . .
RUN dotnet publish src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "HorseRacingPrediction.Api.dll"]
