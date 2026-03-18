# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ENV APP_UID=1004
USER ${APP_UID}
WORKDIR /app
EXPOSE 8080


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Raven.Core/Raven.Core.csproj", "Raven.Core/"]
RUN dotnet restore "./Raven.Core/Raven.Core.csproj"
COPY . .
WORKDIR "/src/Raven.Core"
RUN dotnet build "./Raven.Core.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Raven.Core.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN mkdir -p /data/workspace && chown -R $APP_UID /data/workspace
ENV RAVEN_WORKSPACE_ROOT=/data/workspace
VOLUME ["/data/workspace"]
USER $APP_UID
ENTRYPOINT ["dotnet", "Raven.Core.dll"]