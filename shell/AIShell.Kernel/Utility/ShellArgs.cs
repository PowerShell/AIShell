using AIShell.Abstraction;

namespace AIShell.Kernel;

/// <summary>
/// Arguments for the AI shell.
/// </summary>
public sealed class ShellArgs(ShellWrapper shellWrapper, string channel, string agent)
{
    /// <summary>
    /// Gets the named pipe used to setup communication between aish and the command-line shell.
    /// </summary>
    public string Channel { get; } = string.IsNullOrEmpty(channel) ? null : channel;

    /// <summary>
    /// Gets the shell wrapper configuration.
    /// </summary>
    public ShellWrapper ShellWrapper { get; } = shellWrapper;

    /// <summary>
    /// Gets the AI agent name to use for this session.
    /// </summary>
    public string Agent { get; } = string.IsNullOrEmpty(agent) ? null : agent;
}
