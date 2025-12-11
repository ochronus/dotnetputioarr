# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Csharparr.sln .
COPY src/Csharparr/Csharparr.csproj src/Csharparr/
COPY tests/Csharparr.Tests/Csharparr.Tests.csproj tests/Csharparr.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ src/
COPY tests/ tests/

# Build and publish
ARG VERSION=0.5.0
RUN dotnet publish src/Csharparr/Csharparr.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:Version=${VERSION}

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# Install ca-certificates and tzdata for HTTPS and timezone support
RUN apk add --no-cache ca-certificates tzdata

# Create non-root user
RUN addgroup -g 1000 csharparr && \
    adduser -D -u 1000 -G csharparr csharparr

# Create directories
RUN mkdir -p /config /downloads && \
    chown -R csharparr:csharparr /config /downloads

WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV PUID=1000
ENV PGID=1000
ENV TZ=Etc/UTC
ENV ASPNETCORE_URLS=http://+:9091
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Expose port
EXPOSE 9091

# Volume for config and downloads
VOLUME ["/config", "/downloads"]

# Switch to non-root user
USER csharparr

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:9091/transmission/rpc || exit 1

# Set the entrypoint
ENTRYPOINT ["dotnet", "csharparr.dll"]

# Default command
CMD ["run", "-c", "/config/config.toml"]
