namespace VibeProxy.Core.Models;

public enum ServiceType
{
    Claude,
    Codex,
    Copilot,
    Gemini,
    Qwen,
    Antigravity,
    Zai
}

public static class ServiceTypeExtensions
{
    public static string DisplayName(this ServiceType type) => type switch
    {
        ServiceType.Claude => "Claude Code",
        ServiceType.Codex => "Codex",
        ServiceType.Copilot => "GitHub Copilot",
        ServiceType.Gemini => "Gemini",
        ServiceType.Qwen => "Qwen",
        ServiceType.Antigravity => "Antigravity",
        ServiceType.Zai => "Z.AI GLM",
        _ => type.ToString()
    };

    public static string AuthTypeKey(this ServiceType type) => type switch
    {
        ServiceType.Copilot => "github-copilot",
        _ => type.ToString().ToLowerInvariant()
    };
}