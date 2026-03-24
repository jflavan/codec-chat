/// Azure Monitor alert rules for Codec Chat infrastructure.
/// Covers: container app restarts, 5xx error rate, and PostgreSQL CPU usage.
param location string

@description('Resource ID of the action group to notify when alerts fire.')
param actionGroupId string

@description('Name of the API container app (used in log queries).')
param apiContainerAppName string

@description('Resource ID of the Log Analytics workspace (for log-based alerts).')
param logAnalyticsWorkspaceId string

@description('Resource ID of the PostgreSQL Flexible Server (for DB CPU alerts).')
param postgresqlServerId string

@description('Environment name used in alert naming.')
param environmentName string = 'prod'

// --- Container Restart Alert (log-based) ---
// Fires when the API container app has >= 3 restarts in a 15-minute window.

resource containerRestartAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-container-restarts-${environmentName}'
  location: location
  properties: {
    displayName: 'Container Restarts — ${apiContainerAppName}'
    description: 'Fires when the API container app restarts 3 or more times in 15 minutes.'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      logAnalyticsWorkspaceId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppSystemLogs_CL
            | where ContainerAppName_s == '${apiContainerAppName}'
            | where Reason_s == 'BackOff' or Reason_s == 'CrashLoopBackOff' or Log_s has 'restarted'
            | summarize RestartCount = count() by bin(TimeGenerated, 15m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 3
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupId
      ]
    }
  }
}

// --- 5xx Error Rate Alert (log-based) ---
// Fires when the API returns >= 10 server errors (5xx) in a 5-minute window.

resource http5xxAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-5xx-rate-${environmentName}'
  location: location
  properties: {
    displayName: '5xx Error Rate — ${apiContainerAppName}'
    description: 'Fires when the API returns 10 or more 5xx responses in 5 minutes.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [
      logAnalyticsWorkspaceId
    ]
    criteria: {
      allOf: [
        {
          query: '''
            ContainerAppConsoleLogs_CL
            | where ContainerAppName_s == '${apiContainerAppName}'
            | where Log_s has 'HTTP' and Log_s has_any ('500', '501', '502', '503', '504')
            | summarize ErrorCount = count() by bin(TimeGenerated, 5m)
          '''
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: 10
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupId
      ]
    }
  }
}

// --- PostgreSQL CPU Alert (metric-based) ---
// Fires when average DB CPU exceeds 80% for 10 minutes.

resource dbCpuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-db-cpu-${environmentName}'
  location: 'global'
  properties: {
    description: 'Fires when PostgreSQL average CPU exceeds 80% over 10 minutes.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [
      postgresqlServerId
    ]
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HighCpu'
          metricName: 'cpu_percent'
          metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
  }
}
