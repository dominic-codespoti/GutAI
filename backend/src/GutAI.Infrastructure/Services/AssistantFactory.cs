#pragma warning disable OPENAI001

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using OpenAI.Assistants;

namespace GutAI.Infrastructure.Services;

public class AssistantFactory
{
    private readonly AssistantClient _client;
    private readonly string _model;
    private readonly string _instructions;
    private readonly IReadOnlyList<FunctionToolDefinition> _tools;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TableClient _table;
    private string? _assistantId;
    private readonly ILogger<AssistantFactory> _logger;

    public AssistantFactory(
        AssistantClient client,
        string model,
        string instructions,
        IReadOnlyList<FunctionToolDefinition> tools,
        TableServiceClient tableServiceClient,
        ILogger<AssistantFactory> logger)
    {
        _client = client;
        _model = model;
        _instructions = instructions;
        _tools = tools;
        _table = tableServiceClient.GetTableClient("gutai");
        _logger = logger;
    }

    public async Task<string> GetAssistantIdAsync(CancellationToken ct)
    {
        if (_assistantId is not null) return _assistantId;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_assistantId is not null) return _assistantId;

            var persisted = await TryGetPersistedAssistantIdAsync(ct);
            if (persisted is not null && await AssistantStillExistsAsync(persisted, ct))
            {
                _assistantId = persisted;
                _logger.LogInformation("Reusing persisted assistant {AssistantId}", _assistantId);
                return _assistantId;
            }

            var options = new AssistantCreationOptions
            {
                Name = "GutAI Coach",
                Instructions = _instructions,
            };
            foreach (var tool in _tools)
            {
                options.Tools.Add(tool);
            }

            var assistant = await _client.CreateAssistantAsync(_model, options, ct);

            _assistantId = assistant.Value.Id;
            await PersistAssistantIdAsync(_assistantId, ct);
            _logger.LogInformation("Created assistant {AssistantId}", _assistantId);
            return _assistantId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create assistant");
            _assistantId = null;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string?> TryGetPersistedAssistantIdAsync(CancellationToken ct)
    {
        try
        {
            await _table.CreateIfNotExistsAsync(ct);
            var response = await _table.GetEntityAsync<TableEntity>("SYSTEM", "AssistantId", cancellationToken: ct);
            return response.Value.GetString("Value");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<bool> AssistantStillExistsAsync(string assistantId, CancellationToken ct)
    {
        try
        {
            await _client.GetAssistantAsync(assistantId, ct);
            return true;
        }
        catch (System.ClientModel.ClientResultException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private async Task PersistAssistantIdAsync(string assistantId, CancellationToken ct)
    {
        var entity = new TableEntity("SYSTEM", "AssistantId") { { "Value", assistantId } };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }
}
