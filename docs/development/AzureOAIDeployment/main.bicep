@description('This is the name of your AI Service Account')
param aiserviceaccountname string = '<Insert own account name>'

@description('Custom domain name for the endpoint')
param customDomainName string = '<Insert own unique domain name>'

@description('Name of the deployment ')
param modeldeploymentname string = '<Insert own deployment name>'

@description('The model being deployed')
param model string = 'gpt-4o'

@description('Version of the model being deployed')
param modelversion string = '2024-11-20'

@description('Capacity for specific model used')
param capacity int = 80

@description('Location for all resources.')
param location string = resourceGroup().location

@allowed([
  'S0'
])
param sku string = 'S0'

resource openAIService 'Microsoft.CognitiveServices/accounts@2025-10-01-preview' = {
  name: aiserviceaccountname
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: sku
  }
  kind: 'AIServices'
  properties: {
    customSubDomainName: customDomainName
  }
}

resource azopenaideployment 'Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview' = {
    parent: openAIService
    name: modeldeploymentname
    properties: {
        model: {
            format: 'OpenAI'
            name: model
            version: modelversion
        }
    }
    sku: {
      name: 'Standard'
      capacity: capacity
    }
}

output openAIServiceEndpoint string = openAIService.properties.endpoint
