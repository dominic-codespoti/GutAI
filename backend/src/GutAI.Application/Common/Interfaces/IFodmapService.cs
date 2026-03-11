using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IFodmapService
{
    FodmapAssessmentDto Assess(FoodProductDto product);
    FodmapAssessmentDto AssessText(string foodDescription);
}
