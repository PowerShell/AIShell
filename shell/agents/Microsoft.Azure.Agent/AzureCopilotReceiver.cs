using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Microsoft.Azure.Agent;

internal class AzureCopilotReceiver : IDisposable
{
    private const int BufferSize = 4096;

    private readonly byte[] _buffer;
    private readonly ClientWebSocket _webSocket;
    private readonly MemoryStream _memoryStream;
    private readonly CancellationTokenSource _cancelMessageReceiving;
    private readonly BlockingCollection<CopilotActivity> _activityQueue;

    private AzureCopilotReceiver(ClientWebSocket webSocket)
    {
        _webSocket = webSocket;
        _buffer = new byte[BufferSize];
        _memoryStream = new MemoryStream();
        _cancelMessageReceiving = new CancellationTokenSource();
        _activityQueue = new BlockingCollection<CopilotActivity>();

        Watermark = -1;
    }

    internal int Watermark { get; private set; }
    internal BlockingCollection<CopilotActivity> ActivityQueue => _activityQueue;

    internal static async Task<AzureCopilotReceiver> CreateAsync(string streamUrl)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new Uri(streamUrl), CancellationToken.None);

        var copilotReader = new AzureCopilotReceiver(webSocket);
        _ = Task.Run(copilotReader.ProcessActivities);

        return copilotReader;
    }

    private async Task ProcessActivities()
    {
        while (_webSocket.State is WebSocketState.Open)
        {
            string closingMessage = null;
            WebSocketReceiveResult result = null;

            try
            {
                result = await _webSocket.ReceiveAsync(_buffer, _cancelMessageReceiving.Token);
                if (result.MessageType is WebSocketMessageType.Close)
                {
                    closingMessage = "Close message received";
                }
            }
            catch (OperationCanceledException)
            {
                // TODO: log the cancellation of the message receiving thread.
                // Close the web socket before the thread is going away.
                closingMessage = "Client closing";
            }

            if (closingMessage is not null)
            {
                // TODO: log the closing request.
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, closingMessage, CancellationToken.None);
                break;
            }

            // Occasionally, the Direct Line service sends an empty message as a liveness ping.
            // We simply ignore these messages.
            if (result.Count is 0)
            {
                continue;
            }

            _memoryStream.Write(_buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                _memoryStream.Position = 0;
                var rawResponse =  JsonSerializer.Deserialize<RawResponse>(_memoryStream, Utils.JsonOptions);
                _memoryStream.SetLength(0);

                if (rawResponse.Watermark is not null)
                {
                    Watermark = int.Parse(rawResponse.Watermark);
                }

                foreach (CopilotActivity activity in rawResponse.Activities)
                {
                    if (activity.IsFromCopilot)
                    {
                        _activityQueue.Add(activity);
                    }
                }
            }
        }

        // TODO: log the current state of the web socket
        // TODO: handle error state, such as 'aborted'
    }

    public void Dispose()
    {
        _webSocket.Dispose();
        _cancelMessageReceiving.Cancel();
    }
}
