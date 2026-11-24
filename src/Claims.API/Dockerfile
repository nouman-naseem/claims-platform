FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Claims.Domain/Claims.Domain.csproj src/Claims.Domain/
COPY src/Claims.Application/Claims.Application.csproj src/Claims.Application/
COPY src/Claims.Infrastructure/Claims.Infrastructure.csproj src/Claims.Infrastructure/
COPY src/Claims.API/Claims.API.csproj src/Claims.API/
RUN dotnet restore src/Claims.API/Claims.API.csproj
COPY src/ src/
RUN dotnet publish src/Claims.API/Claims.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Claims.API.dll"]
