using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task JwtProposesWithoutLogging_ThenCommitsServerComputedDraft_OnlyOnce()
    {
        var (client, _, services, jwt) = await factory.CreateAuthenticatedRealStoreClientAsync(
            "mcp-immediate-commit",
            builder => builder.UseSetting("Mcp:MinCommitDelaySeconds", "0"));
        var store = services.GetRequiredService<ITableStore>();
        var foodId = Guid.NewGuid();
        await store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = foodId,
            Name = "MCP test chicken",
            Calories100g = 200m,
            Protein100g = 20m,
            Carbs100g = 0m,
            Fat100g = 10m,
            Fiber100g = 0m,
            ServingQuantity = 100m,
            ServingSize = "100 g",
            DataSource = "Manual"
        });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var before = await client.GetAsync($"/api/meals?date={today}&tzOffsetMinutes=0");
        before.EnsureSuccessStatusCode();
        var mealsBefore = await before.Content.ReadFromJsonAsync<JsonElement>();
        var countBefore = mealsBefore.GetArrayLength();
        client.DefaultRequestHeaders.Authorization = null;
        var init = await PostRpcAsync(client, null, InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, init.Status);
        await client.PostAsync("/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        var proposed = await CallToolAsync(client, jwt, "gutai_propose_meal", new
        {
            mealType = "Lunch",
            items = JsonSerializer.Serialize(new[]
            {
                new { food_product_id = foodId, name = "MCP test chicken", servings = 1, serving_weight_g = 150, match_confidence = 0.98 }
            })
        });
        Assert.Equal(HttpStatusCode.OK, proposed.Status);
        Assert.True(proposed.Response.TryGetProperty("result", out var proposeResult));
        Assert.False(ResultIsError(proposeResult), ToolErrorText(proposeResult));
        var proposal = JsonSerializer.Deserialize<JsonElement>(
            proposeResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.True(proposal.GetProperty("draft_id").GetGuid() != Guid.Empty);
        Assert.Equal("Lunch", proposal.GetProperty("meal_type").GetString());
        var proposalItems = proposal.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, proposalItems.ValueKind);
        Assert.Equal(1, proposalItems.GetArrayLength());
        var proposalItem = proposalItems[0];
        Assert.True(proposalItem.GetProperty("item_id").GetGuid() != Guid.Empty);
        Assert.Equal("MCP test chicken", proposalItem.GetProperty("name").GetString());
        Assert.Equal(150m, proposalItem.GetProperty("grams").GetDecimal());
        Assert.Equal(300m, proposalItem.GetProperty("calories").GetDecimal());
        Assert.Equal("Sourced", proposalItem.GetProperty("provenance").GetString());
        Assert.False(proposalItem.GetProperty("needs_choice").GetBoolean());
        var totals = proposal.GetProperty("totals");
        Assert.Equal(300m, totals.GetProperty("calories").GetDecimal());
        Assert.Equal(30m, totals.GetProperty("protein_g").GetDecimal());
        Assert.Equal(0m, totals.GetProperty("carbs_g").GetDecimal());
        Assert.Equal(15m, totals.GetProperty("fat_g").GetDecimal());
        Assert.Equal(0, totals.GetProperty("items_without_nutrition").GetInt32());
        Assert.Equal("Nothing is logged yet. The user can confirm this draft in the GutAI app, or call gutai_commit_meal after they confirm.",
            proposal.GetProperty("note").GetString());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var afterProposal = await client.GetAsync($"/api/meals?date={today}&tzOffsetMinutes=0");
        afterProposal.EnsureSuccessStatusCode();
        Assert.Equal(countBefore, (await afterProposal.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        client.DefaultRequestHeaders.Authorization = null;

        var draftId = proposal.GetProperty("draft_id").GetString()!;
        var committed = await CallToolAsync(client, jwt, "gutai_commit_meal", new { draftId });
        Assert.True(committed.Response.TryGetProperty("result", out var commitResult));
        Assert.False(ResultIsError(commitResult), ToolErrorText(commitResult));
        var committedPayload = JsonSerializer.Deserialize<JsonElement>(
            commitResult.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.True(committedPayload.GetProperty("meal_id").GetGuid() != Guid.Empty);
        Assert.Equal(300m, committedPayload.GetProperty("total_calories").GetDecimal());
        Assert.Equal(30m, committedPayload.GetProperty("total_protein_g").GetDecimal());
        Assert.Equal(0m, committedPayload.GetProperty("total_carbs_g").GetDecimal());
        Assert.Equal(15m, committedPayload.GetProperty("total_fat_g").GetDecimal());
        Assert.Equal(1, committedPayload.GetProperty("item_count").GetInt32());
        Assert.Equal(0, committedPayload.GetProperty("items_without_nutrition").GetInt32());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var afterCommit = await client.GetAsync($"/api/meals?date={today}&tzOffsetMinutes=0");
        afterCommit.EnsureSuccessStatusCode();
        var mealsAfter = await afterCommit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(countBefore + 1, mealsAfter.GetArrayLength());
        Assert.Contains(mealsAfter.EnumerateArray(), meal =>
            meal.GetProperty("id").GetGuid() == committedPayload.GetProperty("meal_id").GetGuid()
            && meal.GetProperty("totalCalories").GetDecimal() == 300m);
        client.DefaultRequestHeaders.Authorization = null;

        var duplicate = await CallToolAsync(client, jwt, "gutai_commit_meal", new { draftId });
        Assert.True(ResultIsError(duplicate.Response.GetProperty("result")),
            "a committed draft cannot be committed a second time");
    }

    [Fact]
    public async Task ImmediateMcpCommitRequiresReviewAndDoesNotLogMeal()
    {
        var (client, _, services, jwt) = await factory.CreateAuthenticatedRealStoreClientAsync();
        var store = services.GetRequiredService<ITableStore>();
        var foodId = Guid.NewGuid();
        await store.UpsertFoodProductAsync(new FoodProduct
        {
            Id = foodId,
            Name = "MCP immediate-commit chicken",
            Calories100g = 180m,
            Protein100g = 22m,
            Carbs100g = 0m,
            Fat100g = 8m,
            ServingQuantity = 100m,
            DataSource = "Manual"
        });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        client.DefaultRequestHeaders.Authorization = null;
        var init = await PostRpcAsync(client, null, InitializeRequest);
        Assert.Equal(HttpStatusCode.OK, init.Status);
        await client.PostAsync("/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        var proposed = await CallToolAsync(client, jwt, "gutai_propose_meal", new
        {
            mealType = "Lunch",
            items = JsonSerializer.Serialize(new[]
            {
                new { food_product_id = foodId, name = "MCP immediate-commit chicken", servings = 1, serving_weight_g = 100 }
            })
        });
        Assert.False(ResultIsError(proposed.Response.GetProperty("result")));
        var proposal = JsonSerializer.Deserialize<JsonElement>(
            proposed.Response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);

        var commit = await CallToolAsync(client, jwt, "gutai_commit_meal",
            new { draftId = proposal.GetProperty("draft_id").GetString() });
        Assert.True(ResultIsError(commit.Response.GetProperty("result")));
        var error = ToolErrorText(commit.Response.GetProperty("result"));
        Assert.Contains("review", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was logged", error, StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var meals = await client.GetAsync($"/api/meals?date={today}&tzOffsetMinutes=0");
        meals.EnsureSuccessStatusCode();
        Assert.Equal(0, (await meals.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task PatCannotProposeMeal()
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

        var proposal = await CallToolAsync(client, pat, "gutai_propose_meal", new
        {
            mealType = "Lunch",

            items = """[{"name":"chicken"}]"""
        });
        Assert.True(proposal.Response.TryGetProperty("result", out var result));
        Assert.True(ResultIsError(result), "PAT-linked sessions are read-only and must not create meal drafts");
        Assert.Contains("read-only", ToolErrorText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadOnlyPatCanSearchFoodsWithoutReturningEmptyIds()
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

        var search = await CallToolAsync(client, pat, "gutai_search_foods", new { query = "mcp-readonly-search-no-persist" });
        Assert.Equal(HttpStatusCode.OK, search.Status);
        Assert.True(search.Response.TryGetProperty("result", out var result));
        Assert.False(ResultIsError(result), ToolErrorText(result));
        var payload = JsonSerializer.Deserialize<JsonElement>(
            result.GetProperty("content")[0].GetProperty("text").GetString()!);
        var results = payload.GetProperty("results");
        Assert.Equal(JsonValueKind.Array, results.ValueKind);
        foreach (var item in results.EnumerateArray())
        {
            var id = item.GetProperty("id");
            Assert.True(id.ValueKind == JsonValueKind.Null
                || (id.ValueKind == JsonValueKind.String && Guid.Parse(id.GetString()!) != Guid.Empty),
                $"read-only search returned a non-linkable empty id: {id}");
        }
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
