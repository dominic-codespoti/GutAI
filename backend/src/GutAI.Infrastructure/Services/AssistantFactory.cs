#pragma warning disable OPENAI001

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
    private string? _assistantId;
    private readonly ILogger<AssistantFactory> _logger;

    public AssistantFactory(
        AssistantClient client,
        string model,
        string instructions,
        IReadOnlyList<FunctionToolDefinition> tools,
        ILogger<AssistantFactory> logger)
    {
        _client = client;
        _model = model;
        _instructions = instructions;
        _tools = tools;
        _logger = logger;
    }

    public async Task<string> GetAssistantIdAsync(CancellationToken ct)
    {
        if (_assistantId is not null) return _assistantId;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_assistantId is not null) return _assistantId;

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
}
