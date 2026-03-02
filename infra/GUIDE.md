# Infrastructure & Deployment Guide

## Troubleshooting ImagePullBackOff

If the Container App fails to pull the image from GHCR, check the following:

1.  **Personal Access Token (PAT):** Ensure a GitHub PAT with `read:packages` scope is set as the `GH_PAT` secret in the repository. The default `GITHUB_TOKEN` expires after 10 minutes and will cause pulls to fail during scaling or restarts.
2.  **Username Casing:** GHCR and Azure Container Apps are case-sensitive. The deployment workflow automatically lowercases the owner name to ensure compatibility.
3.  **Registry Configuration:** The Bicep template configures `ghcr.io` as a private registry. If you switch to a public repository, you can remove the `registries` section in `main.bicep`.

## Scaling

The app is currently configured with:
- **minReplicas: 0** (Scales to zero when idle to save cost)
- **maxReplicas: 3**
- **Trigger:** 25 concurrent HTTP requests per replica

## Manual Deployment

You can manually trigger a deployment via the GitHub CLI:
```bash
gh workflow run deploy.yml
```
