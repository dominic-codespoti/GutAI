using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface ICorrelationEngine
{
    Task<List<CorrelationDto>> ComputeCorrelationsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
