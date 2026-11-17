# Image unique de l'API, utilisée par les instances (« api ») et par le migrateur (« migrator », argument --migrate).
# Contexte de build : racine du dépôt (voir docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration d'abord (couche mise en cache tant que les projets ne changent pas).
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Shop.Domain/Shop.Domain.csproj src/Shop.Domain/
COPY src/Shop.Application/Shop.Application.csproj src/Shop.Application/
COPY src/Shop.Infrastructure/Shop.Infrastructure.csproj src/Shop.Infrastructure/
COPY src/Shop.Api/Shop.Api.csproj src/Shop.Api/
RUN dotnet restore src/Shop.Api/Shop.Api.csproj

COPY src/ src/
RUN dotnet publish src/Shop.Api/Shop.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Utilisateur non root fourni par l'image officielle.
USER $APP_UID
ENTRYPOINT ["dotnet", "Shop.Api.dll"]
