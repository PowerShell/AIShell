# Deploying Azure OpenAI Service via Bicep 

In order to use the `openai-gpt` agent you will either need a public OpenAI API key or an Azure
OpenAI deployment. Due to its additional features and manageability, we recommend using the Azure
OpenAI Service. This document provides easy step-by-step instructions on how to deploy the Azure
OpenAI Service using Bicep files.

There are two things needed to begin a chat experience with Azure OpenAI Service, an Azure OpenAI
Service account and an Azure OpenAI Deployment. The Azure OpenAI Service account is a resource that
contains multiple different model deployments. The Azure OpenAI Deployment is a model deployment
that can be called via an API to generate responses.

## Prerequisites

Before you begin, ensure you have the following:

- An active Azure subscription
- Azure CLI or Azure PowerShell installed
- Proper permissions to create resources in your Azure subscription

## Steps to Deploy

### 1. Getting and modifying the Bicep file

Clone the repository and navigate to the `./docs/development/AzureOAIDeployment` directory:

```sh
git clone www.github.com/PowerShell/AIShell
cd AIShell/docs/development/AzureOAIDeployment
```

You will need to modify the `./main.bicep` file to include your own values. You will have to modify
the parameters at the top of the file.

```bicep
@description('This is the name of your AI Service Account')
param aiserviceaccountname string = '<Insert own account name>'

@description('Custom domain name for the endpoint')
param customDomainName string = '<Insert own unique domain name>'

@description('Name of the deployment')
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
```

> **Note:** The `customDomainName` parameter is not a full domain URL. It's a unique subdomain prefix
> that Azure will use to create your OpenAI endpoint. For example, if you set `customDomainName` to
> `mycompany-openai`, your endpoint will be `https://mycompany-openai.openai.azure.com/`. This name
> must be globally unique across all Azure OpenAI services.

The above is defaulted to use your resource groups location as the location for the account and
`gpt-4o` version `2024-11-20`. You can modify this based on the particular model you feel best
fits your needs. You can find more information on available models at
[Azure OpenAI Service models][03]. Additionally, you may need to modify the capacity of the
deployment based on what model you use, you can find more information at
[Azure OpenAI Service quotas and limits][04].

### 2. Deploy the Azure OpenAI Service

Now that you have modified the bicep files parameters, you are ready to deploy your own Azure OpenAI
instance! Simply use either Azure CLI or Azure PowerShell to deploy the bicep files.

#### Using Azure CLI

```sh
az deployment group create --resource-group <resource group name> --template-file ./main.bicep 

// Get the endpoint and key of the deployment
az cognitiveservices account show --name <account name> --resource-group <resource group name> --query "properties.endpoint" -o tsv

az cognitiveservices account keys list --name <account name> --resource-group <resource group name> --query "key1" -o tsv
```

#### Using Azure PowerShell

```powershell
New-AzResourceGroupDeployment -ResourceGroupName <resource group name> -TemplateFile ./main.bicep

// Get the endpoint and key of the deployment
Get-AzCognitiveServicesAccount -ResourceGroupName <resource group name> -Name <account name>  | Select-Object -Property Endpoint

Get-AzCognitiveServicesAccountKey -ResourceGroupName <resource group name> -Name <account name> | Select-Object -Property Key1
```

### 3. Configuring the agent to use the deployment

Now that you have the endpoint and key of the deployment, you can open up the `openai-gpt` agent and
run `/agent config` to edit the json configuration file with all the details of the deployment. The
example below shows the default system prompt and the fields that need to be updated.

```jsonc
{
  // Declare GPT instances.
  "GPTs": [
      {
        "Name": "ps-az-gpt4o",
        "Description": "<insert description here>",
        "Endpoint": "<insert endpoint here>",
        "Deployment": "<insert deployment name here>",
        "ModelName": "gpt-4o",  
        "Key": "<insert key here>", 
        "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `osx` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
      }
  ],
  // Specify the default GPT instance to use for user query.
  // For example: "ps-az-gpt4"
  "Active": "ps-az-gpt4o"
}
```

## Conclusion

You have successfully deployed the Azure OpenAI Service and configured your `openai-gpt` agent to
communicate with it! If you would like to go further in the model training, filters and settings you
can find more information about Azure OpenAI deployments at
[Azure OpenAI Service documentation][02].

A big thank you to Sebastian Jensen's medium article,
[Deploy an Azure OpenAI service with LLM deployments via Bicep][01] for inspiring the Bicep code and
guidance on how to deploy the Azure OpenAI Service using Bicep files. Please check out his blog for
more great AI content!

[01]: https://medium.com/medialesson/deploy-an-azure-openai-service-with-llm-deployments-via-bicep-244411472d40
[02]: https://docs.microsoft.com/azure/cognitive-services/openai/
[03]: https://learn.microsoft.com/azure/ai-services/openai/concepts/models?tabs=global-standard%2Cstandard-chat-
[04]: https://learn.microsoft.com/azure/ai-services/openai/quotas-limits