FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY HorseRacingPrediction.sln ./
COPY src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj src/HorseRacingPrediction.Api/
COPY src/HorseRacingPrediction.Agents/HorseRacingPrediction.Agents.csproj src/HorseRacingPrediction.Agents/
COPY src/HorseRacingPrediction.Application/HorseRacingPrediction.Application.csproj src/HorseRacingPrediction.Application/
COPY src/HorseRacingPrediction.Domain/HorseRacingPrediction.Domain.csproj src/HorseRacingPrediction.Domain/
COPY src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj src/HorseRacingPrediction.Infrastructure/
COPY src/HorseRacingPrediction.MachineLearning/HorseRacingPrediction.MachineLearning.csproj src/HorseRacingPrediction.MachineLearning/
RUN dotnet restore src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj

COPY . .
RUN dotnet publish src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "HorseRacingPrediction.Api.dll"]