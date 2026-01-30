---
title: OpenAI agent
description: Learn how to configure the OpenAI agent.
ms.date: 01/12/2026
ms.topic: how-to
ms.collection: ce-skilling-ai-copilot
---
# OpenAI agent

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

This agent provides a user-friendly platform for interacting with OpenAI services. The OpenAI agent
can connect to a public OpenAI service, a private deployment of the Azure OpenAI service, or any
other OpenAI-compatible service. We recommend using an Azure OpenAI deployment for enhanced security
and privacy.

## Prerequisites

Before you can use the agent, you must configure at least one GPT instance in the agent
configuration file. Collect the following information about your OpenAI implementation:

- `ModelName` - the name of the AI model to use
- `Key` - the API key used to authenticate requests to the OpenAI service
- `Endpoint` - required for Azure OpenAI and other 3rd-party services
- `Deployment` - the name of your Azure OpenAI deployment

You also need to give your GPT instance a `Name`, a `Description`, and a `SystemPrompt`.

- `Name` - identifies the GPT instance in the agent configuration
- `Description` - provides additional context about its purpose and capabilities
- `SystemPrompt` - defines the behavior and personality of the GPT instance

You can customize these values to fit your needs.

## Configuration

GPTs are tailored versions of base OpenAI models. You use a GPT to provide focused responses based
on the system prompt you give the model. By changing the system prompt and model, you can create
multiple GPTs for the same endpoint that are tailored for specific domains or scenarios.

- Azure OpenAI requires the `Endpoint`, `Deployment`,`ModelName` and `Key` or `AuthType`.
- Public OpenAI only requires the `ModelName` and `Key`. The endpoint is fixed by OpenAI. There is
  no deployment name.
- Other OpenAI-compatible services typically require the `Endpoint`, `ModelName`, and `Key`.

GPTs are configured in the agent's settings file. To open the configuration file using your default
editor, use the `/agent config openai-gpt` command.

Update the file based on the following example:

```jsonc
{
  // Declare GPT instances.
  "GPTs": [
    // To use the Azure OpenAI service:
    // - Set `Endpoint` to the endpoint of your Azure OpenAI service,
    //   or the endpoint to the Azure API Management service if you are using it as a gateway.
    // - Set `Deployment` to the deployment name of your Azure OpenAI service.
    // - Set `ModelName` to the name of the model used for your deployment, e.g. "gpt-4-0613".
    // - Set `Key` to the access key of your Azure OpenAI service,
    //   or the key of the Azure API Management service if you are using it as a gateway.
    {
      "Name": "ps-az-gpt4",
      "Description": "A GPT instance with expertise in PowerShell scripting and command line utilities. Use gpt-4 running in Azure.",
      "Endpoint": "<insert your Azure OpenAI endpoint>",
      "Deployment": "<insert your deployment name>",
      "ModelName": "<insert the model name>",   // required field to infer properties of the service, such as token limit.
      "Key": "<insert your key>",
      "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `Windows 11` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
    },

    // To use the public OpenAI service:
    // - Ignore the `Endpoint` and `Deployment` keys.
    // - Set `ModelName` to the name of the model to be used.
    // - Set `Key` to be the OpenAI access token.
    // For example:
    {
        "Name": "ps-gpt4o",
        "Description": "A GPT instance with expertise in PowerShell scripting and command line utilities. Use gpt-4o running in OpenAI.",
        "ModelName": "gpt-4o",
        "Key": "<insert your key>",
        "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `Windows 11` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
    }
  ],

  // Specify the default GPT instance to use for user query.
  "Active": "ps-az-gpt4"
}
```

## `/gpt` Command

Use the `/gpt` command to list and select the GPT you want to use.

- Run `/gpt use <gpt-name>` to switch to another GPT instance, or run `/gpt use` to choose from the
  available ones.
- Run `/gpt list <gpt-name>` to view the details of a GPT definition, or run `/gpt list` to list all
  available GPTs.

## Support for Microsoft Entra ID authentication

The OpenAI agent support Entra ID authentication for Azure OpenAI instances. Instead of providing a
key, set the `AuthType` property to `EntraID`. This way, the agent can access your Azure OpenAI
resource without storing keys in the configuration file.

```json
{
  // Declare GPT instances.
  "GPTs": [
      // Declaration of an Azure OpenAI instance with EntraID authentication
      {
        "Name": "ps-az-entraId",
        "Description": "A GPT instance with expertise in PowerShell scripting using Entra ID authentication.",
        "Endpoint": "<Your Endpoint>",
        "Deployment": "<Your Deployment Name>",
        "ModelName": "<Your Model Name>",
        "AuthType": "EntraID",
        "SystemPrompt": "You are a helpful and friendly assistant with expertise in PowerShell scripting and command line."
      }
  ],

  // Specify the default GPT instance to use for user query.
  "Active": "ps-az-entraId"
}
```

Azure OpenAI uses the following hierarchy of credentials for authentication:

- `EnvironmentCredential`
- `WorkloadIdentityCredential`
- `ManagedIdentityCredential`
- `SharedTokenCacheCredential`
- `VisualStudioCredential`
- `AzureCliCredential`
- `AzurePowerShellCredential`
- `AzureDeveloperCliCredential`
- `InteractiveBrowserCredential`

For more information about these credentials, see .NET documentation for
[`DefaultAzureCredential`][09].

## Support for other OpenAI-compatible models

The OpenAI agent supports third-party AI services that implement the OpenAI API specifications. Some
of these models are open source tools for running SLMs and LLMs locally. The OpenAI agent supports
the following 3rd-party models:

- [**Ollama**][08]
- [**LM Studio**][06]
- [**Deepseek**][04]
- [**LocalAI**][07]
- [**Google Gemini**][03]
- [**Grok**][05]
- [**Foundry Local**][02]

Foundry Local is an on-device AI inference solution from Microsoft, currently in public preview. AI
Shell interfaces with it using the OpenAI agent. You must install and configure Foundry Local on
your machine before you can use it with AI Shell. For more information, see
[Get started with Foundry Local][01].

The OpenAI agent supports the following model names:

- `o1`
- `o3`
- `o4-mini`
- `gpt-5`
- `gpt-4.1`
- `gpt-4o`
- `gpt-4`
- `gpt-4-32k`
- `gpt-4-turbo`
- `gpt-3.5-turbo`
- `gpt-35-turbo` - Azure OpenAI name of the model
- Any of the model IDs supported by Foundry Local

For more information about endpoints and model names, see the 3rd-party documentation for the AI
service you want to use.

### Configure a Foundry Local endpoint

After you have Foundry Local installed, run the following commands to get the information you need
to configure the OpenAI agent:

```powershell
PS> foundry service start
🟢 Service is already running on http://127.0.0.1:56952/.

PS> foundry model load phi-3.5-mini
🕔 Loading model...
🟢 Model phi-3.5-mini loaded successfully

PS> foundry service ps
Models running in service:
    Alias                          Model ID
🟢  phi-3.5-mini                   Phi-3.5-mini-instruct-generic-cpu
```

This example starts the Foundry Local service, loads the `phi-3.5-mini` model, and lists the models
in the running in the service.

Next, add a new GPT to your `openai.agent.json` file.

- The `foundry service start` shows the URI for the service. The `Endpoint` for the OpenAI agent is
  the URI plus `/v1`.
- The `foundry service ps` command shows `ModelName` as the **Model ID**. Make sure you use the
  exact casing as shown in **Model ID**. Foundry Local is case-sensitive.
- The API key is hardcoded to `OPENAI_API_KEY`.

```json
{
  "GPTs": [
    {
      "Name": "foundry-local",
      "Description": "A GPT instance using Foundry Local.",
      "Endpoint": "http://127.0.0.1:56952/v1",
      "ModelName": "Phi-3.5-mini-instruct-generic-cpu",
      "Key": "OPENAI_API_KEY"
    }
  ]

  "Active": "foundry-local"
}
```

<!-- link references -->
[01]: /azure/ai-foundry/foundry-local/get-started
[02]: /azure/ai-foundry/foundry-local/what-is-foundry-local
[03]: https://ai.google.dev/gemini-api/docs/openai
[04]: https://api-docs.deepseek.com/
[05]: https://docs.x.ai/docs/overview#migrating-from-another-llm-provider
[06]: https://lmstudio.ai/docs/api/openai-api
[07]: https://localai.io/
[08]: https://ollama.com/blog/openai-compatibility
[09]: xref:Azure.Identity.DefaultAzureCredential

