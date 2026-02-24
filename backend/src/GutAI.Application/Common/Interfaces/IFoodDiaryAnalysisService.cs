namespace GutAI.Application.Common.Interfaces;

using GutAI.Application.Common.DTOs;

public interface IFoodDiaryAnalysisService
{
    Task<FoodDiaryAnalysisDto> AnalyzeAsync(Guid userId, DateOnly from, DateOnly to, ITableStore store);
    Task<EliminationDietStatusDto> GetEliminationStatusAsync(Guid userId, ITableStore store);
}
