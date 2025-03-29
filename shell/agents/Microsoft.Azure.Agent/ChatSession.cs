using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using AIShell.Abstraction;
using Serilog;

namespace Microsoft.Azure.Agent;

internal class ChatSession : IDisposable
{
    private const string ACCESS_URL = "https://copilotweb.production.portalrp.azure.com/api/access?api-version=2024-09-01";
    private const string DL_TOKEN_URL = "https://copilotweb.production.portalrp.azure.com/api/conversations/start?api-version=2024-11-15";

    internal bool UserAuthorized { get; private set; }

    private string _streamUrl;
    private string _dlBaseUrl;
    private string _conversationId;
    private string _conversationUrl;
    private UserDirectLineToken _directLineToken;
    private AzureCopilotReceiver _copilotReceiver;

    private readonly HttpClient _httpClient;
    private readonly UserAccessToken _accessToken;
    private readonly Dictionary<string, object> _flights;

    internal ChatSession(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _accessToken = new UserAccessToken();

        // Keys and values for flights are from the portal request.
        _flights = new Dictionary<string, object>()
        {
            ["openAIModel"] = "gpt4optum",
            ["openAIEndpointName"] = "australiaeast,norwayeast",
            ["docsHandlerEndpoint"] = "learnDocs",
            ["unifiedcopilotdebug"] = false,
            ["unifiedcopilottest"] = false,
            ["captureintent"] = "true",
            ["capturereasoning"] = "true",
            ["captureparameters"] = "true",
            ["logconversationturn"] = true,
            ["aacopilot"] = true,
            ["corecopilotmanager"] = true,
            ["copilotdirectlinetokennameobjectid"] = true,
            ["disableserverhandlerids"] = "SupportAndTroubleshootBot",
            ["allowcopilotpaneresize"] = true,
            ["copilotmanageability"] = true,
            ["gpt4tcsprompt"] = true,
            ["copilotmanageabilityuimenu"] = true,
            ["getformstate"] = true,
            ["chitchatprompt"] = true,
            ["azurepluginstore"] = true,
            ["pipelineorchestration"] = true,
            // TODO: the streaming is slow and not sending chunks, very clumsy for now.
            // ["streamresponse"] = true,
        };
    }

    internal async Task<string> RefreshAsync(IStatusContext context, bool force, CancellationToken cancellationToken)
    {
        if (_directLineToken is not null)
        {
            if (force)
            {
                // End the existing conversation.
                context.Status("Ending current chat ...");
                EndCurrentConversation();
            }
            else
            {
                try
                {
                    context.Status("Refreshing tokens ...");
                    await _accessToken.CreateOrRenewTokenAsync(cancellationToken);
                    await _directLineToken.RenewTokenAsync(_httpClient, cancellationToken);

                    // Tokens successfully refreshed.
                    return null;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Refreshing failed. We will create a new chat session.
                }
            }
        }

        return await SetupNewChat(context, cancellationToken);
    }

    private void Reset()
    {
        _streamUrl = null;
        _dlBaseUrl = null;
        _conversationId = null;
        _conversationUrl = null;
        _directLineToken = null;

        _accessToken.Reset();
        _copilotReceiver?.Dispose();
        _copilotReceiver = null;
    }

    private async Task<string> SetupNewChat(IStatusContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.Status("Get Azure login token ...");
            // Get an access token from the Azure CLI/PowerShell login, using the specific audience guid.
            await _accessToken.CreateOrRenewTokenAsync(cancellationToken);

            context.Status("Check Copilot authorization ...");
            await CheckAuthorizationAsync(cancellationToken);

            context.Status("Start a new chat session ...");
            await GetInitialDLTokenAsync(cancellationToken);
            return await OpenConversationAsync(cancellationToken);
        }
        catch (Exception e)
        {
            if (e is not OperationCanceledException and not TokenRequestException)
            {
                // Trace a telemetry for any unexpected error.
                Telemetry.Trace(AzTrace.Exception("Failed to setup a new chat session."), e);
            }

            Reset();
            throw;
        }
    }

    private async Task CheckAuthorizationAsync(CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, ACCESS_URL);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken.Token);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await response.EnsureSuccessStatusCodeForTokenRequest("Failed to check Copilot authorization.");

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var permission = JsonSerializer.Deserialize<CopilotPermission>(stream, Utils.JsonOptions);
        UserAuthorized = permission.Authorized;

        if (!UserAuthorized)
        {
            string message = $"Access token not authorized to access Azure Copilot. {permission.Message}";
            Telemetry.Trace(AzTrace.Exception(message));
            throw new TokenRequestException(message) { UserUnauthorized = true };
        }
    }

    private async Task GetInitialDLTokenAsync(CancellationToken cancellationToken)
    {
        StringContent content = new("{\"conversationType\": \"Chat\"}", Encoding.UTF8, Utils.JsonContentType);
        HttpRequestMessage request = new(HttpMethod.Post, DL_TOKEN_URL) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken.Token);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await response.EnsureSuccessStatusCodeForTokenRequest("Failed to generate the initial DL token.");

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dlToken = JsonSerializer.Deserialize<DirectLineToken>(stream, Utils.JsonOptions);

        _dlBaseUrl = dlToken.DirectLine.Endpoint;
        _directLineToken = new UserDirectLineToken(dlToken.DirectLine.Token, dlToken.DirectLine.TokenExpiryTimeInSeconds, _dlBaseUrl);
    }

    private async Task<string> OpenConversationAsync(CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"{_dlBaseUrl}/conversations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _directLineToken.Token);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await response.EnsureSuccessStatusCodeForTokenRequest("Failed to open an conversation.");

        using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        SessionPayload spl = JsonSerializer.Deserialize<SessionPayload>(content, Utils.JsonOptions);

        _conversationId = spl.ConversationId;
        _conversationUrl = $"{_dlBaseUrl}/conversations/{_conversationId}/activities";
        _directLineToken = new UserDirectLineToken(spl.Token, spl.ExpiresIn, _dlBaseUrl);
        _streamUrl = spl.StreamUrl;
        _copilotReceiver = await AzureCopilotReceiver.CreateAsync(_streamUrl);

        Log.Debug("[ChatSession] Conversation started. Id: {0}", _conversationId);

        while (true)
        {
            CopilotActivity activity = _copilotReceiver.Take(cancellationToken);
            if (activity.IsMessage && activity.IsFromCopilot && _copilotReceiver.Watermark is 0)
            {
                activity.ExtractMetadata(out _, out ConversationState conversationState);
                int chatNumber = conversationState.DailyConversationNumber;
                return $"{activity.Text} This is chat #{chatNumber}.\n\n\x1b[96mAI Generated content may be incorrect.\x1b[0m\n";
            }
        }
    }

    private HttpRequestMessage PrepareForChat(string input)
    {
        var requestData = new
        {
            type = "message",
            locale = CultureInfo.CurrentCulture.Name,
            from = new { id = "user" },  // TODO, create a uuid to represent the user.
            text = input,
            attachments = new object[] {
                new {
                    contentType = Utils.JsonContentType,
                    name = "azurecopilot/clienthandlerdefinitions",
                    content =  new {
                        clientHandlers = Array.Empty<object>()
                    }
                },
                new {
                    contentType = Utils.JsonContentType,
                    name = "azurecopilot/viewcontext",
                    content = new {
                        viewContext = new {
                            view = new {},
                            additionalDetails = "{\"allContext\":[],\"additionalDetailsString\":\"{}\"}",
                            resourceDetails = new {}
                        }
                    }
                },
                new {
                    contentType = Utils.JsonContentType,
                    name = "azurecopilot/flights",
                    content = new {
                        flights = _flights
                    }
                },
                new {
                    contentType = Utils.JsonContentType,
                    name = "azurecopilot/authorization",
                    content = $"Bearer {_accessToken.Token}"
                }
            },
        };

        var json = JsonSerializer.Serialize(requestData, Utils.JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, Utils.JsonContentType);
        var request = new HttpRequestMessage(HttpMethod.Post, _conversationUrl) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _directLineToken.Token);
        // This header is for server side telemetry to identify where the request comes from.
        request.Headers.Add("ClientType", "AIShell");
        return request;
    }

    private async Task<string> SendQueryToCopilot(string input, CancellationToken cancellationToken)
    {
        // Sending query to Azure Copilot.
        HttpRequestMessage request = PrepareForChat(input);
        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Retrieve the activity id of this query.
        using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonObject contentObj = JsonNode.Parse(content).AsObject();
        return contentObj["id"].ToString();
    }

    private void EndCurrentConversation()
    {
        if (_directLineToken is null || _directLineToken.CheckTokenHealth() is TokenHealth.Expired)
        {
            // Chat session already expired, no need to send request to end the conversation.
            return;
        }

        var content = new StringContent("{\"type\":\"endOfConversation\",\"from\":{\"id\":\"user\"}}", Encoding.UTF8, Utils.JsonContentType);
        var request = new HttpRequestMessage(HttpMethod.Post, _conversationUrl) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _directLineToken.Token);
        _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
    }

    internal async Task<CopilotResponse> GetChatResponseAsync(string input, IStatusContext context, CancellationToken cancellationToken)
    {
        if (_directLineToken is null)
        {
            throw new TokenRequestException("A chat session hasn't been setup yet.");
        }

        try
        {
            context.Status("Refreshing tokens ...");
            await _accessToken.CreateOrRenewTokenAsync(cancellationToken);
            await _directLineToken.RenewTokenAsync(_httpClient, cancellationToken);

            context?.Status("Sending query ...");
            string activityId = await SendQueryToCopilot(input, cancellationToken);

            while (true)
            {
                CopilotActivity activity = _copilotReceiver.Take(cancellationToken);

                if (activity.ReplyToId != activityId)
                {
                    // Ignore an activity if it's not a reply to the current activityId.
                    // This may happen when user cancels a response and thus the response activities from copilot were not consumed.
                    continue;
                }

                if (activity.IsTyping)
                {
                    if (activity.TopicName is CopilotActivity.ProgressTopic)
                    {
                        context?.Status(activity.Text);
                    }
                    continue;
                }

                if (activity.IsMessage)
                {
                    CopilotResponse ret = activity.InputHint switch
                    {
                        "typing"         => new CopilotResponse(activity, new ChunkReader(_copilotReceiver, activity)),
                        "acceptingInput" => new CopilotResponse(activity),
                        "error"          => new CopilotResponse(activity) { IsError = true },
                        _ => throw CorruptDataException.Create($"The 'inputHint' is {activity.InputHint}.", activity)
                    };

                    if (ret.ChunkReader is null)
                    {
                        activity.ExtractMetadata(out string[] suggestion, out ConversationState state);
                        ret.SuggestedUserResponses = suggestion;
                        ret.ConversationState = state;
                    }

                    ret.ConversationId = _conversationId;
                    return ret;
                }

                throw CorruptDataException.Create($"The 'type' is '{activity.Type}', but we only expect 'typing' and 'message' as we don't support any client handlers.", activity);
            }
        }
        catch (OperationCanceledException)
        {
            // TODO: we may need to notify azure copilot somehow about the cancellation.
            return null;
        }
        catch (TokenRequestException)
        {
            Reset();
            throw;
        }
    }

    public void Dispose()
    {
        EndCurrentConversation();
        _copilotReceiver?.Dispose();
    }
}
