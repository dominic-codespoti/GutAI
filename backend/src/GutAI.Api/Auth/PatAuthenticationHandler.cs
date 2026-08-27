using System.Security.Claims;
using System.Text.Encodings.Web;
using GutAI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GutAI.Api.Auth;

/// <summary>
/// Bearer authentication for AI-consumer personal access tokens ("gutai_pat_...").
/// Runs as the second scheme behind the MultiAuth policy-scheme selector, which only
/// routes pat-prefixed headers here — JWTs continue through the JwtBearer scheme.
/// Tokens are valid exclusively for MCP traffic so a leaked token cannot drive the
/// REST API.
/// </summary>
public class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITableStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Pat";
    public const string TokenPrefix = "gutai_pat_";
    public const string TokenTypeClaim = "token_type";
    public const string PatTokenType = "pat";
    public const string ScopeClaim = "scope";

    private static readonly TimeSpan LastUsedWriteThrottle = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Only ever reached with a pat-prefixed header (see Program.cs forwarding).
        var token = Request.Headers.Authorization.ToString();
        var prefix = "Bearer ";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        token = token[prefix.Length..].Trim();
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.Fail("Malformed access token.");
        // Hard boundary: a leaked pairing token cannot drive the REST API — it exists
        // solely for MCP tool traffic. Fail (not NoResult): the MultiAuth selector has
        // already committed this header to the Pat scheme, so falling through would
        // surface as an anonymous request instead of a clear rejection.
        if (!Request.Path.StartsWithSegments("/mcp"))
            return AuthenticateResult.Fail("Access tokens are valid only for the MCP endpoint.");

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));
        var pat = await store.GetPersonalAccessTokenByHashAsync(hash);
        if (pat is null || !pat.IsActive)
            return AuthenticateResult.Fail("Access token is invalid, expired, or revoked.");

        // Bound write amplification from chatty agents while keeping "last active"
        // fresh enough for the user-facing device list.
        if (pat.LastUsedAt is null || DateTime.UtcNow - pat.LastUsedAt > LastUsedWriteThrottle)
        {
            pat.LastUsedAt = DateTime.UtcNow;
            await store.UpsertPersonalAccessTokenAsync(pat);
        }

        var claims = new List<Claim>
        {
            new("sub", pat.UserId.ToString()),
            new("email", pat.Email),
            new(TokenTypeClaim, PatTokenType),
        };
        claims.AddRange(pat.Scopes.Select(s => new Claim(ScopeClaim, s)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{SchemeName} realm=\"gutai-mcp\"";
        return base.HandleChallengeAsync(properties);
    }
}
