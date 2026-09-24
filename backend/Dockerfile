# Backend/orquestador — ASP.NET Core Web API.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Vectify.sln ./
COPY Vectify.Api/Vectify.Api.csproj Vectify.Api/
COPY Vectify.Api.Tests/Vectify.Api.Tests.csproj Vectify.Api.Tests/
RUN dotnet restore Vectify.Api/Vectify.Api.csproj

COPY Vectify.Api/ Vectify.Api/
RUN dotnet publish Vectify.Api/Vectify.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development

EXPOSE 8080

ENTRYPOINT ["dotnet", "Vectify.Api.dll"]
