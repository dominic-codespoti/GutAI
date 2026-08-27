using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GutAI.Api.Tests;

/// <summary>
/// Drives the real Streamable-HTTP MCP surface end to end: anonymous pairing-tool
/// exchange, PAT-authenticated data access, scope rejection, and the MCP-only token
/// boundary. Raw JSON-RPC (no MCP client SDK) so the transport contract itself is
/// what's under test.
/// </summary>
[Collection("WebApi")]
public class McpLinkFlowTests(GutAiWebFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly object InitializeRequest = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "gutai-contract-tests", version = "1.0" }
        }
    };

    /// <summary>Full happy path: pair → link via anonymous MCP tool → use PAT on a data tool.</summary>
    [Fact]
    public async Task PairingCode_LinksViaMcp_AndPatReadsProfile()
    {
        var client = factory.CreateClient();
        var (email, jwt, pairingCode) = await RegisterAndIssueCodeAsync(client);

        // ── Anonymous MCP session: initialize, then exchange the code ──
        var init = await PostRpcAsync(client, null, InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, init.Status);
        Assert.True(init.Response.TryGetProperty("result", out _), "initialize must succeed");

        await client.PostAsync("/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));

        var link = await CallToolAsync(client, null, "gutai_link_account",
            new { pairingCode = pairingCode });
        Assert.True(link.Response.TryGetProperty("result", out var linkResult), $"link tool must succeed, got: {link.Response}");
        Assert.False(ResultIsError(linkResult), $"link tool must not error: {ToolErrorText(linkResult)}");

        var linkPayload = JsonSerializer.Deserialize<JsonElement>(linkResult.GetProperty("content")[0]
            .GetProperty("text").GetString()!);
        var pat = linkPayload.GetProperty("accessToken").GetString()!;
        Assert.StartsWith("gutai_pat_", pat);
        Assert.Equal("Bearer", linkPayload.GetProperty("tokenType").GetString());
        Assert.Equal(email, linkPayload.GetProperty("linkedEmail").GetString());

        // A second data tool proves the PAT identity path is not specific to one handler.
        var meals = await CallToolAsync(client, pat, "gutai_get_todays_meals", new { });
        Assert.True(meals.Response.TryGetProperty("result", out var mr), $"meals result: {meals.Response}");
        Assert.False(ResultIsError(mr), $"pat meals errored: {ToolErrorText(mr)}");

        // ── PAT grants read access to the user's own profile through the tool ──
        var profile = await CallToolAsync(client, pat, "gutai_get_user_profile", new { });
        Assert.True(profile.Response.TryGetProperty("result", out var profileResult), $"profile call must return a result, got: {profile.Response}");
        Assert.False(ResultIsError(profileResult), $"profile tool must not error: {ToolErrorText(profileResult)}");
        var profilePayload = JsonSerializer.Deserialize<JsonElement>(profileResult.GetProperty("content")[0]
            .GetProperty("text").GetString()!);
        Assert.Equal("MCP Test", profilePayload.GetProperty("displayName").GetString());

        // ── Read-only scope blocks mutation ──
        var write = await CallToolAsync(client, pat, "gutai_log_symptom",
            new { symptomName = "Bloating", severity = 5 });
        var writeText = write.Response.ToString();
        Assert.True(writeText.Contains("read-only"), $"expected read-only rejection, got: {writeText}");
    }

    /// <summary>Protected data tools reject unauthenticated sessions outright.</summary>
    [Fact]
    public async Task DataTool_WithoutToken_IsRejected()
    {
        var client = factory.CreateClient();

        var init = await PostRpcAsync(client, null, InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, init.Status);
        await client.PostAsync("/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));

        var profile = await CallToolAsync(client, null, "gutai_get_user_profile", new { });

        // Authorization filter either fails the HTTP request or returns an error result.
        var rejected = profile.Status == HttpStatusCode.Unauthorized
            || !profile.Response.TryGetProperty("result", out var result)
            || (result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.True(rejected, $"unauthenticated data-tool call must be rejected, got: {profile.Response}");
    }

    /// <summary>The PAT hard boundary: pairing tokens cannot drive the REST API.</summary>
    [Fact]
    public async Task Pat_IsRejectedOnRestEndpoints()
    {
        var client = factory.CreateClient();
        var (_, _, pairingCode) = await RegisterAndIssueCodeAsync(client);

        await PostRpcAsync(client, null, InitializeRequest);
        await client.PostAsync("/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        var link = await CallToolAsync(client, null, "gutai_link_account", new { pairingCode });
        var linkPayload = JsonSerializer.Deserialize<JsonElement>(link.Response.GetProperty("result")
            .GetProperty("content")[0].GetProperty("text").GetString()!);
        var pat = linkPayload.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        var rest = await client.GetAsync("/api/user/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
    }

    private static async Task<(string Email, string Jwt, string PairingCode)> RegisterAndIssueCodeAsync(
        HttpClient client)
    {
        var email = $"mcp-{Guid.NewGuid():N}@test.com";
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123",
            displayName = "MCP Test"
        });
        register.EnsureSuccessStatusCode();
        var auth = await register.Content.ReadFromJsonAsync<JsonElement>(Json);
        var jwt = auth.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/user/pairing-codes");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var issued = await client.SendAsync(request);
        issued.EnsureSuccessStatusCode();
        var code = await issued.Content.ReadFromJsonAsync<JsonElement>(Json);
        return (email, jwt, code.GetProperty("code").GetString()!);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Response)> PostRpcAsync(
        HttpClient client, string? bearer, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2025-06-18");
        if (bearer != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!raw.TrimStart().StartsWith("{") && !raw.Contains("data:"))
            throw new Xunit.Sdk.XunitException(
                $"RPC returned HTTP {(int)response.StatusCode} {response.StatusCode} " +
                $"content-type={response.Content.Headers.ContentType} body='{raw[..Math.Min(raw.Length, 400)]}'");
        return (response.StatusCode, ParseRpcResponse(raw));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Response)> CallToolAsync(
        HttpClient client, string? bearer, string tool, object arguments) =>
        await PostRpcAsync(client, bearer, new
        {
            jsonrpc = "2.0",
            id = Random.Shared.Next(100, int.MaxValue),
            method = "tools/call",
            @params = new { name = tool, arguments },
        });

    /// <summary>Accepts both plain application/json responses and SSE-framed data lines.</summary>
    private static JsonElement ParseRpcResponse(string raw)
    {
        if (raw.TrimStart().StartsWith("{"))
            return JsonSerializer.Deserialize<JsonElement>(raw, Json);

        var dataLines = raw.Split('\n')
            .Where(l => l.StartsWith("data:", StringComparison.Ordinal))
            .Select(l => l["data:".Length..].Trim())
            .FirstOrDefault(l => l.Length > 0);
        Assert.False(dataLines == null, $"no JSON-RPC payload in response: {raw}");
        return JsonSerializer.Deserialize<JsonElement>(dataLines!, Json);
    }

    private static bool ResultIsError(JsonElement result) =>
        result.TryGetProperty("isError", out var isError) && isError.GetBoolean();

    private static string ToolErrorText(JsonElement result) =>
        result.TryGetProperty("content", out var content) && content.GetArrayLength() > 0
            ? content[0].TryGetProperty("text", out var text) ? text.GetString() : content[0].ToString()
            : result.ToString();
}
