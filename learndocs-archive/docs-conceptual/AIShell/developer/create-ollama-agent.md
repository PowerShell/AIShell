---
title: How to create an agent for Ollama
description: Learn how to create an agent for the Ollama language model in AI Shell.
ms.date: 01/12/2026
ms.topic: how-to
ms.collection: ce-skilling-ai-copilot
---
# Creating an Agent

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

An agent is a code library that interfaces with the AI Shell to talk to a specific large
language model or other assistance provider. Users chat with the agents using natural language to
get the desired output or assistance. Agents are implemented as C# classes that implement the
`ILLMAgent` interface from the `AIShell.Abstraction` package.

For details about the `AIShell.Abstraction` layer and `AIShell.Kernel`, see the
[AI Shell architecture][05] documentation.

This article is a step-by-step guide to creating an agent for the [Ollama][01] language model. The
purpose of this article is to provide a simple example of how to create an agent. There's a more
robust implementation of the Ollama agent in the [`AIShell.Ollama.Agent`][04] folder of the
repository.

## Prerequisites

- .NET 8 SDK or newer
- PowerShell 7.4.6 or newer

## Steps to create an agent

For this example, we create an agent to communicate with the language model `phi3` using
[Ollama][01]. Ollama is a CLI tool for managing and using locally built LLM/SLMs.

### Step 1: Create a new project

First step is to create a new **classlib** project.

1. Create a new folder named `OllamaAgent`
1. Run the following command to create a new project:

   ```shell
   dotnet new classlib
   ```

### Step 2: Add the necessary packages

Within the newly created project, you need to install the [AIShell.Abstraction][06] package from the
NuGet gallery. Install the NuGet package using the following command:

```shell
dotnet add package AIShell.Abstraction --version 1.0.0-preview.2
```

This command adds the package to your `.csproj` file. Your `.csproj` file should contain the
following XML:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <SuppressNETCoreSdkPreviewMessage>true</SuppressNETCoreSdkPreviewMessage>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AIShell.Abstraction" Version="1.0.0-preview.2" />
  </ItemGroup>
</Project>
```

> [!IMPORTANT]
> Be sure to check you are on the latest version from the NuGet gallery.

### Step 3: Implement the agent class

To implement the `ILLMAgent` interface, modify the `Class1.cs` file.

1. Rename the file to `OllamaAgent.cs`
1. Rename the class to `OllamaAgent`
1. Add the .NET namespaces that are used by the code in the implementation

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIShell.Abstraction;

namespace AIShell.Ollama.Agent;

public sealed class OllamaAgent : ILLMAgent
{

}
```

### Step 4: Add necessary class members and methods

Next, implement the necessary variables and methods of the agent class. The comments provide
descriptions of the members of the **OllamaAgent** class. The `_chatService` member is an instance
of the **OllamaChatService** class, which you implement in a later step.

```csharp
public sealed class OllamaAgent : ILLMAgent
{
    /// <summary>
    /// The name of the agent
    /// </summary>
    public string Name => "ollama";

    /// <summary>
    /// The description of the agent to be shown at start up
    /// </summary>
    public string Description => "This is an AI assistant that uses the Ollama CLI tool. Be sure to follow all prerequisites in https://aka.ms/ollama/readme";

    /// <summary>
    /// This is the company added to `/like` and `/dislike` verbiage for who the telemetry helps.
    /// </summary>
    public string Company => "Microsoft";

    /// <summary>
    /// These are samples that are shown at start up for good questions to ask the agent
    /// </summary>
    public List<string> SampleQueries => [
        "How do I list files in a given directory?"
    ];

    /// <summary>
    /// These are any optional legal/additional information links you want to provide at start up
    /// </summary>
    public Dictionary<string, string> LegalLinks { private set; get; }

    /// <summary>
    /// This is the chat service to call the API from
    /// </summary>
    private OllamaChatService _chatService;

    /// <summary>
    /// A string builder to render the text at the end
    /// </summary>
    private StringBuilder _text;

    /// <summary>
    /// Dispose method to clean up the unmanaged resource of the chatService
    /// </summary>
    public void Dispose()
    {
        _chatService?.Dispose();
    }

    /// <summary>
    /// Initializing function for the class when the shell registers an agent
    /// </summary>
    /// <param name="config">Agent configuration for any configuration file and other settings</param>
    public void Initialize(AgentConfig config)
    {
        _text = new StringBuilder();
        _chatService = new OllamaChatService();

        LegalLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ollama Docs"] = "https://github.com/ollama/ollama",
            ["Prerequisites"] = "https://aka.ms/ollama/readme"
        };

    }

    /// <summary>
    /// Get commands that an agent can register to the shell when being loaded
    /// </summary>
    public IEnumerable<CommandBase> GetCommands() => null;

    /// <summary>
    /// Gets the path to the setting file of the agent.
    /// </summary>
    public string SettingFile { private set; get; } = null;

    /// <summary>
    /// Refresh the current chat by starting a new chat session.
    /// An agent can reset chat states in this method.
    /// </summary>
    public void RefreshChat() {}

    /// <summary>
    /// Gets a value indicating whether the agent accepts a specific user action feedback.
    /// </summary>
    /// <param name="action">The user action.</param>
    public bool CanAcceptFeedback(UserAction action) => false;

    /// <summary>
    /// A user action was taken against the last response from this agent.
    /// </summary>
    /// <param name="action">Type of the action.</param>
    /// <param name="actionPayload"></param>
    public void OnUserAction(UserActionPayload actionPayload) {}

    /// <summary>
    /// Main chat function that takes
    /// </summary>
    /// <param name="input">The user input from the chat experience</param>
    /// <param name="shell">The shell that provides host functionality</param>
    /// <returns>Task Boolean that indicates whether the query was served by the agent.</returns>
    public async Task<bool> Chat(string input, IShell shell)
    {

    }
}
```

For the initial implementation, the agent to returns "Hello World!", proving that you created the
correct interfaces. You also need to add a `try-catch` block to catch and handle any exceptions when
the user tries to cancel the operation.

Add the following code to your `Chat` method.

```csharp
public async Task<bool> Chat(string input, IShell shell)
{
    // Get the shell host
    IHost host = shell.Host;

    // get the cancellation token
    CancellationToken token = shell.CancellationToken;

    try
    {
       host.RenderFullResponse("Hello World!");
    }
    catch (OperationCanceledException e)
    {
        _text.AppendLine(e.ToString());

        host.RenderFullResponse(_text.ToString());

        return false;
    }

    return true;
}
```

### Step 5: Add Ollama check

Next, you need to make sure that Ollama is running.

```csharp
public async Task<bool> Chat(string input, IShell shell)
{
    // Get the shell host
    IHost host = shell.Host;

    // get the cancellation token
    CancellationToken token = shell.CancellationToken;

    if (Process.GetProcessesByName("ollama").Length is 0)
    {
        host.RenderFullResponse("Please be sure that Ollama is installed and the server is running. Ensure that you have met all the prerequisites in the README for this agent.");
        return false;
    }

    // Calls to the API will go here

    return true;
}
```

### Step 6: Create data structures to exchange data with the Chat Service

Before you can use the Ollama API, you need to create classes that send input to and receive
responses from the Ollama API. The following [Ollama example][02] shows the format of the input and
the response from the agent.

This example calls the Ollama API with streaming disabled. Ollama generates a single, fixed
response. In the future, you could add streaming capabilities so that responses could be rendered in
real time, as the agent receives them.

To define the data structures, create a new file in the same folder named `OllamaSchema.cs`. Copy
the following code to the file.

```csharp
namespace AIShell.Ollama.Agent;

// Query class for the data to send to the endpoint
internal class Query
{
    public string prompt { get; set; }
    public string model { get; set; }

    public bool stream { get; set; }
}

// Response data schema
internal class ResponseData
{
    public string model { get; set; }
    public string created_at { get; set; }
    public string response { get; set; }
    public bool done { get; set; }
    public string done_reason { get; set; }
    public int[] context { get; set; }
    public double total_duration { get; set; }
    public long load_duration { get; set; }
    public int prompt_eval_count { get; set; }
    public int prompt_eval_duration { get; set; }
    public int eval_count { get; set; }
    public long eval_duration { get; set; }
}

internal class OllamaResponse
{
    public int Status { get; set; }
    public string Error { get; set; }
    public string Api_version { get; set; }
    public ResponseData Data { get; set; }
}
```

Now you have the pieces needed to construct a chat service that uses the Ollama API. A separate chat
service class isn't required, but it's useful to abstract the calls to the API.

Create a new file called `OllamaChatService.cs` in the same folder as the agent. Copy the example
code into the file.

> [!TIP]
> This example uses a hard-coded endpoint and language model for the Ollama API. In the future, you
> could define configurable parameters in an agent configuration file.

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using AIShell.Abstraction;

namespace AIShell.Ollama.Agent;

internal class OllamaChatService : IDisposable
{
    /// <summary>
    /// Ollama endpoint to call to generate a response
    /// </summary>
    internal const string Endpoint = "http://localhost:11434/api/generate";

    /// <summary>
    /// Http client
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// Initialization method to initialize the http client
    /// </summary>

    internal OllamaChatService()
    {
        _client = new HttpClient();
    }

    /// <summary>
    /// Dispose of the http client
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    /// Preparing chat with data to be sent
    /// </summary>
    /// <param name="input">The user input from the chat experience</param>
    /// <returns>The HTTP request message</returns>
    private HttpRequestMessage PrepareForChat(string input)
    {
        // Main data to send to the endpoint
        var requestData = new Query
        {
            model = "phi3",
            prompt = input,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestData);

        var data = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = data };

        return request;
    }

    /// <summary>
    /// Getting the chat response async
    /// </summary>
    /// <param name="context">Interface for the status context used when displaying a spinner.</param>
    /// <param name="input">The user input from the chat experience</param>
    /// <param name="cancellationToken">The cancellation token to exit out of request</param>
    /// <returns>Response data from the API call</returns>
    internal async Task<ResponseData> GetChatResponseAsync(IStatusContext context, string input, CancellationToken cancellationToken)
    {
        try
        {
            HttpRequestMessage request = PrepareForChat(input);
            HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            context?.Status("Receiving Payload ...");
            Console.Write(response.Content);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return JsonSerializer.Deserialize<ResponseData>(content);
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled by user.
        }

        return null;
    }
}

```

### Step 7: Call the chat service

Next, you need to call the chat service in the main agent class. Modify the `Chat()` method to call
the chat service and render the response to the user. The following example shows the completed
`Chat()` method.

```csharp
public async Task<bool> Chat(string input, IShell shell)
{
    // Get the shell host
    IHost host = shell.Host;

    // get the cancellation token
    CancellationToken token = shell.CancellationToken;

    if (Process.GetProcessesByName("ollama").Length is 0)
    {
        host.RenderFullResponse("Please be sure that Ollama is installed and the server is running. Ensure that you have met all the prerequisites in the README for this agent.");
        return false;
    }

    ResponseData ollamaResponse = await host.RunWithSpinnerAsync(
        status: "Thinking ...",
        func: async context => await _chatService.GetChatResponseAsync(context, input, token)
    ).ConfigureAwait(false);

    if (ollamaResponse is not null)
    {
        // render the content
        host.RenderFullResponse(ollamaResponse.response);
    }

    return true;
}
```

The agent code is complete.

### Step 8: Build and test the agent

Next, you need to build and test that the code is working as expected. Run the following command:

```shell
dotnet build
```

This command builds all necessary packages in the `\bin\Debug\net8.0` folder of the project.

To have `aish` load the agent, you need to copy the `.dll` files to a folder in the `Agents` folder.
The folder name should be the same as the agent name.

You can install agents in one of two locations:

- In the `Agents` folder under the location where you installed `aish.exe`. The
  [install script][08] for AI Shell installs in `%LOCALAPPDATA%\Programs\AIShell`. Create the
  `%LOCALAPPDATA%\Programs\AIShell\Agents\OllamaAgent` folder.
- As an alternative, you install agents in `%USERPROFILE%\.aish\Agents`. Create the
  `%USERPROFILE%\.aish\Agents\OllamaAgent` folder.

Copy the `.dll` files to agent folder you created. You should see the agent when you start `aish`.

```shell
AI Shell
v1.0.0-preview.2

Please select an agent to use:

    azure
   >ollama
    openai-gpt
```

## How can I share my own agent?

There's no way to share your agents in a centralized repository. We suggest forking this repository
for development of your own agent. You can share a link your fork in the `Agent Sharing` section of
the [Discussions][03] tab of this repository. To use an agent, put agent `dll` files in the `agents`
folder of the base directory of `aish.exe`. AI Shell automatically loads the agents from that
folder.

<!-- link references -->
[01]: https://github.com/ollama/ollama
[02]: https://github.com/ollama/ollama/blob/main/docs/api.md#request-no-streaming
[03]: https://github.com/PowerShell/AIShell/discussions/categories/agent-sharing
[04]: https://github.com/PowerShell/AIShell/tree/main/shell/agents/AIShell.Ollama.Agent
[05]: https://github.com/PowerShell/AIShell/blob/main/shell/README.md
[06]: https://www.nuget.org/packages/AIShell.Abstraction

