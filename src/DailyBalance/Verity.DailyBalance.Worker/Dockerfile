FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Verity.slnx ./
COPY src/Shared/Verity.Shared.Contracts/Verity.Shared.Contracts.csproj src/Shared/Verity.Shared.Contracts/
COPY src/DailyBalance/Verity.DailyBalance.Domain/Verity.DailyBalance.Domain.csproj src/DailyBalance/Verity.DailyBalance.Domain/
COPY src/DailyBalance/Verity.DailyBalance.Application/Verity.DailyBalance.Application.csproj src/DailyBalance/Verity.DailyBalance.Application/
COPY src/DailyBalance/Verity.DailyBalance.Infrastructure/Verity.DailyBalance.Infrastructure.csproj src/DailyBalance/Verity.DailyBalance.Infrastructure/
COPY src/DailyBalance/Verity.DailyBalance.Worker/Verity.DailyBalance.Worker.csproj src/DailyBalance/Verity.DailyBalance.Worker/
RUN dotnet restore src/DailyBalance/Verity.DailyBalance.Worker/Verity.DailyBalance.Worker.csproj

COPY src/Shared/Verity.Shared.Contracts/ src/Shared/Verity.Shared.Contracts/
COPY src/DailyBalance/ src/DailyBalance/
RUN dotnet publish src/DailyBalance/Verity.DailyBalance.Worker/Verity.DailyBalance.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Verity.DailyBalance.Worker.dll"]
