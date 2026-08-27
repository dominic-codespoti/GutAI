using System.ComponentModel;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GutAI.Api.Mcp;

[McpServerToolType]
public class LinkTools(IPairingService pairing, ILogger<LinkTools> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [AllowAnonymous]
    [McpServerTool(Name = "gutai_link_account", ReadOnly = true)]
    [Description("Link the user's GutAI account to this AI assistant. Ask the user to open GutAI → Settings → " +
                 "Connected AI Assistants and share the 8-character pairing code (valid ~10 minutes). " +
                 "Returns an access token the user must add to their client configuration for this MCP server " +
                 "(Authorization: Bearer header) — all other gutai_* tools require it. Read-only access.")]
    public async Task<string> LinkAccount(
        [Description("The 8-character pairing code shown in the GutAI app, e.g. 'ABCD-EFGH'")] string pairingCode,
        CancellationToken ct)
    {
        try
        {
            var redeemed = await pairing.RedeemPairingCodeAsync(pairingCode, ct);
            return JsonSerializer.Serialize(new
            {
                accessToken = redeemed.AccessToken,
                tokenType = "Bearer",
                scopes = redeemed.Token.Scopes,
                linkedEmail = redeemed.Token.Email,
                instructions = "Connection created. The user should add this token as an " +
                               "'Authorization: Bearer <token>' header on the GutAI MCP server entry in their " +
                               "client configuration, then reconnect. Treat it as a secret — never repeat it " +
                               "back to the user.",
            }, JsonOpts);
        }
        catch (PairingCodeInvalidException)
        {
            throw new McpException(
                "That pairing code is invalid, expired, or already used. Ask the user to generate a fresh one in " +
                "GutAI → Settings → Connected AI Assistants.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "gutai_link_account failed");
            throw new McpException("Could not complete the link. Please try again.");
        }
    }
}
