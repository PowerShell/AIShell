using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using AIShell.Abstraction;
using Azure.Core;
using Azure.Identity;

namespace Microsoft.Azure.Agent;

internal class AgentSetting
{
    public bool Logging { get; set; }
    public bool Telemetry { get; set; }

    public AgentSetting()
    {
        // Enable logging and telemetry by default.
        Logging = true;
        Telemetry = true;
    }

    internal static AgentSetting Default => new();

    internal static AgentSetting LoadFromFile(string path)
    {
        FileInfo file = new(path);
        if (file.Exists)
        {
            try
            {
                using var stream = file.OpenRead();
                return JsonSerializer.Deserialize<AgentSetting>(stream, Utils.JsonOptions);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Parsing settings from '{path}' failed with the following error: {e.Message}", e);
            }
        }

        return null;
    }

    internal static void NewSettingFile(string path)
    {
        const string content = """
            {
              "logging": true,
              "telemetry": true
            }
            """;
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}

internal enum TokenHealth
{
    Good,
    TimeToRefresh,
    Expired
}

internal class CopilotPermission
{
    public bool Authorized { get; set; }
    public string Message { get; set; }
}

internal class RefreshDLToken
{
    public string ConversationId { get; set; }
    public string Token { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class NewDLToken
{
    public string Endpoint { get; set; }
    public string Token { get; set; }
    public int TokenExpiryTimeInSeconds { get; set; }
}

internal class DirectLineToken
{
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public NewDLToken DirectLine { get; set; }
}

internal class SessionPayload
{
    public string ConversationId { get; set; }
    public string Token { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    public string StreamUrl { get; set; }
    public string ReferenceGrammarId { get; set; }
}

/// <summary>
/// Interface for copilot channel account.
/// </summary>
internal class ChannelAccount
{
    public string Id { get; set; }
    public string Name { get; set; }
};

/// <summary>
/// Interface for copilot activity data.
/// </summary>
internal class CopilotActivity
{
    public const string ConversationStateName = "azurecopilot/conversationstate";
    public const string SuggestedResponseName = "azurecopilot/suggesteduserresponses";
    public const string CLIHandlerTopic = "generate_azure_cli_scripts";
    public const string PSHandlerTopic = "generate_powershell_script";

    public string Type { get; set; }
    public string Id { get; set; }
    public string Timestamp { get; set; }
    public string ChannelId { get; set; }
    public ChannelAccount From { get; set; }
    public string TopicName { get; set; }
    public string Text { get; set; }
    public string InputHint { get; set; }
    public string Locale { get; set; }
    public JsonObject[] Attachments { get; set; }
    public string ReplyToId { get; set; }

    internal Exception Error { get; set; }

    internal bool IsFromCopilot => From.Id.StartsWith("copilot", StringComparison.OrdinalIgnoreCase);
    internal bool IsTyping => Type is "typing";
    internal bool IsEvent => Type is "event";
    internal bool IsMessage => Type is "message";
    internal bool IsMessageUpdate => Type is "messageUpdate";

    internal void ExtractMetadata(out string[] suggestedUserResponses, out ConversationState conversationState)
    {
        suggestedUserResponses = null;
        conversationState = null;

        foreach (JsonObject jObj in Attachments)
        {
            string name = jObj["name"].ToString();
            JsonNode content = jObj["content"];

            if (name is CopilotActivity.SuggestedResponseName)
            {
                suggestedUserResponses = content.Deserialize<string[]>(Utils.JsonOptions);
            }
            else if (name is CopilotActivity.ConversationStateName)
            {
                conversationState = content.Deserialize<ConversationState>(Utils.JsonOptions);
            }
        }
    }

    internal string Serialize() => JsonSerializer.Serialize(this, Utils.JsonHumanReadableOptions);
};

internal class ConversationState
{
    public int TurnLimit { get; set; }
    public int TurnNumber { get; set; }
    public bool TurnLimitMet { get; set; }
    public bool TurnLimitExceeded { get; set; }

    public int DailyConversationNumber { get; set; }
    public bool DailyConversationLimitMet { get; set; }
    public bool DailyConversationLimitExceeded { get; set; }
}

internal class CopilotResponse
{
    internal CopilotResponse(CopilotActivity activity, ChunkReader chunkReader)
        : this(activity)
    {
        ArgumentNullException.ThrowIfNull(chunkReader);
        ChunkReader = chunkReader;
    }

    internal CopilotResponse(CopilotActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        Text = activity.Text;
        Locale = activity.Locale;
        TopicName = activity.TopicName;
        ReplyToId = activity.ReplyToId;
    }

    internal ChunkReader ChunkReader { get; }
    internal string Text { get; }
    internal string Locale { get; }
    internal string TopicName { get; }
    internal string ReplyToId { get; }

    internal bool IsError { get; set; }
    internal string ConversationId { get; set; }
    internal string[] SuggestedUserResponses { get; set; }
    internal ConversationState ConversationState { get; set; }
}

internal class RawResponse
{
    public List<CopilotActivity> Activities { get; set; }
    public string Watermark { get; set; }
}

internal class ChunkReader
{
    private readonly string _replyToId;
    private readonly AzureCopilotReceiver _receiver;
    private CopilotActivity _current;
    private bool _complete;

    internal ChunkReader(AzureCopilotReceiver receiver, CopilotActivity current)
    {
        _replyToId = current.ReplyToId;
        _receiver = receiver;
        _current = current;
        _complete = false;
    }

    internal CopilotActivity ReadChunk(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            CopilotActivity ret = _current;
            _current = null;
            return ret;
        }

        if (_complete)
        {
            return null;
        }

        CopilotActivity activity = _receiver.Take(cancellationToken);

        if (!activity.IsMessageUpdate)
        {
            throw CorruptDataException.Create($"The 'type' should be 'messageUpdate' but it's '{activity.Type}'.", activity);
        }
        if (activity.ReplyToId != _replyToId)
        {
            throw CorruptDataException.Create($"The 'replyToId' should be '{_replyToId}', but it's '{activity.ReplyToId}'.", activity);
        }
        if (activity.InputHint is not "typing" and not "acceptingInput")
        {
            throw CorruptDataException.Create($"The 'inputHint' should be 'typing' or 'acceptingInput', but it's '{activity.InputHint}'.", activity);
        }

        _complete = activity.InputHint is "acceptingInput";

        return activity;
    }
}

#region parameter injection data types

internal class CommandItem
{
    /// <summary>
    /// Indicates whether the <see cref="Script"/> was updated by replacing placeholders with actual values.
    /// </summary>
    internal bool Updated { get; set; }

    /// <summary>
    /// The command script.
    /// </summary>
    internal string Script { get; set; }

    /// <summary>
    /// The source extent information.
    /// </summary>
    internal SourceInfo SourceInfo { get; set; }
}

internal class PlaceholderItem
{
    public string Name { get; set; }
    public string Desc { get; set; }
    public string Type { get; set; }
    public List<string> ValidValues { get; set; }
}

internal class ResponseData
{
    internal string Text { get; set; }
    internal string Locale { get; set; }
    internal string TopicName { get; set; }
    internal List<CommandItem> CommandSet { get; set; }
    internal List<PlaceholderItem> PlaceholderSet { get; set; }
}

internal class ArgumentPlaceholder
{
    internal ArgumentPlaceholder(string query, ResponseData data, HttpClient httpClient)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(data);

        Query = query;
        ResponseData = data;
        DataRetriever = new(data, httpClient);
    }

    public string Query { get; set; }
    public ResponseData ResponseData { get; set; }
    public DataRetriever DataRetriever { get; }
}

internal class AzCLIParameter
{
    public List<string> Options { get; set; }
    public List<string> Choices { get; set; }
    public bool Required { get; set; }

    [JsonPropertyName("has_completer")]
    public bool HasCompleter { get; set; }
}

internal class AzCLICommand
{
    public List<AzCLIParameter> Parameters { get; set; }

    public AzCLIParameter FindParameter(string name)
    {
        foreach (var param in Parameters)
        {
            foreach (var option in param.Options)
            {
                if (option.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return param;
                }
            }
        }

        return null;
    }
}

#endregion

#region token wrappers

internal class UserAccessToken
{
    private readonly TokenRequestContext _tokenContext;
    private AccessToken? _accessToken;

    /// <summary>
    /// The access token.
    /// </summary>
    internal string Token => _accessToken?.Token;

    /// <summary>
    /// Initialize an instance with the proper token request context.
    /// </summary>
    internal UserAccessToken()
    {
        _tokenContext = new TokenRequestContext(["7000789f-b583-4714-ab18-aef39213018a/.default"]);
    }

    /// <summary>
    /// Create an access token, or renew an existing token.
    /// </summary>
    internal async Task CreateOrRenewTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            bool needRefresh = !_accessToken.HasValue;
            if (!needRefresh)
            {
                needRefresh = DateTimeOffset.UtcNow.AddMinutes(5) > _accessToken.Value.ExpiresOn;
            }

            if (needRefresh)
            {
                _accessToken = await new ChainedTokenCredential(
                    new AzureCliCredential(),
                    new AzurePowerShellCredential()
                ).GetTokenAsync(_tokenContext, cancellationToken);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            string message = $"Failed to generate the user access token: {e.Message}.";
            Telemetry.Trace(AzTrace.Exception(message), e);
            throw new TokenRequestException(message, e);
        }
    }

    /// <summary>
    /// Reset the access token.
    /// </summary>
    internal void Reset()
    {
        _accessToken = null;
    }
}

internal class UserDirectLineToken
{
    private string _token;
    private DateTimeOffset _expireOn;
    private readonly string _tokenRenewUrl;

    /// <summary>
    /// The DirectLine token.
    /// </summary>
    internal string Token => _token;

    /// <summary>
    /// Initialize an instance.
    /// </summary>
    internal UserDirectLineToken(string token, int expiresInSec, string dlBaseUrl)
    {
        _token = token;
        _expireOn = DateTimeOffset.UtcNow.AddSeconds(expiresInSec);
        _tokenRenewUrl = $"{dlBaseUrl}/tokens/refresh";
    }

    /// <summary>
    /// Check the token health.
    /// </summary>
    /// <returns></returns>
    internal TokenHealth CheckTokenHealth()
    {
        var now = DateTimeOffset.UtcNow;
        if (now > _expireOn || now.AddMinutes(2) >= _expireOn)
        {
            return TokenHealth.Expired;
        }

        if (now.AddMinutes(10) < _expireOn)
        {
            return TokenHealth.Good;
        }

        return TokenHealth.TimeToRefresh;
    }

    /// <summary>
    /// Renew the DirectLine token.
    /// </summary>
    internal async Task RenewTokenAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        TokenHealth health = CheckTokenHealth();
        if (health is TokenHealth.Expired)
        {
            throw new TokenRequestException("The chat session has expired. Please start a new chat session.");
        }

        if (health is TokenHealth.Good)
        {
            return;
        }

        try
        {
            HttpRequestMessage request = new(HttpMethod.Post, _tokenRenewUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            RefreshDLToken dlToken = JsonSerializer.Deserialize<RefreshDLToken>(content, Utils.JsonOptions);

            _token = dlToken.Token;
            _expireOn = DateTimeOffset.UtcNow.AddSeconds(dlToken.ExpiresIn);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            string message = $"Failed to renew the 'DirectLine' token: {e.Message}.";
            Telemetry.Trace(AzTrace.Exception(message), e);
            throw new TokenRequestException(message, e);
        }
    }
}

#endregion
