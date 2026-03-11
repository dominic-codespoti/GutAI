using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IGutRiskService
{
    GutRiskAssessmentDto Assess(FoodProductDto product);
    GutRiskAssessmentDto AssessText(string foodDescription);
}
