# SigNoz Example Application

This is a simple example application that can be used to demonstrate SigNoz.

## Getting Started

Install SigNoz using Docker by following the instructions on the SigNoz documentation site:

- https://signoz.io/docs/install/docker/

Verify `appsettings.json` and `nlog.config` files are configured correctly for your environment.

The application can be started with the following VS Code task: `start-signoz`

Trigger some requests to the application to generate traces, metrics and logs. This can be done with the `REST Client` VS Code extension, check the signoz.api.http file for example requests.

# OpenTelemetry Collector Configuration

Some additional configuration is required to send health check metrics to SigNoz. This involves setting up the OpenTelemetry Collector with the HTTP Check receiver.

## HTTP Check Receiver

The HTTP Check receiver performs synthetic monitoring by making HTTP requests to configured endpoints and generating metrics about their availability and performance.

### Docker Configuration

To allow the collector to access services running on the host machine, add the `extra_hosts` configuration to the otel-collector service in `docker-compose.yaml`:

```yaml
services:
  otel-collector:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

### Basic Configuration

Configure the httpcheck receiver in `otel-collector-config.yaml`:

```yaml
receivers:
  httpcheck:
    targets:
      - method: "GET"
        endpoints:
          - "https://host.docker.internal:5001/health"
        tls:
          insecure_skip_verify: true
    collection_interval: 30s
    initial_delay: 10s
    timeout: 10s
```

### Configuration Options

- **targets**: List of HTTP endpoints to monitor
  - **method**: HTTP method to use (GET, POST, etc.)
  - **endpoints**: List of URLs to check
  - **tls.insecure_skip_verify**: Skip TLS certificate verification for HTTPS endpoints
- **collection_interval**: How often to perform the health checks (default: 60s)
- **initial_delay**: Wait time before starting health checks (default: 1s)
- **timeout**: Request timeout duration (default: 10s)

### Pipeline Integration

Add the httpcheck receiver to your metrics pipeline:

```yaml
exporters:
  debug:
    verbosity: detailed
service:
  pipelines:
    metrics:
      receivers: [..., httpcheck]
      processors: [batch]
      exporters: [..., debug]
```

### Generated Metrics

The httpcheck receiver generates the following metrics:
- `httpcheck_status`: HTTP response status code
- `httpcheck_duration`: Request duration in seconds
- `httpcheck_up`: Whether the endpoint is reachable (1 = up, 0 = down)

### References

- [OpenTelemetry Collector Receivers](https://github.com/open-telemetry/opentelemetry-collector/blob/main/receiver/README.md)
- [HTTP Client Configuration](https://github.com/open-telemetry/opentelemetry-collector/tree/main/config/confighttp#client-configuration)
- [HTTP Check Receiver](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/httpcheckreceiver/README.md)
- [HTTP Check Receiver Documentation](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/httpcheckreceiver/documentation.md)