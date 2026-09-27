using 'main.bicep'

param location = 'australiaeast'
param containerImage = 'ghcr.io/dominic-codespoti/gutai/gutai-api:latest'
param azureOpenAIEndpoint = 'https://ai-misc-proj-resource.openai.azure.com/'
param azureContentUnderstandingEndpoint = 'https://ai-misc-proj-resource.services.ai.azure.com/'
param azureOpenAIDeploymentName = 'gpt-5.4-mini'
param azureOpenAIInputPer1M = '0.20'
param azureOpenAIOutputPer1M = '1.20'
param alertEmailAddress = ''
param scanCostP95ThresholdUsd = '0.05'
param scanLatencyP95BudgetMs = 45000
