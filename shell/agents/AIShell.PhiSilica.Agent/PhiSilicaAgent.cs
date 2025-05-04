using AIShell.Abstraction;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Generative;

namespace AIShell.PhiSilica.Agent;

public sealed partial class PhiSilicaAgent : ILLMAgent
{
    private readonly Task _initTask;
    private LanguageModel _model;

    public string Name => "PhiSilica";
    public string Description => "This is the AI Shell Agent for talking to the inbox Phi Silica model on Copilot+ PCs.";
    public string SettingFile => null;

    public IEnumerable<CommandBase> GetCommands() => null;
    public bool CanAcceptFeedback(UserAction action) => false;
    public Task RefreshChatAsync(IShell shell, bool force) => Task.CompletedTask;
    public void OnUserAction(UserActionPayload actionPayload) { }
    public void Initialize(AgentConfig config) { }
    public void Dispose() { }

    public PhiSilicaAgent()
    {
        // Start the initialization for AI feature and model on a background thread.
        _initTask = Task.Run(InitFeatureAndModelAsync);
    }

    private async Task InitFeatureAndModelAsync()
    {
        AIFeatureReadyState state = LanguageModel.GetReadyState();
        if (state is AIFeatureReadyState.NotSupportedOnCurrentSystem)
        {
            throw new PlatformNotSupportedException("The Phi Silica feature is not supported on current system.");
        }

        if (state is AIFeatureReadyState.DisabledByUser)
        {
            throw new PlatformNotSupportedException("The Phi Silica feature is currently disabled.");
        }

        if (state is AIFeatureReadyState.EnsureNeeded)
        {
            // Initialize the WinRT runtime.
            AIFeatureReadyResult result = await LanguageModel.EnsureReadyAsync();
            // Do not proceed if it failed to get the feature ready.
            if (result.Status is not AIFeatureReadyResultState.Success)
            {
                throw new InvalidOperationException(result.ErrorDisplayText, result.Error);
            }
        }

        _model = await LanguageModel.CreateAsync();
    }

    public async Task<bool> ChatAsync(string input, IShell shell)
    {
        IHost host = shell.Host;

        try
        {
            // Wait for the init task to finish. Once it's finished, calling this again is a non-op.
            await _initTask;
        }
        catch (Exception e)
        {
            host.WriteErrorLine(e.Message);
            if (e is InvalidOperationException && e.InnerException is not null)
            {
                host.WriteErrorLine(e.InnerException.StackTrace);
            }
            else if (e is not PlatformNotSupportedException)
            {
                // Show stack trace for non-PNS exception.
                host.WriteErrorLine(e.StackTrace);
            }

            return false;
        }

        var result = await host.RunWithSpinnerAsync(
            status: "Thinking ...",
            func: async () => await _model.GenerateResponseAsync(input)
        );

        if (result is not null && !string.IsNullOrEmpty(result.Text))
        {
            host.RenderFullResponse(result.Text);
        }
        else
        {
            host.WriteErrorLine("No response received from the language model.");
        }

        return true;
    }
}
