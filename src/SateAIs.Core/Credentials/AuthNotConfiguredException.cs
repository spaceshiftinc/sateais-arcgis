using SateAIs.Core.Api;

namespace SateAIs.Core.Credentials;

/// <summary>No API key could be resolved from any source.</summary>
public sealed class AuthNotConfiguredException() : SateAIsException("No API key is configured.");
