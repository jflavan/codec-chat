import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';

const appInsightsCs = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;
const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
async function createSdk(): Promise<NodeSDK | null> {
  const serviceName = process.env.OTEL_SERVICE_NAME ?? 'codec-sfu';
  const instrumentations = [
    getNodeAutoInstrumentations({
      '@opentelemetry/instrumentation-fs': { enabled: false },
    }),
  ];

  // Prefer Azure Monitor when connection string is available (production).
  if (appInsightsCs) {
    const { AzureMonitorTraceExporter, AzureMonitorMetricExporter } = await import('@azure/monitor-opentelemetry-exporter');
    return new NodeSDK({
      serviceName,
      // The Azure Monitor exporter bundles its own OTel types which may differ
      // from the versions installed at the top level. The NodeSDK constructor
      // accepts the structural shape at runtime even when TS versions diverge.
      traceExporter: new AzureMonitorTraceExporter({ connectionString: appInsightsCs }) as any,
      metricReader: new PeriodicExportingMetricReader({
        exporter: new AzureMonitorMetricExporter({ connectionString: appInsightsCs }) as any,
        exportIntervalMillis: 30_000,
      }),
      instrumentations,
    });
  }

  // Fall back to generic OTLP (local Aspire dashboard).
  if (otlpEndpoint) {
    return new NodeSDK({
      serviceName,
      traceExporter: new OTLPTraceExporter({ url: `${otlpEndpoint}/v1/traces` }),
      metricReader: new PeriodicExportingMetricReader({
        exporter: new OTLPMetricExporter({ url: `${otlpEndpoint}/v1/metrics` }),
        exportIntervalMillis: 30_000,
      }),
      instrumentations,
    });
  }

  return null;
}

const sdk = await createSdk();

if (sdk) {
  sdk.start();
  const target = appInsightsCs ? 'Azure Monitor' : otlpEndpoint;
  console.log(`OpenTelemetry initialized — exporting to ${target}`);

  process.on('SIGTERM', () => sdk.shutdown());
} else {
  console.log('OpenTelemetry disabled — no APPLICATIONINSIGHTS_CONNECTION_STRING or OTEL_EXPORTER_OTLP_ENDPOINT set');
}
