using 'main.bicep'

param location = 'australiaeast'
param containerImage = 'ghcr.io/dominic-codespoti/gutai/gutai-api:latest'
param azureOpenAIEndpoint = 'https://ai-misc-proj-resource.openai.azure.com/'
param azureOpenAIDeploymentName = 'gpt-4o-mini'
