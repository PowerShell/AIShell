
using Microsoft.Windows.AI.Generative;

using AIShell.Abstraction;
using Microsoft.Windows.AI;

namespace AIShell.PhiSilica.Agent
{
    public class PhiSilicaAgent : ILLMAgent
    {
        public string Name => "PhiSilica";

        public string Description => "This is the Phi Silica agent, an offline local agent on Copilot+ PCs";

        public string SettingFile => null;

        public bool CanAcceptFeedback(UserAction action) => false;


        public async Task<bool> ChatAsync(string input, IShell shell)
        {
            IHost host = shell.Host;
            if (LanguageModel.GetReadyState() == AIFeatureReadyState.EnsureNeeded)
            {
                var op = await LanguageModel.EnsureReadyAsync();

            }

            var languageModel = await LanguageModel.CreateAsync();

            var results = await languageModel.GenerateResponseAsync(input);

            if (results != null && !string.IsNullOrEmpty(results.Text))
            {
                host.RenderFullResponse(results.Text);
            }
            else
            {
                host.WriteErrorLine("No response received from the language model.");
            }
            //host.RenderFullResponse("Goodbye World");

            return true;
        }

        public void Dispose()
        {
           
        }

        public IEnumerable<CommandBase> GetCommands() => null;

        public void Initialize(AgentConfig config)
        {
            
        }

        public void OnUserAction(UserActionPayload actionPayload)
        {
            
        }

        public Task RefreshChatAsync(IShell shell, bool force)
        {
            return Task.CompletedTask;
        }
    }

}
