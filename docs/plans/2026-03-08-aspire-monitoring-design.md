# .NET Aspire & Observability Design

## Goal

Add .NET Aspire 13 to Codec for local dev orchestration (single `dotnet run` starts everything) and full observability (distributed tracing, metrics, structured logs) across all services, both locally and in production on Azure.

## Decisions

- **Approach**: AppHost + ServiceDefaults (standard Aspire pattern)
- **Local orchestration**: Aspire manages API, Web, Postgres, Redis, Azurite. SFU runs separately.
- **Production deployment**: Keep existing Bicep IaC and CI/CD. Aspire is local-dev only; ServiceDefaults + OpenTelemetry ship in the deployed apps.
- **Telemetry backend**: Azure Monitor / Application Insights via OTLP (Aspire defaults)
- **SFU**: OpenTelemetry JS SDK for production observability, not orchestrated by Aspire

## Project Structure

```
apps/
  api/
    Codec.Api/              # Existing — gains reference to ServiceDefaults
    Codec.ServiceDefaults/  # NEW — shared OpenTelemetry + health check + resilience wiring
  aspire/
    Codec.AppHost/          # NEW — Aspire orchestrator (local dev only, not deployed)
  sfu/                      # Existing — gains OpenTelemetry JS instrumentation
  web/                      # Existing — orchestrated by AppHost via AddViteApp
```

Both new projects added to `Codec.sln`.

## Section 1: AppHost Configuration

`Codec.AppHost/AppHost.cs` orchestrates all local dev resources:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("codec_dev")
    .WithDataVolume();

var redis = builder.AddRedis("redis");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.Codec_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(blobs)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithHttpHealthCheck("/health/ready");

builder.AddViteApp("web", "../../web")
    .WithHttpEndpoint(port: 5174, env: "PORT")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

Key points:
- Replaces docker-compose for local dev. `dotnet run` from AppHost starts Postgres, Redis, Azurite, API, and Web.
- Connection strings auto-injected via Aspire service discovery.
- SvelteKit uses Vite, so `AddViteApp` gives Aspire visibility into the frontend process.
- Existing docker-compose.yml still works for anyone who doesn't want to use Aspire.

### AppHost Packages

- `Aspire.Hosting.PostgreSQL`
- `Aspire.Hosting.Redis`
- `Aspire.Hosting.Azure.Storage`
- `Aspire.Hosting.JavaScript`

## Section 2: ServiceDefaults & API Integration

### `Extensions.cs` — Two Extension Methods

**`AddServiceDefaults(this IHostApplicationBuilder builder)`** configures:
- OpenTelemetry metrics: ASP.NET Core, HTTP client, runtime
- OpenTelemetry tracing: ASP.NET Core requests, HTTP client calls, EF Core queries, SignalR
- OpenTelemetry logging: structured log export via OTLP
- OTLP exporter: sends telemetry to `OTEL_EXPORTER_OTLP_ENDPOINT` (Aspire dashboard locally, Azure Monitor in production)
- Service discovery: resolves service references from AppHost
- HTTP resilience: standard retry/timeout policies on HttpClient

**`MapDefaultEndpoints(this WebApplication app)`** maps:
- `/health/live` — liveness probe
- `/health/ready` — readiness probe (DB + Redis)
- `/alive` — simple alive check

### ServiceDefaults Packages

- `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.Runtime`
- `Microsoft.Extensions.Http.Resilience`
- `Microsoft.Extensions.ServiceDiscovery`
- `Azure.Monitor.OpenTelemetry.Exporter` (for production Azure Monitor export)

### Changes to Program.cs

- Add `builder.AddServiceDefaults()` near the top
- Add `app.MapDefaultEndpoints()` near the bottom
- Remove hand-rolled health check endpoints (`/health/live`, `/health/ready`, `WriteHealthResponse`)
- Keep Serilog (coexists with OTel logging — Serilog for console, OTel for export)
- Remove `AspNetCore.HealthChecks.Redis` package (Aspire's Redis integration handles this)

### Production Behavior

ServiceDefaults works without the AppHost. Set `OTEL_EXPORTER_OTLP_ENDPOINT` and `APPLICATIONINSIGHTS_CONNECTION_STRING` in the Container App environment. Connection strings resolve via existing appsettings config when not running through Aspire.

## Section 3: SFU Observability (Production Only)

### New npm Dependencies in `apps/sfu/`

- `@opentelemetry/sdk-node`
- `@opentelemetry/auto-instrumentations-node`
- `@opentelemetry/exporter-trace-otlp-http`
- `@opentelemetry/exporter-metrics-otlp-http`

### New File: `apps/sfu/src/instrumentation.ts`

- Initializes OpenTelemetry Node SDK
- Configures OTLP exporters for traces and metrics
- Adds Express and HTTP auto-instrumentation
- Reads `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME` from environment
- No-ops gracefully if endpoint isn't set (local dev without Aspire still works)
- Imported at top of `src/index.ts` before other imports (required for auto-instrumentation)

### Custom Spans

- Room creation/destruction
- Transport creation (WebRTC)
- Producer/consumer lifecycle

### Production Wiring

- Set `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_SERVICE_NAME=codec-sfu` in Voice VM docker-compose
- Traces correlate with API via W3C trace context propagation

## Section 4: Azure Production Telemetry

### New Bicep Module: `infra/application-insights.bicep`

- Creates Application Insights resource linked to existing Log Analytics workspace
- Outputs OTLP connection string

### Bicep Changes

**`infra/container-app-api.bicep`** — add environment variables:
- `OTEL_EXPORTER_OTLP_ENDPOINT`
- `OTEL_SERVICE_NAME=codec-api`
- `APPLICATIONINSIGHTS_CONNECTION_STRING`

**`infra/voice-vm.bicep`** — pass to SFU container:
- `OTEL_EXPORTER_OTLP_ENDPOINT`
- `OTEL_SERVICE_NAME=codec-sfu`

**`infra/main.bicep`** — add Application Insights module, wire outputs to API and Voice VM.

### Production Observability

- Application Map: visual topology of API, SFU, Postgres, Redis dependencies
- Distributed tracing: end-to-end request traces across API and SFU
- Live metrics: real-time request rates, failure rates, response times
- Log correlation: Serilog structured logs correlated with traces via trace ID
- Alerts: Azure Monitor alerts on failure rates, latency, etc.
