﻿using ShellCopilot.Abstraction;
using System.ComponentModel;
using System.Diagnostics;

namespace ShellCopilot.Interpreter.Agent;

/// <summary>
/// This class is used to execute powershell code on the local machine. It inherits most functionality 
/// from the SubprocessLanguage class while implementing the PreprocessCode method for non-function calling
/// AIs.
/// </summary>
internal class PowerShell: SubprocessLanguage
{
    internal PowerShell()
    {
        // -NoProfile prevents the profile from loading and -file - reads the code from stdin
        StartCmd = ["pwsh.exe", "-NoProfile -Command -"];
        VersionCmd = ["pwsh.exe", "--version"];
        OutputQueue = new Queue<Dictionary<string, string>>();
    }

    protected override string PreprocessCode(string code)
    {
        string try_catch_code = """
try {
    $ErrorActionPreference = 'Stop'
""";
        code = code.TrimEnd();
        code = try_catch_code + "\n    " + code + "\n} catch {\n    [Console]::Error.WriteLine($_)\r\n    [Console]::Error.WriteLine($_.InvocationInfo.PositionMessage)\n}\n";
        // Add end marker (listen for this in HandleStreamOutput to know when code ends)
        code += "\nWrite-Host '##end_of_execution##'";
        return code;
    }

    protected override void WriteToProcess(string code)
    {
        Process.StandardInput.WriteLine(code);
        Process.StandardInput.Flush();
    }
}
