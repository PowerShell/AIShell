using AIShell.Abstraction;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AIShell.Kernel.Mcp;

internal class BuiltInTool : AIFunction
{
    private enum ToolType : int
    {
        get_working_directory = 0,
        get_command_history = 1,
        get_terminal_content = 2,
        get_environment_variables = 3,
        copy_text_to_clipboard = 4,
        post_code_to_terminal = 5,
        run_command_in_terminal = 6,
        get_command_output = 7,
        NumberOfBuiltInTools = 8
    };

    private static readonly string[] s_toolDescription =
    [
        // get_working_directory
        "Get the current working directory of the connected PowerShell session, including the provider name (e.g., `FileSystem`, `Certificate`) and the path (e.g., `C:\\`, `cert:\\`).",

        // get_command_history
        "Get up to 5 of the most recent commands executed in the connected PowerShell session.",

        // get_terminal_content
        "Get all output currently displayed in the terminal window of the connected PowerShell session.",

        //get_environment_variables
        "Get environment variables and their values from the connected PowerShell session. Values of potentially sensitive variables are redacted.",

        // copy_text_to_clipboard
        "Copy the provided text or code to the system clipboard, making it available for pasting elsewhere.",

        // post_code_to_terminal
        "Insert code into the prompt of the connected PowerShell session without executing it. The user can review and choose to run it manually by pressing Enter.",

        // run_command_in_terminal
        """
        This tool allows you to execute shell commands in a persistent PowerShell session, preserving environment variables, working directory, and other context across multiple commands.

        Command Execution:
        - Supports chaining with `&&` or `;` (e.g., npm install && npm start).
        - Supports multi-line commands

        Directory Management:
        - Use absolute paths to avoid navigation issues.

        Program Execution:
        - Supports running PowerShell commands and scripts.
        - Supports Python, Node.js, and other executables.
        - Install dependencies via pip, npm, etc.

        Background Processes:
        - For long-running tasks (e.g., servers), set `isBackground=true`.
        - Returns a command ID for checking status and output later.

        Important Notes:
        - If the command may produce excessively large output, use head or tail to reduce the output.
        - If a command may use a pager, you must add something to disable it. For example, you can use `git --no-pager`. Otherwise you should add something like ` | cat`. Examples: git, less, man, etc.
        """,

        // get_command_output
        "Get the output of a command previously started with `run_command_in_terminal`."
    ];

    private static readonly string[] s_toolSchema =
    [
        // get_working_directory
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // get_command_history
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // get_terminal_content
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // get_environment_variables
        """
        {
          "type": "object",
          "properties": {
            "names": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "default": null,
              "description": "Environment variable names to get values for. When no name is specified, returns all environment variables."
            }
          },
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // copy_text_to_clipboard
        """
        {
          "type": "object",
          "properties": {
            "content": {
              "type": "string",
              "description": "Text or code to be copied to the system clipboard."
            }
          },
          "required": [
            "content"
          ],
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // post_code_to_terminal
        """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "Command or code snippet to be inserted to the terminal's prompt."
            }
          },
          "required": [
            "command"
          ],
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // run_command_in_terminal
        """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "Command to run in the connected PowerShell session."
            },
            "explanation": {
              "type": "string",
              "description": "A one-sentence description of what the command does. This will be shown to the user before the command is run."
            },
            "isBackground": {
              "type": "boolean",
              "description": "Whether the command starts a background process. If true, the command will run in the background and you will not see the output. If false, the tool call will block on the command finishing, and then you will get the output. Examples of backgrond processes: building in watch mode, starting a server. You can check the output of a backgrond process later on by using `get_command_output`."
            }
          },
          "required": [
            "command",
            "explanation",
            "isBackground"
          ],
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """,

        // get_command_output
        """
        {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "description": "The ID of the command to get the output from. This is the ID returned by the `run_command_in_terminal` tool."
            }
          },
          "required": [
            "id"
          ],
          "additionalProperties": false,
          "$schema": "http://json-schema.org/draft-07/schema#"
        }
        """
    ];

    private readonly string _fullName;
    private readonly string _toolName;
    private readonly ToolType _toolType;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;
    private readonly Shell _shell;

    private BuiltInTool(ToolType toolType, string description, JsonElement schema, Shell shell)
    {
        _toolType = toolType;
        _shell = shell;
        _toolName = toolType.ToString();
        _description = description;
        _jsonSchema = schema;

        _fullName = $"{McpManager.BuiltInServerName}{McpManager.ServerToolSeparator}{_toolName}";
    }

    /// <summary>
    /// The original tool name without the server name prefix.
    /// </summary>
    internal string OriginalName => _toolName;

    /// <summary>
    /// The fully qualified name of the tool in the form of '<server-name>.<tool-name>'
    /// </summary>
    public override string Name => _fullName;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Overrides the base method with the call to <see cref="CallAsync"/>.
    /// </summary>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var response = await CallAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (response is PostContextMessage postContextMsg)
        {
            return postContextMsg.ContextInfo;
        }

        if (response is PostResultMessage postResultMsg)
        {
            StringBuilder strb = new(postResultMsg.Output.Length + 40);
            strb.AppendLine("### Status")
                .AppendLine(postResultMsg.UserCancelled
                    ? "Execution was cancelled by the user."
                    : postResultMsg.HadError ? "Had error." : "Succeeded.")
                .AppendLine()
                .AppendLine("### Output")
                .AppendLine("```")
                .AppendLine(postResultMsg.Output.Trim())
                .AppendLine("```");

            return strb.ToString();
        }

        return response is null ? "Success: Function completed." : JsonSerializer.SerializeToElement(response);
    }

    /// <summary>
    /// Invokes the built-in tool.
    /// </summary>
    /// <param name="arguments">
    /// An optional dictionary of arguments to pass to the tool.
    /// Each key represents a parameter name, and its associated value represents the argument value.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token to monitor for cancellation requests.
    /// The default is <see cref="CancellationToken.None" />.
    /// </param>
    /// <exception cref="OperationCanceledException">The user rejected the tool call.</exception>
    internal async Task<PipeMessage> CallAsync(
        IReadOnlyDictionary<string, object> arguments = null,
        CancellationToken cancellationToken = default)
    {
        PipeMessage response = null;
        AskContextMessage contextRequest = _toolType switch
        {
            ToolType.get_working_directory => new(ContextType.CurrentLocation),
            ToolType.get_command_history => new(ContextType.CommandHistory),
            ToolType.get_terminal_content => new(ContextType.TerminalContent),
            ToolType.get_environment_variables => new(
                ContextType.EnvironmentVariables,
                TryGetArgumentValue(arguments, "names", out string[] names)
                    ? names is { Length: > 0 } ? names : null
                    : null),
            _ => null
        };

        if (contextRequest is not null)
        {
            response = await _shell.Host.RunWithSpinnerAsync(
                async () => await _shell.Channel.AskContext(contextRequest, cancellationToken),
                status: $"Running '{_toolName}'",
                spinnerKind: SpinnerKind.Processing);
        }
        else if (_toolType is ToolType.copy_text_to_clipboard)
        {
            TryGetArgumentValue(arguments, "content", out string content);
            if (string.IsNullOrEmpty(content))
            {
                throw new ArgumentException("The 'content' argument is required for the 'copy_text_to_clipboard' tool.");
            }

            Clipboard.SetText(content);
        }
        else if (_toolType is ToolType.post_code_to_terminal)
        {
            TryGetArgumentValue(arguments, "command", out string command);
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("The 'command' argument is required for the 'post_code_to_terminal' tool.");
            }

            _shell.Channel.PostCode(new PostCodeMessage([command]));
        }
        else if (_toolType is ToolType.run_command_in_terminal)
        {
            TryGetArgumentValue(arguments, "command", out string command);
            TryGetArgumentValue(arguments, "explanation", out string explanation);
            TryGetArgumentValue(arguments, "isBackground", out bool isBackground);

            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentException("The 'command' argument is required for the 'run_command_in_terminal' tool.");
            }
            if (string.IsNullOrEmpty(explanation))
            {
                throw new ArgumentException("The 'explanation' argument is required for the 'run_command_in_terminal' tool.");
            }

            _shell.Host.RenderBuiltInToolCallRequest(OriginalName, explanation, Tuple.Create("command", command));
            // Prompt for user's approval to call the tool.
            const string title = "\n\u26A0  Malicious conversation content may attempt to misuse 'AIShell' through the built-in tools. Please carefully review any requested actions to decide if you want to proceed.";
            string choice = await _shell.Host.PromptForSelectionAsync(
                title: title,
                choices: McpTool.UserChoices,
                cancellationToken: cancellationToken);

            if (choice is "Cancel")
            {
                _shell.Host.MarkupLine($"\n    [red]\u2717[/] Cancelled '{OriginalName}'");
                throw new OperationCanceledException("The call was rejected by user.");
            }

            response = await _shell.Host.RunWithSpinnerAsync(
                async () => await _shell.Channel.RunCommand(new RunCommandMessage(command, blocking: !isBackground), cancellationToken),
                status: $"Running '{_toolName}'",
                spinnerKind: SpinnerKind.Processing);
        }
        else if (_toolType is ToolType.get_command_output)
        {
            TryGetArgumentValue(arguments, "id", out string id);
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("The 'id' argument is required for the 'get_command_output' tool.");
            }

            response = await _shell.Channel.AskCommandOutput(new AskCommandOutputMessage(id), cancellationToken);
        }

        // Notify the user about this tool call.
        _shell.Host.MarkupLine($"\n    [green]\u2713[/] Ran '{_toolName}'");

        // Signal any active stream render about the output.
        FancyStreamRender.ConsoleUpdated();
        return response;
    }

    private static bool TryGetArgumentValue<T>(IReadOnlyDictionary<string, object> arguments, string argName, out T value)
    {
        if (arguments is null || !arguments.TryGetValue(argName, out object argValue))
        {
            value = default;
            return false;
        }

        if (argValue is T tValue)
        {
            value = tValue;
            return true;
        }

        if (argValue is JsonElement json)
        {
            Type tType = typeof(T);
            JsonValueKind kind = json.ValueKind;

            if (tType == typeof(string))
            {
                if (kind is JsonValueKind.String)
                {
                    object stringValue = json.GetString();
                    value = (T)stringValue;
                    return true;
                }

                value = default;
                return kind is JsonValueKind.Null;
            }

            if (tType == typeof(string[]))
            {
                if (kind is JsonValueKind.Array)
                {
                    object stringArray = json.EnumerateArray().Select(e => e.GetString()).ToArray();
                    value = (T)stringArray;
                    return true;
                }

                value = default;
                return kind is JsonValueKind.Null;
            }

            if (tType == typeof(bool) && kind is JsonValueKind.True or JsonValueKind.False)
            {
                value = (T)(object)json.GetBoolean();
                return true;
            }

            if (tType == typeof(int) && kind is JsonValueKind.Number)
            {
                value = (T)(object)json.GetInt32();
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Gets the list of built-in tools available in AIShell.
    /// </summary>
    internal static Dictionary<string, BuiltInTool> GetBuiltInTools(Shell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        int toolCount = (int)ToolType.NumberOfBuiltInTools;
        Debug.Assert(s_toolDescription.Length == (int)ToolType.NumberOfBuiltInTools, "Number of tool descriptions doesn't match the number of tools.");
        Debug.Assert(s_toolSchema.Length == (int)ToolType.NumberOfBuiltInTools, "Number of tool schemas doesn't match the number of tools.");

        if (shell.Channel is null || !shell.Channel.Connected)
        {
            return null;
        }

        Dictionary<string, BuiltInTool> tools = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < toolCount; i++)
        {
            ToolType toolType = (ToolType)i;
            string description = s_toolDescription[i];
            JsonElement schema = JsonSerializer.Deserialize<JsonElement>(s_toolSchema[i]);
            BuiltInTool tool = new(toolType, description, schema, shell);

            tools.Add(tool.OriginalName, tool);
        }

        return tools;
    }
}
