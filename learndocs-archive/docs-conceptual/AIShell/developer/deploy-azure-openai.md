---
title: Deploy the Azure OpenAI Service using Bicep
description: This article provides the step-by-step instructions to deploy and instance of the Azure OpenAI Service using Bicep.
ms.date: 01/12/2026
ms.topic: concept-article
ms.collection: ce-skilling-ai-copilot
---
# Deploy the Azure OpenAI Service using Bicep

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

In AIShell, the **openai-gpt** agent can be used with a public OpenAI instance or an Azure OpenAI
deployment. We recommend using the Azure OpenAI Service because it has additional features and
provides better manageability. This article provides the step-by-step instructions to deploy and
instance of the Azure OpenAI Service using Bicep.

To use AIShell with the Azure OpenAI Service, you need the following Azure resources:

- An Azure OpenAI Service account - a resource that contains multiple different model deployments.
- An Azure OpenAI Deployment - a model deployment that can be called using an API to generate
  responses.

## Prerequisites

Before you begin, ensure you have the following prerequisites:

- An active Azure subscription
- [Azure CLI][04] or [Azure PowerShell][05] installed locally or access to Azure Cloud Shell
- Proper permissions to create resources in your Azure subscription

## Steps to Deploy

These steps walk you through the following tasks:

1. Download and modify the Bicep file
1. Deploy the Azure OpenAI Service
1. Configure the agent to use the deployment

### 1. Download and modify the Bicep file

Download the [main.bicep][07] file from the AIShell repository.

You must modify the parameters at the top of the `./main.bicep` file to include your own values.
Replace the placeholders in angle brackets (`< >`) with your own values.

```bicep
@description('This is the name of your AI Service Account')
param aiserviceaccountname string = '<Insert own account name>'

@description('Custom domain name for the endpoint')
param customDomainName string = '<Insert own unique domain name>'

@description('Name of the deployment')
param modeldeploymentname string = '<Insert own deployment name>'

@description('The model being deployed')
param model string = 'gpt-4'

@description('Version of the model being deployed')
param modelversion string = 'turbo-2024-04-09'

@description('Capacity for specific model used')
param capacity int = 80

@description('Location for all resources.')
param location string = resourceGroup().location

@allowed([
  'S0'
])
param sku string = 'S0'
```

For this deployment, the Bicep file uses the following defaults:

- The location of the Azure OpenAI account is set to the location of the resource group
- The AI model is `gpt-4` version `turbo-2024-04-09`

You can change these settings for your particular needs. For more information on available models,
see [Azure OpenAI Service models][01]. You might need to modify the capacity of the deployment based
the model you use. For more information about setting the capacity, see
[Azure OpenAI Service quotas and limits][02].

### 2. Deploy the Azure OpenAI Service

After you modified the Bicep file parameters, you're ready to deploy your own Azure OpenAI
instance. You can use Azure CLI or Azure PowerShell to deploy the Bicep files.

#### Deploy using Azure CLI

Use the following Azure CLI commands to deploy the Azure OpenAI Service. The following commands are
intended to be run in a Bash session. Replace the placeholders in angle brackets (`< >`) with your
own values.

You can run the commands locally or in Azure Cloud Shell. If you run them locally, you must sign in
to your Azure account using `az login` and set the subscription using
`az account set --subscription <subscription name>`.

```azurecli-interactive
az deployment group create \
    --resource-group '<resource group name>' \
    --template-file ./main.bicep

# Get the endpoint and key of the deployment
az cognitiveservices account show \
    --name '<account name>'
    --resource-group '<resource group name>' | jq -r .properties.endpoint

az cognitiveservices account keys list \
    --name '<account name>' \
    --resource-group  '<resource group name>' | jq -r .key1
```

#### Deploy using Azure PowerShell

Use the following Azure PowerShell commands to deploy the Azure OpenAI Service. Replace the
placeholders in angle brackets (`< >`) with your own values.

You can run the commands locally or in Azure Cloud Shell. If you run them locally, you must sign in
to your Azure account using `Connect-AzAccount` and set the subscription using
`Set-AzContext -SubscriptionId <subscription id>`.

```azurepowershell-interactive
$AzResourceGroupDeploymentSplat = @{
    ResourceGroupName = '<resource group name>'
    TemplateFile = './main.bicep'
}
New-AzResourceGroupDeployment @AzResourceGroupDeploymentSplat

# Get the endpoint and key of the deployment
$AzCognitiveServicesAccountSplat = @{
    ResourceGroupName = '<resource group name>'
    Name = '<account name>'
}
Get-AzCognitiveServicesAccount @AzCognitiveServicesAccountSplat  |
    Select-Object -Property Endpoint

Get-AzCognitiveServicesAccountKey @AzCognitiveServicesAccountSplat |
    Select-Object -Property Key1
```

### 3. Configure the agent to use the deployment

Now that you have the endpoint and key for the deployment, you need to configure the **openai-gpt**
agent. The configuration is stored in a JSON file.

Use the following steps to edit the JSON configuration.

1. Start AIShell and select the `openai-gpt` agent from the list of agents.
1. At the AIShell command prompt, run the `/agent config` command. This command opens the JSON
   configuration file.
1. Replace the placeholder values in angle brackets (`< >`) in the JSON file with the endpoint and
   key values you obtained from the Azure OpenAI deployment. The following JSON shows an example of
   the configuration settings you want to update.

   ```json
   {
     // Declare GPT instances.
     "GPTs": [
         {
           "Name": "ps-az-gpt4",
           "Description": "<insert description here>",
           "Endpoint": "<insert endpoint here>",
           "Deployment": "<insert deployment name here>",
           "ModelName": "gpt-4",
           "Key": "<insert key here>",
           "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `osx` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
         }
     ],
     // Specify the default GPT instance to use for user query.
     // For example: "ps-az-gpt4"
     "Active": "ps-az-gpt4"
   }
   ```

1. Save the JSON file and close the editor.

## Conclusion

You successfully deployed the Azure OpenAI Service and configured your `openai-gpt` agent to
communicate with your deployment. For more information about model training, filters, and settings
for Azure OpenAI deployments, see [Azure OpenAI Service documentation][03].

> [!NOTE]
> We would like to thank Sebastian Jensen for his guidance on how to deploy the Azure OpenAI Service
> using Bicep files. This article was inspired by his blog post on Medium and used with permission.
> Take a moment to read his original post:
> [Deploy an Azure OpenAI service with LLM deployments via Bicep][06].

<!-- link references -->
[01]: /azure/ai-services/openai/concepts/models?tabs=global-standard%2Cstandard-chat-
[02]: /azure/ai-services/openai/quotas-limits
[03]: /azure/cognitive-services/openai/
[04]: /cli/azure/install-azure-cli
[05]: /powershell/azure/install-azure-powershell
[06]: https://medium.com/medialesson/deploy-an-azure-openai-service-with-llm-deployments-via-bicep-244411472d40
[07]: https://raw.githubusercontent.com/PowerShell/AIShell/refs/heads/main/docs/development/AzureOAIDeployment/main.bicep

