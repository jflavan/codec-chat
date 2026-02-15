FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Codec.Api/Codec.Api.csproj Codec.Api/
RUN dotnet restore Codec.Api/Codec.Api.csproj
COPY . .
WORKDIR /src/Codec.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

RUN useradd --no-create-home --shell /bin/false appuser
USER appuser

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Codec.Api.dll"]
