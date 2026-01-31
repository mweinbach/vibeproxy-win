namespace VibeProxy.Core.Services;

public record AuthResult(bool Ok, string Message, string? DeviceCode = null);