// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ColorCode.Common;

namespace ColorCode.VT;

public class PowerShell : ILanguage
{
    public string Id
    {
        get { return LanguageId.PowerShell; }
    }

    public string Name
    {
        get { return "PowerShell"; }
    }

    public string CssClassName
    {
        get { return "powershell"; }
    }

    public string FirstLinePattern
    {
        get { return null; }
    }

    public IList<LanguageRule> Rules
    {
        get
        {
            return new List<LanguageRule>
                {
                    new LanguageRule(
                        @"(?s)(<#.*?#>)",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.Comment}
                            }),

                    new LanguageRule(
                        @"(#.*?)\r?$",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.Comment}
                            }),

                    new LanguageRule(
                        @"'[^\n]*?'",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.String}
                            }),

                    new LanguageRule(
                        @"(?s)@"".*?""@",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.StringCSharpVerbatim}
                            }),

                    new LanguageRule(
                        @"""[^\n]*?(?<!`)""",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.String}
                            }),

                    new LanguageRule(
                        @"\$(?:[\d\w\-]+(?::[\d\w\-]+)?|\$|\?|\^)",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellVariable}
                            }),

                    new LanguageRule(
                        @"\${[^}]+}",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellVariable}
                            }),

                    new LanguageRule(
                        @"(?i)\b(begin|break|catch|continue|data|do|dynamicparam|elseif|else|end|exit|filter|finally|foreach|for|from|function|if|in|param|process|return|switch|throw|trap|try|until|while)\b",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.Keyword}
                            }),

                    // We use positive lookbehind assertion to indicate what should immediately precedes a command name.
                    // By using the positive lookbehind assertion, the operators like '|' and '=' can still be matched
                    // by their respective rules.
                    new LanguageRule(
                        @"(?<=(?:^|[\(\|=])\s*)(?:\w+-)*\w+",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellCommand}
                            }),

                    new LanguageRule(
                        @"-(?:c|i)?(?:eq|ne|gt|ge|lt|le|notlike|like|notmatch|match|notcontains|contains|replace)",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellOperator}
                            }),

                    new LanguageRule(
                        @"-(?:band|and|as|join|not|bxor|xor|bor|or|isnot|is|split)",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellOperator}
                            }),

                    // Match parameters like '-word'. Note that we require a preceding whitespace.
                    new LanguageRule(
                        @"\s(-\w+)",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.PowerShellParameter}
                            }),

                    // match options like '--word', '--word-word', and '--word-word-word', but not '--word-' or '--word-word-'.
                    // Also match potential value for the option that is specified in the form of '--word=value', but we don't
                    // capture the value part because it should be rendered as plain text, and our real purpose is to not let
                    // the value part to be matched by other rules.
                    new LanguageRule(
                        @"\s(--(?:\w+-)*\w+)(?:=(?:\w+-)*\w+)?",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.PowerShellParameter}
                            }),

                    new LanguageRule(
                        @"(?:\+=|-=|\*=|/=|%=|=|\+\+|--|\+|-|\*|/|%|\||,)",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellOperator}
                            }),

                    new LanguageRule(
                        @"(?:\>\>|2\>&1|\>|2\>\>|2\>)",
                        new Dictionary<int, string>
                            {
                                {0, ScopeName.PowerShellOperator}
                            }),

                    new LanguageRule(
                        @"(?is)\[(cmdletbinding|alias|outputtype|parameter|validatenotnull|validatenotnullorempty|validatecount|validateset|allownull|allowemptycollection|allowemptystring|validatescript|validaterange|validatepattern|validatelength|supportswildcards)[^\]]+\]",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.PowerShellAttribute}
                            }),

                    new LanguageRule(
                        @"(\[)([^\]]+)(\])(::)?",
                        new Dictionary<int, string>
                            {
                                {1, ScopeName.PowerShellOperator},
                                {2, ScopeName.PowerShellType},
                                {3, ScopeName.PowerShellOperator},
                                {4, ScopeName.PowerShellOperator}
                            })
                };
        }
    }

    public bool HasAlias(string lang)
    {
        switch (lang.ToLower())
        {
            case "posh":
            case "ps1":
            case "pwsh":
                return true;

            default:
                return false;
        }
    }
}
