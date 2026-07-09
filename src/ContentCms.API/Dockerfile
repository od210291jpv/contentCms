# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["ContentCms.API/ContentCms.API.csproj", "ContentCms.API/"]
RUN dotnet restore "ContentCms.API/ContentCms.API.csproj"

# Copy the rest of the source code and build
COPY . .
WORKDIR "/src/ContentCms.API"
RUN dotnet build "ContentCms.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "ContentCms.API.csproj" -c Release -o /app/publish \
    -p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "ContentCms.API.dll"]
