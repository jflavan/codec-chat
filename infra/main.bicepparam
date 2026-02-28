using './main.bicep'

param location = 'centralus'
param environmentName = 'prod'
param webCustomDomain = 'codec-chat.com'
param apiCustomDomain = 'api.codec-chat.com'
param googleClientId = readEnvironmentVariable('GOOGLE_CLIENT_ID', '')
param postgresqlAdminPassword = readEnvironmentVariable('POSTGRESQL_ADMIN_PASSWORD', '')
param globalAdminEmail = readEnvironmentVariable('GLOBAL_ADMIN_EMAIL', '')
