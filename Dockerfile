# ----------------------------------------------------------
# Stage 1: Build the Angular 19 frontend
# ----------------------------------------------------------
FROM node:22-alpine AS node-build

WORKDIR /src/client

# Install dependencies first (layer cache)
COPY Broot.Redirect.Client/package.json Broot.Redirect.Client/package-lock.json ./

RUN npm ci --ignore-scripts

# Copy source and build (base href defaults to / from angular.json)
COPY Broot.Redirect.Client/ ./

RUN npx ng build --configuration production

# ----------------------------------------------------------
# Stage 2: Build the .NET 8 backend
# ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

# Restore dependencies first (layer cache)
COPY Broot.Redirect.Core/Broot.Redirect.Core.csproj Broot.Redirect.Core/
COPY Broot.Redirect.Infrastructure/Broot.Redirect.Infrastructure.csproj Broot.Redirect.Infrastructure/
COPY Broot.Redirect.API/Broot.Redirect.API.csproj Broot.Redirect.API/

RUN dotnet restore Broot.Redirect.API/Broot.Redirect.API.csproj

# Copy all source and publish
COPY Broot.Redirect.Core/ Broot.Redirect.Core/
COPY Broot.Redirect.Infrastructure/ Broot.Redirect.Infrastructure/
COPY Broot.Redirect.API/ Broot.Redirect.API/

RUN dotnet publish Broot.Redirect.API/Broot.Redirect.API.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ----------------------------------------------------------
# Stage 3: Runtime image
# ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app

EXPOSE 8080

# Copy the published .NET app
COPY --from=dotnet-build /app/publish .

# Copy the Angular build output into wwwroot/
# Angular 19 outputs to dist/<project>/browser/
COPY --from=node-build /src/client/dist/broot.redirect.client/browser/ ./wwwroot/

ENTRYPOINT ["dotnet", "Broot.Redirect.API.dll"]