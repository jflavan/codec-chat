# .NET Aspire & Observability Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add .NET Aspire 13 for local dev orchestration and OpenTelemetry-based observability across the API and SFU, with Azure Monitor export in production.

**Architecture:** Two new .NET projects (AppHost for local orchestration, ServiceDefaults for shared OTel/health/resilience wiring). The API references ServiceDefaults; the AppHost orchestrates all local dev services. The SFU gets standalone OpenTelemetry JS instrumentation. A new Application Insights Bicep module completes the production telemetry pipeline.

**Tech Stack:** .NET Aspire 13, OpenTelemetry (.NET + Node.js), Azure Monitor, Application Insights, Bicep

---

### Task 1: Create Codec.ServiceDefaults Project

This is the shared library that wires OpenTelemetry, health checks, service discovery, and HTTP resilience into any .NET service. It works both locally (with Aspire dashboard) and in production (with Azure Monitor).

**Files:**
- Create: `apps/api/Codec.ServiceDefaults/Codec.ServiceDefaults.csproj`
- Create: `apps/api/Codec.ServiceDefaults/Extensions.cs`
- Modify: `Codec.sln` (add project)

**Step 1: Create the project directory**

```bash
mkdir -p apps/api/Codec.ServiceDefaults
```

**Step 2: Create the .csproj file**

Create `apps/api/Codec.ServiceDefaults/Codec.ServiceDefaults.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.4.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="10.0.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />
  </ItemGroup>

</Project>
```

> **Note on package versions:** The versions above are placeholders. When implementing, run `dotnet add package <name>` to get the latest compatible versions, or check NuGet. The Aspire 13 packages should resolve to 13.x versions for the `Microsoft.Extensions.*` packages. OpenTelemetry packages should be the latest stable (1.12.x or newer). `Azure.Monitor.OpenTelemetry.Exporter` should be the latest stable (1.4.x or newer).

**Step 3: Create Extensions.cs**

Create `apps/api/Codec.ServiceDefaults/Extensions.cs`:

```csharp
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codec.ServiceDefaults;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness probe: always 200 — proves the process is running.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        // Readiness probe: includes checks tagged "ready" (DB, Redis, etc.).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        // Simple alive check.
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        return app;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlp = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddAzureMonitorMetricExporter(o =>
                    o.ConnectionString = appInsightsConnectionString))
                .WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(o =>
                    o.ConnectionString = appInsightsConnectionString));
        }

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }
}
```

**Step 4: Add the project to the solution**

```bash
cd /path/to/repo
dotnet sln Codec.sln add apps/api/Codec.ServiceDefaults/Codec.ServiceDefaults.csproj --solution-folder "apps/api"
```

**Step 5: Verify it builds**

```bash
dotnet build apps/api/Codec.ServiceDefaults/Codec.ServiceDefaults.csproj
```

Expected: Build succeeded.

**Step 6: Commit**

```bash
git add apps/api/Codec.ServiceDefaults/ Codec.sln
git commit -m "feat: add Codec.ServiceDefaults project with OpenTelemetry and health checks"
```

---

### Task 2: Integrate ServiceDefaults into the API

Wire the API to use ServiceDefaults, replacing hand-rolled health checks.

**Files:**
- Modify: `apps/api/Codec.Api/Codec.Api.csproj` (add ProjectReference, remove `AspNetCore.HealthChecks.Redis`)
- Modify: `apps/api/Codec.Api/Program.cs` (add `AddServiceDefaults`/`MapDefaultEndpoints`, remove old health endpoints)

**Step 1: Add project reference and remove old health check package**

```bash
cd apps/api/Codec.Api
dotnet add reference ../Codec.ServiceDefaults/Codec.ServiceDefaults.csproj
dotnet remove package AspNetCore.HealthChecks.Redis
```

Also remove `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` from the API .csproj — the DB health check registration stays in Program.cs but uses the base health checks package (already included transitively via ServiceDefaults).

```bash
dotnet remove package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
```

> **Check:** After removing these, verify the API still has access to `AddDbContextCheck` via the ServiceDefaults transitive dependency. If not, add `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` back to the API .csproj.

**Step 2: Modify Program.cs — add ServiceDefaults**

Near the top of `Program.cs`, right after `var builder = WebApplication.CreateBuilder(args);`, add:

```csharp
builder.AddServiceDefaults();
```

**Step 3: Modify Program.cs — remove old health check setup**

Remove these lines (around lines 141-148):

```csharp
var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<CodecDbContext>("database", tags: ["ready"]);

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    healthChecks.AddRedis(redisConnectionString, name: "redis", tags: ["ready"],
        failureStatus: HealthStatus.Degraded);
}
```

Replace with:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CodecDbContext>("database", tags: ["ready"]);
```

> **Note:** Redis health checks are removed because when running through Aspire, the Redis resource handles its own health. In production, the readiness probe at `/health/ready` still checks the DB. If you want Redis health checks in production too, keep the Redis health check but use the connection string from config.

**Step 4: Modify Program.cs — remove old health endpoints and WriteHealthResponse**

Remove the following (around lines 379-408):

```csharp
// Liveness probe: always 200 — proves the process is running.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthResponse
});

// Readiness probe: includes DB connectivity check.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var result = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    };

    return context.Response.WriteAsJsonAsync(result);
}
```

Replace with:

```csharp
app.MapDefaultEndpoints();
```

Place this right before `app.Run();`.

**Step 5: Remove unused usings from Program.cs**

Remove these `using` statements if they become unused:
- `using Microsoft.AspNetCore.Diagnostics.HealthChecks;`
- `using Microsoft.Extensions.Diagnostics.HealthChecks;`

**Step 6: Verify it builds**

```bash
dotnet build apps/api/Codec.Api/Codec.Api.csproj
```

Expected: Build succeeded with no errors.

**Step 7: Commit**

```bash
git add apps/api/Codec.Api/ apps/api/Codec.ServiceDefaults/
git commit -m "feat: integrate ServiceDefaults into API, replace hand-rolled health checks"
```

---

### Task 3: Create Codec.AppHost Project

The Aspire orchestrator that starts all local dev services with a single `dotnet run`.

**Files:**
- Create: `apps/aspire/Codec.AppHost/Codec.AppHost.csproj`
- Create: `apps/aspire/Codec.AppHost/AppHost.cs`
- Create: `apps/aspire/Codec.AppHost/Properties/launchSettings.json`
- Modify: `Codec.sln` (add project)

**Step 1: Create the project directory**

```bash
mkdir -p apps/aspire/Codec.AppHost/Properties
```

**Step 2: Create the .csproj file**

Create `apps/aspire/Codec.AppHost/Codec.AppHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Sdk Name="Aspire.AppHost.Sdk" Version="13.1.1" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="13.1.1" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" Version="13.1.1" />
    <PackageReference Include="Aspire.Hosting.Redis" Version="13.1.1" />
    <PackageReference Include="Aspire.Hosting.Azure.Storage" Version="13.1.1" />
    <PackageReference Include="Aspire.Hosting.JavaScript" Version="13.1.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../api/Codec.Api/Codec.Api.csproj" />
  </ItemGroup>

</Project>
```

> **Note on versions:** Use `dotnet add package <name>` to fetch the latest Aspire 13.x versions. The `Aspire.AppHost.Sdk` version in the `<Sdk>` element must match the hosting packages. Check NuGet for the exact latest 13.1.x version.

**Step 3: Create AppHost.cs**

Create `apps/aspire/Codec.AppHost/AppHost.cs`:

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

**Step 4: Create launchSettings.json**

Create `apps/aspire/Codec.AppHost/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17222;http://localhost:15222",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "DOTNET_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21222",
        "DOTNET_RESOURCE_SERVICE_ENDPOINT_URL": "https://localhost:22222"
      }
    }
  }
}
```

**Step 5: Add the project to the solution**

```bash
dotnet sln Codec.sln add apps/aspire/Codec.AppHost/Codec.AppHost.csproj --solution-folder "apps/aspire"
```

**Step 6: Verify it builds**

```bash
dotnet build apps/aspire/Codec.AppHost/Codec.AppHost.csproj
```

Expected: Build succeeded. (It won't *run* without Docker, but it should compile.)

**Step 7: Commit**

```bash
git add apps/aspire/Codec.AppHost/ Codec.sln
git commit -m "feat: add Codec.AppHost Aspire orchestrator for local dev"
```

---

### Task 4: Verify Aspire Local Dev Works End-to-End

Smoke test the full Aspire orchestration locally.

**Prerequisites:** Docker Desktop running.

**Step 1: Start the AppHost**

```bash
cd apps/aspire/Codec.AppHost
dotnet run
```

Expected: The Aspire dashboard opens in the browser. You should see resources for `postgres`, `redis`, `storage`, `api`, and `web` in the dashboard.

**Step 2: Verify all resources start**

In the Aspire dashboard:
- `postgres` — Running (green)
- `redis` — Running (green)
- `storage` (Azurite emulator) — Running (green)
- `api` — Running (green), health check passing
- `web` — Running (green)

**Step 3: Verify the API responds**

Open the API URL shown in the dashboard (or click the endpoint link). Hit `/health/ready`.

Expected: `200 OK` with health check JSON.

**Step 4: Verify the Web frontend loads**

Open the Web URL shown in the dashboard.

Expected: The Codec chat login page renders.

**Step 5: Check telemetry in the dashboard**

Click on "Traces" in the Aspire dashboard. Make a few requests to the API.

Expected: You see traces with spans for ASP.NET Core request handling, EF Core queries, etc.

**Step 6: Fix any issues discovered**

Common issues:
- **Connection string format mismatch**: Aspire injects connection strings in a specific format. If the API's `Program.cs` reads `builder.Configuration.GetConnectionString("Default")` but Aspire names the DB resource differently, you may need to align the names. The Postgres database resource name `"codec_dev"` should map to `ConnectionStrings__codec_dev` in the environment. Check whether the API needs to read `GetConnectionString("codec_dev")` when running under Aspire vs `GetConnectionString("Default")` in production. You may need to use `WithEnvironment("ConnectionStrings__Default", postgres)` or rename the resource.
- **Google:ClientId required**: The API throws if `Google:ClientId` is missing. Add it to the AppHost via `.WithEnvironment("Google__ClientId", builder.Configuration["Google:ClientId"])` or use an `appsettings.Development.json` in the AppHost project.
- **CORS**: The web frontend URL may differ from `localhost:5174` under Aspire. Add the Aspire-assigned URL to CORS allowed origins.

**Step 7: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve Aspire local dev integration issues"
```

---

### Task 5: Add OpenTelemetry to the SFU

Instrument the Node.js SFU with OpenTelemetry for production observability.

**Files:**
- Modify: `apps/sfu/package.json` (add OTel dependencies)
- Create: `apps/sfu/src/instrumentation.ts`
- Modify: `apps/sfu/src/index.ts` (import instrumentation first)
- Modify: `apps/sfu/src/rooms.ts` (add custom spans)

**Step 1: Install OpenTelemetry packages**

```bash
cd apps/sfu
npm install @opentelemetry/sdk-node @opentelemetry/auto-instrumentations-node @opentelemetry/exporter-trace-otlp-http @opentelemetry/exporter-metrics-otlp-http @opentelemetry/api
```

**Step 2: Create instrumentation.ts**

Create `apps/sfu/src/instrumentation.ts`:

```typescript
import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';

const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;

if (otlpEndpoint) {
  const sdk = new NodeSDK({
    traceExporter: new OTLPTraceExporter({ url: `${otlpEndpoint}/v1/traces` }),
    metricReader: new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter({ url: `${otlpEndpoint}/v1/metrics` }),
      exportIntervalMillis: 30_000,
    }),
    instrumentations: [
      getNodeAutoInstrumentations({
        '@opentelemetry/instrumentation-fs': { enabled: false },
      }),
    ],
  });

  sdk.start();
  console.log(`OpenTelemetry initialized — exporting to ${otlpEndpoint}`);

  process.on('SIGTERM', () => sdk.shutdown());
} else {
  console.log('OpenTelemetry disabled — OTEL_EXPORTER_OTLP_ENDPOINT not set');
}
```

> **Note:** `@opentelemetry/sdk-metrics` is a transitive dependency of `@opentelemetry/sdk-node`. If it doesn't resolve, install it explicitly: `npm install @opentelemetry/sdk-metrics`.

**Step 3: Import instrumentation at the top of index.ts**

Modify `apps/sfu/src/index.ts` — add this as the **very first line**, before all other imports:

```typescript
import './instrumentation.js';
```

This must be the first import so auto-instrumentation can monkey-patch Express and HTTP before they're loaded.

**Step 4: Add custom spans to rooms.ts**

Modify `apps/sfu/src/rooms.ts` — add import at the top:

```typescript
import { trace } from '@opentelemetry/api';

const tracer = trace.getTracer('codec-sfu');
```

Then wrap key operations with custom spans. For example, in the `POST /rooms/:roomId` handler, wrap the router creation:

```typescript
router.post('/rooms/:roomId', async (req, res) => {
  const { roomId } = req.params;
  let room = rooms.get(roomId);
  if (!room) {
    await tracer.startActiveSpan('sfu.room.create', async (span) => {
      span.setAttribute('room.id', roomId);
      const sfuRouter = await worker.createRouter({ mediaCodecs: MEDIA_CODECS });
      room = { router: sfuRouter, participants: new Map() };
      rooms.set(roomId, room);
      span.end();
    });
  }
  res.json({ routerRtpCapabilities: room!.router.rtpCapabilities });
});
```

Apply similar patterns to:
- `DELETE /rooms/:roomId` — span `sfu.room.destroy`
- `POST /rooms/:roomId/transports` — span `sfu.transport.create`
- `POST /rooms/:roomId/transports/:transportId/produce` — span `sfu.producer.create`
- `POST /rooms/:roomId/consumers` — span `sfu.consumer.create`

**Step 5: Verify it builds**

```bash
cd apps/sfu
npx tsc --noEmit
```

Expected: No TypeScript errors.

**Step 6: Verify graceful no-op locally**

```bash
cd apps/sfu
npx tsx src/index.ts
```

Expected: Output includes `OpenTelemetry disabled — OTEL_EXPORTER_OTLP_ENDPOINT not set` and the SFU starts normally.

**Step 7: Commit**

```bash
git add apps/sfu/
git commit -m "feat: add OpenTelemetry instrumentation to SFU"
```

---

### Task 6: Add OTel Environment Variables to Voice VM Docker Compose

Wire the SFU's OpenTelemetry exporter in the production docker-compose template.

**Files:**
- Modify: `infra/voice/docker-compose.yml`

**Step 1: Add OTel environment variables to the SFU service**

In `infra/voice/docker-compose.yml`, add to the `sfu` service's `environment` block:

```yaml
      OTEL_EXPORTER_OTLP_ENDPOINT: "${OTEL_EXPORTER_OTLP_ENDPOINT}"
      OTEL_SERVICE_NAME: "codec-sfu"
```

These go after the existing `SFU_INTERNAL_KEY` line.

> **Note:** `OTEL_EXPORTER_OTLP_ENDPOINT` will be substituted by CI/CD alongside the other `${...}` variables. When not set, the SFU's instrumentation gracefully no-ops.

**Step 2: Commit**

```bash
git add infra/voice/docker-compose.yml
git commit -m "feat: add OTel env vars to SFU docker-compose template"
```

---

### Task 7: Create Application Insights Bicep Module

Add the Azure Application Insights resource for production telemetry collection.

**Files:**
- Create: `infra/modules/application-insights.bicep`
- Modify: `infra/main.bicep`
- Modify: `infra/modules/container-app-api.bicep`

**Step 1: Create application-insights.bicep**

Create `infra/modules/application-insights.bicep`:

```bicep
/// Application Insights for distributed tracing and metrics collection.
param name string
param location string
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    IngestionMode: 'LogAnalytics'
  }
}

output connectionString string = appInsights.properties.ConnectionString
output instrumentationKey string = appInsights.properties.InstrumentationKey
```

**Step 2: Add Application Insights module to main.bicep**

In `infra/main.bicep`, add a naming variable after the existing naming section (around line 79):

```bicep
var appInsightsName = 'appi-${baseName}'
```

Add the module after the `logAnalytics` module (around line 93):

```bicep
module appInsights 'modules/application-insights.bicep' = {
  name: 'application-insights'
  params: {
    name: appInsightsName
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}
```

**Step 3: Add Application Insights connection string to the API Container App**

In `infra/main.bicep`, add a new parameter to the `apiApp` module invocation:

```bicep
    appInsightsConnectionString: appInsights.outputs.connectionString
```

In `infra/modules/container-app-api.bicep`, add the parameter:

```bicep
@description('Application Insights connection string for OpenTelemetry export. Leave empty to disable.')
param appInsightsConnectionString string = ''
```

Add OTel environment variables to the container's `env` array (in the `concat` call, add a new conditional block):

```bicep
], appInsightsConnectionString != '' ? [
  {
    name: 'OTEL_SERVICE_NAME'
    value: 'codec-api'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
] : [])
```

**Step 4: Add Application Insights output to main.bicep**

Add at the end of `infra/main.bicep`:

```bicep
output appInsightsConnectionString string = appInsights.outputs.connectionString
```

**Step 5: Verify Bicep compiles**

```bash
az bicep build --file infra/main.bicep
```

Expected: No errors. (If `az` CLI is not installed, skip this — CI will validate.)

**Step 6: Commit**

```bash
git add infra/
git commit -m "feat: add Application Insights Bicep module and wire to API container app"
```

---

### Task 8: Update Documentation

Update project documentation to reflect the new Aspire setup.

**Files:**
- Modify: `CLAUDE.md` (add Aspire dev commands, update architecture section)
- Modify: `apps/web/.env.example` (no changes needed — Web env is unchanged)

**Step 1: Update CLAUDE.md development commands**

Add a new section after the existing "Start PostgreSQL" section:

```markdown
### Start with Aspire (recommended)
```bash
cd apps/aspire/Codec.AppHost
dotnet run          # Starts Postgres, Redis, Azurite, API, and Web — dashboard at https://localhost:17222
```

### Start without Aspire (alternative)
```bash
docker compose up -d postgres azurite
# Then start API and Web separately as below
```
```

**Step 2: Update architecture section**

Add to the repository layout:

```
  aspire/
    Codec.AppHost/   # Aspire orchestrator (local dev only)
  api/
    Codec.ServiceDefaults/  # Shared OpenTelemetry + health + resilience
```

**Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add Aspire development instructions to CLAUDE.md"
```
