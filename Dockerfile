# Construye la API de Luz & Vida y la deja lista para Render.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LuzVidaApi/LuzVidaApi.csproj LuzVidaApi/
RUN dotnet restore LuzVidaApi/LuzVidaApi.csproj
COPY LuzVidaApi/ LuzVidaApi/
RUN dotnet publish LuzVidaApi/LuzVidaApi.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
# La pagina se entrega desde la propia API.
COPY maria.html wwwroot/index.html
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "LuzVidaApi.dll"]
