using System.Collections.Concurrent;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using AIShell.Abstraction;
using Serilog;

namespace Microsoft.Azure.Agent;

using PowerShell = System.Management.Automation.PowerShell;

internal class DataRetriever : IDisposable
{
    private const int WorkerCount = 3;
    private const string MetadataQueryTemplate = "{{\"command\":\"{0}\"}}";
    private const string MetadataEndpoint = "https://cli-validation-tool-meta-qry.azurewebsites.net/api/command_metadata";

    private static readonly string s_azCompleteCmd, s_azCompleteArg;
    private static readonly Dictionary<string, NamingRule> s_azNamingRules;
    private static readonly ConcurrentDictionary<string, AzCLICommand> s_azStaticDataCache;
    private static readonly PowerShellPool s_psPool;

    private readonly Task _rootTask;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore;
    private readonly List<ArgumentPair> _placeholders;
    private readonly Dictionary<string, ArgumentPair> _placeholderMap;

    private bool _stop;

    static DataRetriever()
    {
        s_psPool = new(size: WorkerCount);
        List<NamingRule> rules = [
            new("API Management Service",
                "apim",
                "The name only allows alphanumeric characters and hyphens, and the first character must be a letter. Length: 1 to 50 chars.",
                "az apim create --name",
                "New-AzApiManagement -Name"),

            new("Function App",
                "func",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 2 to 60 chars.",
                "az functionapp create --name",
                "New-AzFunctionApp -Name"),

            new("App Service Plan",
                "asp",
                "The name only allows alphanumeric characters and hyphens. Length: 1 to 60 chars.",
                "az appservice plan create --name",
                "New-AzAppServicePlan -Name"),

            new("Web App",
                "web",
                "The name only allows alphanumeric characters and hyphens. Length: 2 to 43 chars.",
                "az webapp create --name",
                "New-AzWebApp -Name"),

            new("Application Gateway",
                "agw",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 - 80 chars.",
                "az network application-gateway create --name",
                "New-AzApplicationGateway -Name"),

            new("Application Insights",
                "ai",
                "The name only allows alphanumeric characters, underscores, periods, hyphens and parenthesis, and cannot end in a period. Length: 1 to 255 chars.",
                "az monitor app-insights component create --app",
                "New-AzApplicationInsights -Name"),

            new("Application Security Group",
                "asg",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network asg create --name",
                "New-AzApplicationSecurityGroup -Name"),

            new("Automation Account",
                "aa",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 6 to 50 chars.",
                "az automation account create --name",
                "New-AzAutomationAccount -Name"),

            new("Availability Set",
                "as",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az vm availability-set create --name",
                "New-AzAvailabilitySet -Name"),

            new("Redis Cache",
                "redis",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Consecutive hyphens are not allowed. Length: 1 to 63 chars.",
                "az redis create --name",
                "New-AzRedisCache -Name"),

            new("Cognitive Service",
                "cogs",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 2 to 64 chars.",
                "az cognitiveservices account create --name",
                "New-AzCognitiveServicesAccount -Name"),

            new("Cosmos DB",
                "cosmos",
                "The name only allows lowercase letters, numbers, and hyphens, and cannot start or end with a hyphen. Length: 3 to 44 chars.",
                "az cosmosdb create --name",
                "New-AzCosmosDBAccount -Name"),

            new("Event Hubs Namespace",
                "eh",
                "The name only allows alphanumeric characters and hyphens. It must start with a letter and end with a letter or number. Length: 6 to 50 chars.",
                "az eventhubs namespace create --name",
                "New-AzEventHubNamespace -Name"),

            new("Event Hubs",
                abbreviation: null,
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start and end with a letter or number. Length: 1 to 256 chars.",
                "az eventhubs eventhub create --name",
                "New-AzEventHub -Name"),

            new("Key Vault",
                "kv",
                "The name only allows alphanumeric characters and hyphens. It must start with a letter and end with a letter or number. Consecutive hyphens are not allowed. Length: 3 to 24 chars.",
                "az keyvault create --name",
                "New-AzKeyVault -Name"),

            new("Load Balancer",
                "lb",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network lb create --name",
                "New-AzLoadBalancer -Name"),

            new("Log Analytics workspace",
                "la",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 4 to 63 chars.",
                "az monitor log-analytics workspace create --name",
                "New-AzOperationalInsightsWorkspace -Name"),

            new("Logic App",
                "lapp",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 2 to 64 chars.",
                "az logic workflow create --name",
                "New-AzLogicApp -Name"),

            new("Machine Learning workspace",
                "mlw",
                "The name only allows alphanumeric characters, underscores, and hyphens. It must start with a letter or number. Length: 3 to 33 chars.",
                "az ml workspace create --name",
                "New-AzMLWorkspace -Name"),

            new("Network Interface",
                "nic",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 2 to 64 chars.",
                "az network nic create --name",
                "New-AzNetworkInterface -Name"),

            new("Network Security Group",
                "nsg",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 2 to 64 chars.",
                "az network nsg create --name",
                "New-AzNetworkSecurityGroup -Name"),

            new("Notification Hub Namespace",
                "nh",
                "The name only allows alphanumeric characters and hyphens. It must start with a letter and end with a letter or number. Length: 6 to 50 chars.",
                "az notification-hub namespace create --name",
                "New-AzNotificationHubsNamespace -Namespace"),

            new("Notification Hub",
                abbreviation: null,
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start and end with a letter or number. Length: 1 to 260 chars.",
                "az notification-hub create --name",
                "New-AzNotificationHub -Name"),

            new("Public IP address",
                "pip",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network public-ip create --name",
                "New-AzPublicIpAddress -Name"),

            new("Resource Group",
                "rg",
                "Resource group names can only include alphanumeric, underscore, parentheses, hyphen, period (except at end), and Unicode characters that match the allowed characters. Length: 1 to 90 chars.",
                "az group create --name",
                "New-AzResourceGroup -Name"),

            new("Route table",
                "rt",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start and end with a letter or number. Length: 1 to 80 chars.",
                "az network route-table create --name",
                "New-AzRouteTable -Name"),

            new("Search Service",
                "srch",
                "Service name must only contain lowercase letters, digits or dashes, cannot use dash as the first two or last one characters, and cannot contain consecutive dashes. Length: 2 to 60 chars.",
                "az search service create --name",
                "New-AzSearchService -Name"),

            new("Service Bus Namespace",
                "sb",
                "The name only allows alphanumeric characters and hyphens. It must start with a letter and end with a letter or number. Length: 6 to 50 chars.",
                "az servicebus namespace create --name",
                "New-AzServiceBusNamespace -Name"),

            new("Service Bus queue",
                abbreviation: null,
                "The name only allows alphanumeric characters and hyphens. It must start with a letter and end with a letter or number. Length: 6 to 50 chars.",
                "az servicebus queue create --name",
                "New-AzServiceBusQueue -Name"),

            new("Azure SQL Managed Instance",
                "sqlmi",
                "The name can only contain lowercase letters, numbers and hyphens. It cannot start or end with a hyphen, nor can it have two consecutive hyphens in the third and fourth places of the name. Length: 1 to 63 chars.",
                "az sql mi create --name",
                "New-AzSqlInstance -Name"),

            new("SQL Server",
                "sqldb",
                "The name can only contain lowercase letters, numbers and hyphens. It cannot start or end with a hyphen, nor can it have two consecutive hyphens in the third and fourth places of the name. Length: 1 to 63 chars.",
                "az sql server create --name",
                "New-AzSqlServer -ServerName"),

            new("Storage Container",
                abbreviation: null,
                "The name can only contain lowercase letters, numbers and hyphens. It must start with a letter or a number, and each hyphen must be preceded and followed by a non-hyphen character. Length: 3 to 63 chars.",
                "az storage container create --name",
                "New-AzStorageContainer -Name"),

            new("Storage Queue",
                abbreviation: null,
                "The name can only contain lowercase letters, numbers and hyphens. It must start with a letter or a number, and each hyphen must be preceded and followed by a non-hyphen character. Length: 3 to 63 chars.",
                "az storage queue create --name",
                "New-AzStorageQueue -Name"),

            new("Storage Table",
                abbreviation: null,
                "The name can only contain letters and numbers, and must start with a letter. Length: 3 to 63 chars.",
                "az storage table create --name",
                "New-AzStorageTable -Name"),

            new("Storage File Share",
                abbreviation: null,
                "The name can only contain lowercase letters, numbers and hyphens. It must start and end with a letter or number, and cannot contain two consecutive hyphens. Length: 3 to 63 chars.",
                "az storage share create --name",
                "New-AzStorageShare -Name"),

            new("Container Registry",
                "cr",
                "The name only allows alphanumeric characters. Length: 5 to 50 chars.",
                "cr<prod>[<env>][<id>]",
                ["crnavigatorprod001", "crhadoopdev001"],
                "az acr create --name",
                "New-AzContainerRegistry -Name"),

            new("Storage Account",
                "st",
                "The name can only contain lowercase letters and numbers. Length: 3 to 24 chars.",
                "st<prod>[<env>][<id>]",
                ["stsalesappdataqa", "sthadoopoutputtest"],
                "az storage account create --name",
                "New-AzStorageAccount -Name"),

            new("Traffic Manager profile",
                "tm",
                "The name only allows alphanumeric characters and hyphens, and cannot start or end with a hyphen. Length: 1 to 63 chars.",
                "az network traffic-manager profile create --name",
                "New-AzTrafficManagerProfile -Name"),

            new("Virtual Machine",
                "vm",
                @"The name cannot contain special characters \/""[]:|<>+=;,?*@&#%, whitespace, or begin with '_' or end with '.' or '-'. Length: 1 to 15 chars for Windows; 1 to 64 chars for Linux.",
                "az vm create --name",
                "New-AzVM -Name"),

            new("Virtual Network Gateway",
                "vgw",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network vnet-gateway create --name",
                "New-AzVirtualNetworkGateway -Name"),

            new("Local Network Gateway",
                "lgw",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network local-gateway create --name",
                "New-AzLocalNetworkGateway -Name"),

            new("Virtual Network",
                "vnet",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network vnet create --name",
                "New-AzVirtualNetwork -Name"),

            new("Subnet",
                "snet",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network vnet subnet create --name",
                "Add-AzVirtualNetworkSubnetConfig -Name"),

            new("VPN Connection",
                "vcn",
                "The name only allows alphanumeric characters, underscores, periods, and hyphens. It must start with a letter or number, and end with a letter, number or underscore. Length: 1 to 80 chars.",
                "az network vpn-connection create --name",
                "New-AzVpnConnection -Name"),
        ];

        s_azNamingRules = new(capacity: rules.Count * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            s_azNamingRules.Add(rule.AzCLICommand, rule);
            s_azNamingRules.Add(rule.AzPSCommand, rule);
        }

        (s_azCompleteCmd, s_azCompleteArg) = GetAzCLIPythonPath();
        s_azStaticDataCache = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// TODO: Need to support Linux and macOS.
    /// </summary>
    private static (string compCmd, string compArg) GetAzCLIPythonPath()
    {
        if (OperatingSystem.IsWindows())
        {
            const string AzWinCmd = @"Microsoft SDKs\Azure\CLI2\python.exe";
            const string AzWinArg = "-Im azure.cli";

            string x64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AzWinCmd);
            string x86Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AzWinCmd);

            if (File.Exists(x64Path)) { return (x64Path, AzWinArg); }
            if (File.Exists(x86Path)) { return (x86Path, AzWinArg); }
        }
        else
        {
            // On Linux and macOS, simply run 'az' as starting a new process is cheap.
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (string path in paths)
                {
                    string fullPath = Path.Combine(path, "az");
                    if (File.Exists(fullPath))
                    {
                        return (fullPath, string.Empty);
                    }
                }
            }
        }

        return (null, null);
    }

    internal DataRetriever(ResponseData data, HttpClient httpClient)
    {
        _stop = false;
        _httpClient = httpClient;
        _semaphore = new SemaphoreSlim(WorkerCount, WorkerCount);
        _placeholders = new(capacity: data.PlaceholderSet.Count);
        _placeholderMap = new(capacity: data.PlaceholderSet.Count);

        switch (data.TopicName)
        {
            case CopilotActivity.CLIHandlerTopic:
                PairPlaceholdersForCLICode(data);
                break;
            case CopilotActivity.PSHandlerTopic:
                PairPlaceholdersForPSCode(data);
                break;
            default:
                throw new UnreachableException();
        }

        _rootTask = Task.Run(StartProcessing);
    }

    private void PairPlaceholdersForPSCode(ResponseData data)
    {
        var asts = new Dictionary<string, ScriptBlockAst>(data.CommandSet.Count);

        foreach (var item in data.PlaceholderSet)
        {
            string command = null, parameter = null;
            VariableExpressionAst variable = null;

            foreach (var cmd in data.CommandSet)
            {
                if (!asts.TryGetValue(cmd.Script, out ScriptBlockAst sbAst))
                {
                    sbAst = Parser.ParseInput(cmd.Script, out _, out _);
                    asts.Add(cmd.Script, sbAst);
                }

                if (variable is null)
                {
                    Ast argAst = sbAst.Find(
                        predicate: a => a is StringConstantExpressionAst strConst && strConst.Value == item.Name,
                        searchNestedScriptBlocks: false);

                    if (argAst?.Parent is CommandAst cmdAst)
                    {
                        (command, parameter) = GetCommandAndParameter(cmdAst, argAst);
                        break;
                    }

                    // The generated powershell script may just assign the placeholders to some variables
                    // and use those variables in the command invocation. In that case, we search for those
                    // variables instead.
                    if (argAst?.Parent is CommandExpressionAst cmdExpr &&
                        cmdExpr.Parent is AssignmentStatementAst assignment &&
                        assignment.Left is VariableExpressionAst var)
                    {
                        variable = var;
                    }
                }

                if (variable is not null)
                {
                    Ast argAst = sbAst.Find(
                        predicate: a => a is VariableExpressionAst v && v.VariablePath.UserPath == variable.VariablePath.UserPath && v.Parent is CommandAst,
                        searchNestedScriptBlocks: false);

                    if (argAst is not null)
                    {
                        var cmdAst = (CommandAst)argAst.Parent;
                        (command, parameter) = GetCommandAndParameter(cmdAst, argAst);

                        break;
                    }
                }
            }

            if (command is null)
            {
                // This may happen if the generated PowerShell script assigns the placeholder values to a set of variables at the beginning.
                Log.Debug("[DataRetriever] Failed to pair the placeholder '{0}' for PowerShell code.", item.Name);
            }

            ArgumentPair pair = new(item, command, parameter, CopilotActivity.PSHandlerTopic);
            _placeholders.Add(pair);
            _placeholderMap.Add(item.Name, pair);
        }

        static (string command, string parameter) GetCommandAndParameter(CommandAst cmdAst, Ast argAst)
        {
            string command = ((StringConstantExpressionAst)cmdAst.CommandElements[0]).Value;
            string parameter = null;

            for (int i = 0; i < cmdAst.CommandElements.Count; i++)
            {
                if (cmdAst.CommandElements[i] == argAst)
                {
                    parameter = cmdAst.CommandElements[i-1].Extent.Text;
                    break;
                }
            }

            return (command, parameter);
        }
    }

    private void PairPlaceholdersForCLICode(ResponseData data)
    {
        var cmds = new Dictionary<string, string>(data.CommandSet.Count);

        foreach (var item in data.PlaceholderSet)
        {
            string command = null, parameter = null;

            foreach (var cmd in data.CommandSet)
            {
                bool placeholderFound = false;
                // Az Copilot may return a code block that contains multiple commands.
                string[] scripts = cmd.Script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (string script in scripts)
                {
                    // Handle AzCLI commands.
                    if (script.StartsWith("az ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!cmds.TryGetValue(script, out command))
                        {
                            int firstParamIndex = script.IndexOf("--");
                            command = script.AsSpan(0, firstParamIndex).Trim().ToString();
                            cmds.Add(script, command);
                        }

                        int argIndex = script.IndexOf(item.Name, StringComparison.OrdinalIgnoreCase);
                        if (argIndex is -1)
                        {
                            continue;
                        }

                        // The placeholder value may be enclosed in double or single quotes.
                        if (script[argIndex - 1] is '\'' or '\"')
                        {
                            argIndex--;
                        }

                        // The generated AzCLI command may contain both long (--xxx) and short (-x) flag forms
                        // for its parameters. So we need to properly handle it when looking for the parameter
                        // right before the placeholder value.
                        int paramIndex = 1 + Math.Max(
                            script.LastIndexOf(" --", argIndex),
                            script.LastIndexOf(" -", argIndex));
                        parameter = script.AsSpan(paramIndex, argIndex - paramIndex).Trim().ToString();

                        placeholderFound = true;
                        break;
                    }

                    // It's a non-AzCLI command, such as "ssh".
                    if (script.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Leave the parameter to be null for non-AzCLI commands, as there is
                        // no reliable way to parse an arbitrary command
                        command = script;
                        parameter = null;

                        Log.Debug("[DataRetriever] Non-AzCLI command: '{0}'", command);
                        placeholderFound = true;
                        break;
                    }
                }

                if (placeholderFound)
                {
                    break;
                }
            }

            ArgumentPair pair = new(item, command, parameter, CopilotActivity.CLIHandlerTopic);
            _placeholders.Add(pair);
            _placeholderMap.Add(item.Name, pair);
        }
    }

    private void StartProcessing()
    {
        foreach (var pair in _placeholders)
        {
            if (_stop) { break; }

            _semaphore.Wait();

            if (pair.ArgumentInfo is null)
            {
                lock (pair)
                {
                    if (pair.ArgumentInfo is null)
                    {
                        pair.ArgumentInfo = Task.Factory.StartNew(ProcessOne, pair);
                        continue;
                    }
                }
            }

            _semaphore.Release();
        }

        ArgumentInfo ProcessOne(object pair)
        {
            try
            {
                return CreateArgInfo((ArgumentPair)pair);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    private ArgumentInfo CreateArgInfo(ArgumentPair pair)
    {
        var item = pair.Placeholder;
        var dataType = Enum.Parse<ArgumentInfo.DataType>(item.Type, ignoreCase: true);

        if (item.ValidValues?.Count > 0)
        {
            return new ArgumentInfo(item.Name, item.Desc, restriction: null, dataType, item.ValidValues);
        }

        // Pairing placeholder with command and parameter was not successful.
        // This may happen when the generated code is unexpected or contains commands that is not AzPS or AzCLI.
        if (pair.Command is null || pair.Parameter is null)
        {
            return new ArgumentInfo(item.Name, item.Desc, dataType);
        }

        string cmdAndParam = $"{pair.Command} {pair.Parameter}";
        if (s_azNamingRules.TryGetValue(cmdAndParam, out NamingRule rule))
        {
            string restriction = rule.PatternText is null ? null : $"Recommended pattern: {rule.PatternText}";
            return new ArgumentInfoWithNamingRule(item.Name, item.Desc, restriction, rule);
        }

        if (IsCreatingNewResourceWithAzCLI(pair) || IsCreatingNewResourceWithAzPS(pair))
        {
            // Placeholder is for the name of a new resource to be created, but not in our cache.
            return new ArgumentInfo(item.Name, item.Desc, dataType);
        }

        if (_stop) { return null; }

        IList<string> suggestions = pair.TopicName is CopilotActivity.CLIHandlerTopic
            ? GetArgValuesForAzCLI(pair)
            : GetArgValuesForAzPS(pair);
        return new ArgumentInfo(item.Name, item.Desc, restriction: null, dataType, suggestions);


        /* local helper methods */
        static bool IsCreatingNewResourceWithAzCLI(ArgumentPair pair)
        {
            return pair.TopicName is CopilotActivity.CLIHandlerTopic
                && string.Equals(pair.Parameter, "--name", StringComparison.OrdinalIgnoreCase)
                && pair.Command.EndsWith(" create", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsCreatingNewResourceWithAzPS(ArgumentPair pair)
        {
            return pair.TopicName is CopilotActivity.PSHandlerTopic
                && string.Equals(pair.Parameter, "-Name", StringComparison.OrdinalIgnoreCase)
                && pair.Command.StartsWith("New-", StringComparison.OrdinalIgnoreCase);
        }
    }

    private IList<string> GetArgValuesForAzPS(ArgumentPair pair)
    {
        string command = pair.Command;
        string parameter = pair.Parameter.TrimStart('-');

        Runspace defaultRunspace = Runspace.DefaultRunspace;
        PowerShell pwsh = s_psPool.Checkout();
        Runspace.DefaultRunspace = pwsh.Runspace;

        try
        {
            CommandInfo cmdInfo = null;
            var r = pwsh.AddCommand("Get-Command").AddParameter("Name", command).Invoke<CommandInfo>();
            cmdInfo = r.FirstOrDefault();

            if (cmdInfo is null)
            {
                Log.Debug("[DataRetriever] Cannot find the command '{0}'", command);
                return null;
            }

            if (!cmdInfo.Parameters.TryGetValue(parameter, out ParameterMetadata paramMetadata))
            {
                Log.Debug("[DataRetriever] Cannot find the parameter '{0}' for command '{1}'", parameter, command);
                return null;
            }

            Log.Debug("[DataRetriever] Perform tab completion for '{0} {1} '", command, pair.Parameter);

            if (paramMetadata.ParameterType.IsEnum)
            {
                Log.Debug("[DataRetriever]  - Enum values completion");
                return Enum.GetNames(paramMetadata.ParameterType);
            }

            List<string> returnValues = null;
            foreach (var attribute in paramMetadata.Attributes)
            {
                if (_stop)
                {
                    return null;
                }

                if (attribute is ValidateSetAttribute setAtt)
                {
                    Log.Debug("[DataRetriever]  - ValidateSetAttribute completion");

                    returnValues = new(capacity: setAtt.ValidValues.Count);
                    foreach (string value in setAtt.ValidValues)
                    {
                        if (value != string.Empty)
                        {
                            returnValues.Add(value);
                        }
                    }

                    return returnValues;
                }

                if (attribute is ArgumentCompleterAttribute comAtt)
                {
                    Log.Debug("[DataRetriever]  - ArgumentCompleterAttribute completion");

                    if (comAtt.ScriptBlock is not null)
                    {
                        Log.Debug("[DataRetriever]    - Invoke attr.ScriptBlock");

                        // Today, none of Azure PowerShell's argument completer attributes use the 'CommandAst' and
                        // the fake bound parameters. So we pass 'null' for both of them for simplicity.
                        Collection<PSObject> results = comAtt.ScriptBlock.Invoke(command, parameter, "", null, null);
                        if (results?.Count > 0)
                        {
                            returnValues = new(capacity: results.Count);
                            foreach (var result in results)
                            {
                                if (result.BaseObject is CompletionResult cr)
                                {
                                    returnValues.Add(cr.CompletionText);
                                }
                                else
                                {
                                    returnValues.Add(result.ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        Log.Debug("[DataRetriever]    - Invoke IArgumentCompleter");

                        IArgumentCompleter completer = comAtt.Type is not null
                            ? Activator.CreateInstance(comAtt.Type) as IArgumentCompleter
                            : comAtt is IArgumentCompleterFactory factory
                                ? factory.Create()
                                : null;

                        // Today, Azure PowerShell's argument completer attributes don't use 'CommandAst' and the fake bound parameters.
                        // So we pass 'null' for both of them for simplicity.
                        IEnumerable<CompletionResult> results = completer?.CompleteArgument(command, parameter, "", null, null);
                        if (results is not null)
                        {
                            foreach (var result in results)
                            {
                                returnValues ??= [];
                                returnValues.Add(result.CompletionText);
                            }
                        }
                    }

                    return returnValues;
                }
            }
        }
        catch (Exception e)
        {
            string commandLine = $"{command} {pair.Parameter}";
            Log.Error(e, "[DataRetriever] Exception while performing argument completion for '{0}'", commandLine);
            if (Telemetry.Enabled)
            {
                Dictionary<string, string> details = new()
                {
                    ["Command"] = commandLine,
                    ["Message"] = "Argument completion for AzPS command raised an exception."
                };
                Telemetry.Trace(AzTrace.Exception(details), e);
            }
        }
        finally
        {
            Runspace.DefaultRunspace = defaultRunspace;
            s_psPool.Return(pwsh);
        }

        return null;
    }

    private List<string> GetArgValuesForAzCLI(ArgumentPair pair)
    {
        // First, try to get static argument values if they exist.
        bool hasCompleter = true;
        string command = pair.Command;

        AzCLICommand commandData = s_azStaticDataCache.GetOrAdd(command, QueryForMetadata);
        AzCLIParameter param = commandData?.FindParameter(pair.Parameter);

        if (param is not null)
        {
            if (param.Choices?.Count > 0)
            {
                return param.Choices;
            }

            hasCompleter = param.HasCompleter;
        }

        if (_stop || !hasCompleter || s_azCompleteCmd is null) { return null; }

        // Then, try to get dynamic argument values using AzCLI tab completion.
        string commandLine = $"{pair.Command} {pair.Parameter} ";
        string tempFile = Path.GetTempFileName();

        Log.Debug("[DataRetriever] Perform tab completion for '{0}'", commandLine);

        try
        {
            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = s_azCompleteCmd,
                    Arguments = s_azCompleteArg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // Redirect stdin to force installing a missing extension, otherwise 'az'
                    // may prompt interactively for user's approval to install.
                    RedirectStandardInput = true,
                }
            };

            var env = process.StartInfo.Environment;
            env.Add("ARGCOMPLETE_USE_TEMPFILES", "1");
            env.Add("_ARGCOMPLETE_STDOUT_FILENAME", tempFile);
            env.Add("COMP_LINE", commandLine);
            env.Add("COMP_POINT", (commandLine.Length + 1).ToString());
            env.Add("_ARGCOMPLETE", "1");
            env.Add("_ARGCOMPLETE_SUPPRESS_SPACE", "0");
            env.Add("_ARGCOMPLETE_IFS", "\n");
            env.Add("_ARGCOMPLETE_SHELL", "powershell");

            process.Start();
            process.WaitForExit();

            string line;
            using FileStream stream = File.OpenRead(tempFile);
            if (stream.Length is 0)
            {
                // No allowed values for the option.
                return null;
            }

            using StreamReader reader = new(stream);
            List<string> output = [];

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith('-'))
                {
                    // Argument completion generates incorrect results -- options are written into the file instead of argument allowed values.
                    return null;
                }

                string value = line.Trim();
                if (value != string.Empty)
                {
                    output.Add(value);
                }
            }

            return output.Count > 0 ? output : null;
        }
        catch (Win32Exception e)
        {
            throw new ApplicationException($"Failed to get allowed values for 'az {commandLine}': {e.Message}", e);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private AzCLICommand QueryForMetadata(string azCommand)
    {
        AzCLICommand command = null;
        var reqBody = new StringContent(string.Format(MetadataQueryTemplate, azCommand), Encoding.UTF8, Utils.JsonContentType);
        var request = new HttpRequestMessage(HttpMethod.Get, MetadataEndpoint) { Content = reqBody };

        try
        {
            using var cts = new CancellationTokenSource(1200);
            var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            using Stream stream = response.Content.ReadAsStream(cts.Token);
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement root = document.RootElement;
            if (root.TryGetProperty("data", out JsonElement data) &&
                data.TryGetProperty("metadata", out JsonElement metadata))
            {
                command = metadata.Deserialize<AzCLICommand>(Utils.JsonOptions);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "[QueryForMetadata] Exception while processing command: {0}", azCommand);
            if (Telemetry.Enabled)
            {
                Dictionary<string, string> details = new()
                {
                    ["Command"] = azCommand,
                    ["Message"] = "AzCLI metadata query and process raised an exception."
                };
                Telemetry.Trace(AzTrace.Exception(details), e);
            }
        }

        return command;
    }

    internal (string command, string parameter) GetMappedCommand(string placeholderName)
    {
        if (_placeholderMap.TryGetValue(placeholderName, out ArgumentPair pair))
        {
            return (pair.Command, pair.Parameter);
        }

        throw new ArgumentException($"Unknown placeholder name: '{placeholderName}'", nameof(placeholderName));
    }

    internal Task<ArgumentInfo> GetArgInfo(string placeholderName)
    {
        if (_placeholderMap.TryGetValue(placeholderName, out ArgumentPair pair))
        {
            if (pair.ArgumentInfo is null)
            {
                lock (pair)
                {
                    pair.ArgumentInfo ??= Task.Run(() => CreateArgInfo(pair));
                }
            }

            return pair.ArgumentInfo;
        }

        throw new ArgumentException($"Unknown placeholder name: '{placeholderName}'", nameof(placeholderName));
    }

    public void Dispose()
    {
        _stop = true;
        _rootTask.Wait();
        _semaphore.Dispose();
    }

    internal static void WarmUpMetadataService(HttpClient httpClient)
    {
        // Send a request to the AzCLI metadata service to warm up the service (code start is slow).
        // We query for the command 'az sql server list' which only has 2 parameters,
        // so it should cause minimum processing on the server side.
        HttpRequestMessage request = new(HttpMethod.Get, MetadataEndpoint)
        {
            Content = new StringContent(
                "{\"command\":\"az sql server list\"}",
                Encoding.UTF8,
                Utils.JsonContentType)
        };

        _ = httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
}

internal class ArgumentPair
{
    internal PlaceholderItem Placeholder { get; }
    internal string Command { get; }
    internal string Parameter { get; }
    internal string TopicName { get; }
    internal Task<ArgumentInfo> ArgumentInfo { set; get; }

    internal ArgumentPair(PlaceholderItem placeholder, string command, string parameter, string topicName)
    {
        Placeholder = placeholder;
        Command = command;
        Parameter = parameter;
        TopicName = topicName;
        ArgumentInfo = null;
    }
}

internal class ArgumentInfoWithNamingRule : ArgumentInfo
{
    internal ArgumentInfoWithNamingRule(string name, string description, string restriction, NamingRule rule)
        : base(name, description, restriction, DataType.@string, suggestions: [])
    {
        ArgumentNullException.ThrowIfNull(rule);
        NamingRule = rule;
    }

    internal NamingRule NamingRule { get; }
}

internal class NamingRule
{
    private static readonly string[] s_products = ["salesapp", "bookingweb", "navigator", "hadoop", "sharepoint"];
    private static readonly string[] s_envs = ["prod", "dev", "qa", "stage", "test"];

    internal string ResourceName { get; }
    internal string Abbreviation { get; }
    internal string GeneralRule { get; }
    internal string PatternText { get; }
    internal Regex PatternRegex { get; }
    internal string[] Example { get; }

    internal string AzCLICommand { get; }
    internal string AzPSCommand { get; }

    internal NamingRule(
        string resourceName,
        string abbreviation,
        string generalRule,
        string azCLICommand,
        string azPSCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceName);
        ArgumentException.ThrowIfNullOrEmpty(generalRule);
        ArgumentException.ThrowIfNullOrEmpty(azCLICommand);
        ArgumentException.ThrowIfNullOrEmpty(azPSCommand);

        ResourceName = resourceName;
        Abbreviation = abbreviation;
        GeneralRule = generalRule;
        AzCLICommand = azCLICommand;
        AzPSCommand = azPSCommand;

        if (abbreviation is not null)
        {
            PatternText = $"<prod>-{abbreviation}[-<env>][-<id>]";
            PatternRegex = new Regex($"^(?<prod>[a-zA-Z0-9]+)-{abbreviation}(?:-(?<env>[a-zA-Z0-9]+))?(?:-[a-zA-Z0-9]+)?$", RegexOptions.Compiled);

            string product = s_products[Random.Shared.Next(0, s_products.Length)];
            int envIndex = Random.Shared.Next(0, s_envs.Length);
            Example = [$"{product}-{abbreviation}-{s_envs[envIndex]}", $"{product}-{abbreviation}-{s_envs[(envIndex + 1) % s_envs.Length]}"];
        }
    }

    internal NamingRule(
        string resourceName,
        string abbreviation,
        string generalRule,
        string patternText,
        string[] example,
        string azCLICommand,
        string azPSCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceName);
        ArgumentException.ThrowIfNullOrEmpty(generalRule);
        ArgumentException.ThrowIfNullOrEmpty(azCLICommand);
        ArgumentException.ThrowIfNullOrEmpty(azPSCommand);

        ResourceName = resourceName;
        Abbreviation = abbreviation;
        GeneralRule = generalRule;
        PatternText = patternText;
        PatternRegex = null;
        Example = example;

        AzCLICommand = azCLICommand;
        AzPSCommand = azPSCommand;
    }

    internal bool TryMatchName(string name, out string prodName, out string envName)
    {
        prodName = envName = null;
        if (PatternRegex is null)
        {
            return false;
        }

        Match match = PatternRegex.Match(name);
        if (match.Success)
        {
            prodName = match.Groups["prod"].Value;
            envName = match.Groups["env"].Value;
            return true;
        }

        return false;
    }
}
