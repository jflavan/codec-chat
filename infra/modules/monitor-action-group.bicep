/// Azure Monitor action group for alert notifications.
param name string
param location string = 'global'

@description('Short name for the action group (max 12 characters, shown in SMS/email).')
@maxLength(12)
param shortName string = 'CodecAlerts'

@description('Email address to receive alert notifications. Leave empty to skip email receiver.')
param alertEmailAddress string = ''

@description('Name label for the email receiver.')
param alertEmailName string = 'Codec Admin'

resource actionGroup 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: name
  location: location
  properties: {
    groupShortName: shortName
    enabled: true
    emailReceivers: alertEmailAddress != '' ? [
      {
        name: alertEmailName
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ] : []
  }
}

output id string = actionGroup.id
