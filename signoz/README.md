# SigNoz Sample Application

This sample application can be used to demonstrate sending OpenTelemetry data to SigNoz.

## Getting Started

Install SigNoz using Docker by following the instructions on the SigNoz documentation site:

- https://signoz.io/docs/install/docker/

Verify application's `appsettings.json` and `nlog.config` files are configured correctly for your environment.

The application can be started with the following VS Code task: `start-signoz`

Trigger some requests to the application to generate traces, metrics and logs. This can be done with the `REST Client` VS Code extension, check the signoz.api.http file for example requests.

## OpenTelemetry Collector Configuration

Some additional configuration is required to send health check metrics to SigNoz. This involves setting up the OpenTelemetry Collector with the HTTP Check receiver. The HTTP Check receiver performs synthetic monitoring by making HTTP requests to configured endpoints and generating metrics about their availability and performance.

To allow the collector to access services running on the host machine, add the `extra_hosts` configuration to the otel-collector service in `docker-compose.yaml`:

```yaml
services:
  otel-collector:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

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

## References

- [OpenTelemetry Collector Receivers](https://github.com/open-telemetry/opentelemetry-collector/blob/main/receiver/README.md)
- [HTTP Client Configuration](https://github.com/open-telemetry/opentelemetry-collector/tree/main/config/confighttp#client-configuration)
- [HTTP Check Receiver](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/httpcheckreceiver/README.md)
- [HTTP Check Receiver Documentation](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/httpcheckreceiver/documentation.md)