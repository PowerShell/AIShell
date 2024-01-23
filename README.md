# Shell Copilot

This is a repository of various A.I + Shell prototypes we have created to test out experiences and
features. **Shell Copilot** is the latest and most finished prototype. It is a CLI tool that creates
an interactive chat session with a registered Large Language Model. Currently we are in a **Private Preview** state and everything is subject to change.

![GIF showing demo of Shell Copilot](./docs/media/ShellCopilotDemo.gif)

## Installing and Using Shell Copilot

Some prerequistates for building Shell Copilot
- Need to be on [PowerShell 7.0+](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell?view=powershell-7.4)
- Need [.NET SDK v7+](https://dotnet.microsoft.com/en-us/download) installed
- Execution permission on the `build.ps1` script, you can do this by setting the execution policy to unrestricted `Set-ExecutionPolicy -ExecutionPolicy Restricted -Scope CurrentUser`

Here are the steps to install and use Shell Copilot.
1. Clone this repository, `git clone https://github.com/PowerShell/ShellCopilot`
2. To build run `./build.ps1` in the project's directory
3. Add the `<path to project>\ShellCopilot\out\debug` directory to your `$PATH` with `$env:PATH += <path to project>\ShellCopilot\out\debug`
4. Add the above line to your `$PROFILE` to be able to use it anytime you open up PowerShell. You can edit it by doing `code $PROFILE` if you have VSCode installed or `notepad $PROFILE` if you are on Windows

> Note: Depending on your OS directory paths may be `\` on Windows or `/` on Mac.

## Agent Concept

ShellCopilot has a concept of different A.I Agents, these can be thought of like modules that users can use to interact with different A.I models. Right now there are two supported agents
- `az-cli`
- `openai-gpt`

If you run `aish` you will get prompted to choose between the two.

# Az-CLI Agent

This agent is for talking specifically to an Az CLI endpoint tailored to helping users with Azure CLI questions.

Prerequistates:
- Have [Azure CLI installed](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- Login with an Azure account within the Microsoft tenant with `az login` command

# OpenAI-GPT Agent

This is a more generalized agent that users can bring their own instance of Azure OpenAI and a completely customizable prompt.

## Getting an Azure OpenAI Endpoint Key

Currently we only support Azure OpenAI LLM endpoints. We are currently hosting a internal only Azure
OpenAI endpoint that you can get and use without getting your Azure OpenAI instance. This is for internal private preview purposes only.

All the configuration is already included by default and so you will be prompted to include a API key to be able to use this endpoint.

Guide for Signing Up For API Key
1.  Navigate to <https://pscopilot.developer.azure-api.net>
2.  Click `Sign Up` located on the top right corner of the page.
3.  Sign up for a subscription by filling in the fields (email, password, first name, last name).
4.  Verify the account (An email should have been sent from
    <apimgmt-noreply@mail.windowsazure.com> to your email)
5.  Click `Sign In` located on the top right corner of the page.
6.  Enter the email and password used when signing up.
7.  Click `Products` located on the top right corner of the page
8.  In the field stating `Your new product subscription name`, Enter `Azure OpenAI Service API`.
9.  Click `Subscribe` to be subscribed to the product.

In order to view your subscription/API key,
1.  Click `Profile` located on the top right corner of the page.
2.  Your Key should be located under the `Subscriptions` section. Click on `Show` to view the
    primary or secondary key.

Once you have a key you can always edit your endpoint configuration by running `/agent config openai-gpt` within Shell Copilot. This opens up a JSON file with all the configuration options. 

If you have separate Azure OpenAI endpoint you can use that instead of the one above. Read more at
[Create and deploy an Azure OpenAI Service resource](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/create-resource?pivots=ps).

## Using Shell Copilot

To start a chat session with the LLM, simply run `aish` and it will open up a new session in your current window. We suggest using a split pane approach with the terminal of choice, Windows Terminal offers an easy pane option by running:

```shell
wt -w 0 sp aish
```

To explore the other options available to you, run `aish --help` to see all the subcommands. 

## Feedback

We still in development and value any and all feedback! Please file an [issue in this repository](https://github.com/PowerShell/ShellCopilot/issues) for
any bugs, suggestions and feedback. Any additional feedback can be sent to
stevenbucher@microsoft.com.
