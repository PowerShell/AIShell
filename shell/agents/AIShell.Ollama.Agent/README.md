# Ollama Plugin

This agent is used to interact with a language model running locally or remotely by utilizing the Ollama API.
Before using this agent locally you need to have Ollama installed and running.

## Pre-requisites to using the agent locally

- Install [Ollama](https://github.com/ollama/ollama)
- Install a [Ollama model](https://github.com/ollama/ollama?tab=readme-ov-file#model-library), we suggest using the `phi3` model as it is set as the default model in the code
- [Start the Ollama API server](https://github.com/ollama/ollama?tab=readme-ov-file#start-ollama)

## Configuration

To configure the agent, run `/agent config ollama` to open up the setting file in your default editor, and then update the file based on the following example.

```jsonc
{
    // To use Ollama API service:
    // 1. Install Ollama: `winget install Ollama.Ollama`
    // 2. Start Ollama API server: `ollama serve`
    // 3. Install Ollama model: `ollama pull phi3`

    // Declare predefined model configurations
    "Presets": [
        {
            "Name": "PowerShell Expert",
            "Description": "A ollama agent with expertise in PowerShell scripting and command line utilities.",
            "ModelName": "phi3",
            "SystemPrompt": "You are a helpful and friendly assistant with expertise in PowerShell scripting and command line."
        }
    ],

    // Declare Ollama endpoint
    "Endpoint": "http://localhost:11434",
    // Enable Ollama streaming
    "Stream": false,
    // Specify the default preset to use
    "DefaultPreset": "PowerShell Expert"
}
```
