using Microsoft.ML.Tokenizers;

namespace AIShell.OpenAI.Agent;

internal class ModelInfo
{
    // Models gpt4, gpt3.5, and the variants of them all use the 'cl100k_base' token encoding.
    // But gpt-4o and o1 models use the 'o200k_base' token encoding. For reference:
    //   https://github.com/openai/tiktoken/blob/63527649963def8c759b0f91f2eb69a40934e468/tiktoken/model.py
    private const string Gpt4oEncoding = "o200k_base";
    private const string Gpt34Encoding = "cl100k_base";

    private static readonly Dictionary<string, ModelInfo> s_modelMap;
    private static readonly Dictionary<string, Task<Tokenizer>> s_encodingMap;

    // A rough estimate to cover all third-party models.
    //  - most popular models today support 32K+ context length;
    //  - use the gpt-4o encoding as an estimate for token count.
    internal static readonly ModelInfo ThirdPartyModel = new(32_000, encoding: Gpt4oEncoding);

    static ModelInfo()
    {
        // For reference, see https://platform.openai.com/docs/models and the "Counting tokens" section in
        // https://github.com/openai/openai-cookbook/blob/main/examples/How_to_format_inputs_to_ChatGPT_models.ipynb
        // https://github.com/openai/openai-cookbook/blob/main/examples/How_to_count_tokens_with_tiktoken.ipynb
        s_modelMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["o1"]            = new(tokenLimit: 200_000, encoding: Gpt4oEncoding, reasoning: true),
            ["o3"]            = new(tokenLimit: 200_000, encoding: Gpt4oEncoding, reasoning: true),
            ["o4-mini"]       = new(tokenLimit: 200_000, encoding: Gpt4oEncoding, reasoning: true),
            ["gpt-4.1"]       = new(tokenLimit: 1_047_576, encoding: Gpt4oEncoding),
            ["gpt-4o"]        = new(tokenLimit: 128_000, encoding: Gpt4oEncoding),
            ["gpt-4"]         = new(tokenLimit: 8_192),
            ["gpt-4-32k"]     = new(tokenLimit: 32_768),
            ["gpt-4-turbo"]   = new(tokenLimit: 128_000),
            ["gpt-3.5-turbo"] = new(tokenLimit: 16_385),
            // Azure naming of the 'gpt-3.5-turbo' models
            ["gpt-35-turbo"]  = new(tokenLimit: 16_385),
        };

        // The first call to 'GptEncoding.GetEncoding' is very slow, taking about 2 seconds on my machine.
        // We don't immediately need the encodings at the startup, so by getting the values in tasks,
        // we don't block the startup and the values will be ready when we really need them.
        s_encodingMap = new(StringComparer.OrdinalIgnoreCase)
        {
            [Gpt34Encoding] = Task.Run(() => (Tokenizer)TiktokenTokenizer.CreateForEncoding(Gpt34Encoding)),
            [Gpt4oEncoding] = Task.Run(() => (Tokenizer)TiktokenTokenizer.CreateForEncoding(Gpt4oEncoding))
        };
    }

    private ModelInfo(int tokenLimit, string encoding = null, bool reasoning = false)
    {
        TokenLimit = tokenLimit;
        _encodingName = encoding ?? Gpt34Encoding;

        // For gpt4o, gpt4 and gpt3.5-turbo, the following 2 properties are the same.
        // See https://github.com/openai/openai-cookbook/blob/main/examples/How_to_count_tokens_with_tiktoken.ipynb
        TokensPerMessage = 3;
        TokensPerName = 1;
        Reasoning = reasoning;
    }

    private readonly string _encodingName;
    private Tokenizer _gptEncoding;

    internal int TokenLimit { get; }
    internal int TokensPerMessage { get; }
    internal int TokensPerName { get; }
    internal bool Reasoning { get; }
    internal Tokenizer Encoding
    {
        get {
            _gptEncoding ??= s_encodingMap.TryGetValue(_encodingName, out Task<Tokenizer> value)
                ? value.Result
                : TiktokenTokenizer.CreateForEncoding(_encodingName);
            return _gptEncoding;
        }
    }

    /// <summary>
    /// Try resolving the specified model name.
    /// </summary>
    internal static bool TryResolve(string name, out ModelInfo model)
    {
        if (s_modelMap.TryGetValue(name, out model))
        {
            return true;
        }

        int lastDashIndex = name.LastIndexOf('-');
        while (lastDashIndex > 0)
        {
            string parentName = name[..lastDashIndex];
            if (s_modelMap.TryGetValue(parentName, out model))
            {
                return true;
            }

            lastDashIndex = parentName.LastIndexOf('-');
        }

        return false;
    }

    internal static ModelInfo GetByName(string name)
    {
        return s_modelMap[name] ?? throw new ArgumentException($"Invalid key '{name}'", nameof(name));
    }

    internal static IEnumerable<string> SupportedModels()
    {
        return s_modelMap.Keys.SkipWhile(n => n.StartsWith("gpt-35")).OrderDescending();
    }
}
