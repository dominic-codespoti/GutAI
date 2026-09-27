FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the API's project graph for a cached restore. Restore only the API project, so test and
# tool projects added to GutAI.sln never break the image build; add a COPY here only when the
# API gains a new ProjectReference.
COPY src/GutAI.Api/GutAI.Api.csproj src/GutAI.Api/
COPY src/GutAI.Application/GutAI.Application.csproj src/GutAI.Application/
COPY src/GutAI.Domain/GutAI.Domain.csproj src/GutAI.Domain/
COPY src/GutAI.Infrastructure/GutAI.Infrastructure.csproj src/GutAI.Infrastructure/

RUN dotnet restore src/GutAI.Api/GutAI.Api.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish src/GutAI.Api/GutAI.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    curl -sL https://aka.ms/InstallAzureCLIDeb | bash && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "GutAI.Api.dll"]
