# csharparr

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Proxy that allows put.io to be used as a download client for sonarr/radarr/whisparr. The proxy uses the Transmission protocol.

This is a C# / .NET 10 port of the [original Rust implementation](https://github.com/wouterdebie/putioarr).

## Installation

### From Source

Make sure you have .NET 10 SDK or later installed.

```bash
# Clone the repository
git clone https://github.com/ochronus/csharparr.git
cd csharparr

# Build
dotnet build -c Release

# Run
dotnet run --project src/Csharparr -- run

# Or publish a self-contained executable
dotnet publish -c Release -o publish
```

### Docker

```bash
docker run -d \
  --name=csharparr \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=Etc/UTC \
  -p 9091:9091 \
  -v /path/to/csharparr/config:/config \
  -v /path/to/your/downloads:/downloads \
  --restart unless-stopped \
  ghcr.io/ochronus/csharparr:latest
```

#### Docker Compose

```yaml
version: "3.8"
services:
  csharparr:
    image: ghcr.io/ochronus/csharparr:latest
    container_name: csharparr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    volumes:
      - /path/to/csharparr/config:/config
      - /path/to/your/downloads:/downloads
    ports:
      - 9091:9091
    restart: unless-stopped
```

## Usage

### Commands

```bash
# Run the proxy
csharparr run

# Run with custom config path
csharparr run -c /path/to/config.toml

# Generate a put.io API token
csharparr get-token

# Generate a config file (will prompt for put.io authentication)
csharparr generate-config

# Generate config at a specific path
csharparr generate-config -c /path/to/config.toml

# Show version
csharparr version
```

### First-Time Setup

1. Run `csharparr generate-config` to create a configuration file
2. Follow the prompts to authenticate with put.io
3. Edit the generated config file to set:
   - `username` and `password` for the proxy

- `instance_name` to tag transfers and the folder they live in on put.io (3-10 alphanumeric chars)
  - Run `csharparr create-instance-folder -c /path/to/config.toml` to create the put.io folder for this instance and copy the printed `instance_folder_id` into your config
- `download_directory` for where files should be downloaded
- Sonarr/Radarr/Whisparr connection details

4. Run `csharparr run` to start the proxy
5. Configure the Transmission download client in sonarr/radarr/whisparr:
   - Host: Your csharparr host
   - Port: 9091 (default)
   - Url Base: /transmission
   - Username: <configured username>
   - Password: <configured password>

## Configuration

A configuration file can be specified using `-c`, but the default configuration file location is:

- Linux/macOS: `~/.config/putioarr/config.toml`

TOML is used as the configuration format:

```toml
# Required. Username and password that sonarr/radarr/whisparr use to connect to the proxy
username = "myusername"
password = "mypassword"

# Required. Alphanumeric (3-10 chars). Transfers are tagged with this value and saved under a folder of
# the same name on put.io.
instance_name = "myinstance"

# Required. Numeric folder ID for this instance on put.io. Run `csharparr create-instance-folder -c /path/to/config.toml`
# to create the folder and then paste the printed ID here.
instance_folder_id = 0

# Required. Directory where the proxy will download files to. This directory has to be readable by
# sonarr/radarr/whisparr in order to import downloads
download_directory = "/path/to/downloads"

# Optional bind address, default "0.0.0.0"
bind_address = "0.0.0.0"

# Optional TCP port, default 9091
port = 9091

# Optional log level, default "info"
loglevel = "info"

# Optional UID, default 1000. Change the owner of the downloaded files to this UID. Requires root.
uid = 1000

# Optional polling interval in secs, default 10.
polling_interval = 10

# Optional skip directories when downloading, default ["sample", "extras"]
skip_directories = ["sample", "extras"]

# Optional number of orchestration workers, default 10. Unless there are many changes coming from
# put.io, you shouldn't have to touch this number. 10 is already overkill.
orchestration_workers = 10

# Optional number of download workers, default 4. This controls how many downloads we run in parallel.
download_workers = 4

[putio]
# Required. Putio API key. You can generate one using 'csharparr get-token'
api_key = "MYPUTIOKEY"

# Both [sonarr] and [radarr] are optional, but you'll need at least one of them
[sonarr]
url = "http://mysonarrhost:8989/sonarr"
# Can be found in Settings -> General
api_key = "MYSONARRAPIKEY"

[radarr]
url = "http://myradarrhost:7878/radarr"
# Can be found in Settings -> General
api_key = "MYRADARRAPIKEY"

[whisparr]
url = "http://mywhisparrhost:6969/whisparr"
# Can be found in Settings -> General
api_key = "MYWHISPARRAPIKEY"
```

## Behavior

The proxy will:

1. Upload torrents or magnet links to put.io
2. Monitor transfers for completion
3. Download completed files to the specified download directory
4. Wait for sonarr/radarr/whisparr to import the files
5. Remove files after import and when put.io is done seeding
6. Skip directories named "Sample" or "Extras" by default

## Project Structure

```
.
├── src/
│   └── Csharparr/
│       ├── Commands/           # CLI command handlers
│       ├── Configuration/      # Configuration types and loading
│       ├── Download/           # Download orchestration
│       ├── Http/               # HTTP/Transmission RPC handlers
│       ├── Services/           # API clients (Put.io, Arr)
│       └── Program.cs          # Application entry point
├── tests/
│   └── Csharparr.Tests/        # Unit tests
├── Dockerfile
├── docker-compose.yaml
└── Csharparr.sln
```

## Dependencies

- [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/) - Web framework
- [System.CommandLine](https://github.com/dotnet/command-line-api) - CLI framework
- [Serilog](https://serilog.net/) - Structured logging
- [Tomlyn](https://github.com/xoofx/Tomlyn) - TOML parser

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running Locally

```bash
dotnet run --project src/Csharparr -- run -c path/to/config.toml
```

## Contributing

Contributions are welcome! When contributing:

1. Add tests for new features
2. Ensure `dotnet test` passes
3. Run `dotnet format` to format code
4. Update documentation as needed

## License

MIT

## Thanks

- [davidchalifoux](https://github.com/davidchalifoux) for borrowed code from kaput-cli
- [wouterdebie](https://github.com/wouterdebie) for the original Rust implementation
