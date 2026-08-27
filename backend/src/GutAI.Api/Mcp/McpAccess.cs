using System.Security.Claims;
using GutAI.Api.Auth;
using ModelContextProtocol;

namespace GutAI.Api.Mcp;

/// <summary>
/// Scope enforcement for MCP tools. Interactive JWT users (app + coach chat) have full
/// access; PAT-linked AI consumers are limited to their granted scopes. Every mutating
/// tool MUST call <see cref="EnsureWrite"/> before its first side effect.
/// </summary>
public static class McpAccess
{
    public const string ReadScope = "read";
    public const string WriteScope = "write";

    /// <summary>Throws unless the caller is an interactive JWT user or holds the write scope.</summary>
    public static void EnsureWrite(ClaimsPrincipal user)
    {
        if (user.FindFirst(PatAuthenticationHandler.TokenTypeClaim)?.Value != PatAuthenticationHandler.PatTokenType)
            return; // JWT session — interactive access

        if (!user.FindAll(PatAuthenticationHandler.ScopeClaim).Any(c => c.Value == WriteScope))
            throw new McpException(
                "This AI connection is read-only. The user can grant write access by removing and re-linking " +
                "the connection in GutAI → Settings → Connected AI Assistants once write-scoped links are offered.");
    }
}
