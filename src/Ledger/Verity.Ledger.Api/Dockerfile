FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Verity.slnx ./
COPY src/Shared/Verity.Shared.Contracts/Verity.Shared.Contracts.csproj src/Shared/Verity.Shared.Contracts/
COPY src/Ledger/Verity.Ledger.Domain/Verity.Ledger.Domain.csproj src/Ledger/Verity.Ledger.Domain/
COPY src/Ledger/Verity.Ledger.Application/Verity.Ledger.Application.csproj src/Ledger/Verity.Ledger.Application/
COPY src/Ledger/Verity.Ledger.Infrastructure/Verity.Ledger.Infrastructure.csproj src/Ledger/Verity.Ledger.Infrastructure/
COPY src/Ledger/Verity.Ledger.Api/Verity.Ledger.Api.csproj src/Ledger/Verity.Ledger.Api/
RUN dotnet restore src/Ledger/Verity.Ledger.Api/Verity.Ledger.Api.csproj

COPY src/Shared/Verity.Shared.Contracts/ src/Shared/Verity.Shared.Contracts/
COPY src/Ledger/ src/Ledger/
RUN dotnet publish src/Ledger/Verity.Ledger.Api/Verity.Ledger.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Verity.Ledger.Api.dll"]
