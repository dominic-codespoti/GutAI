# GutAI

> Meal logging · Calorie tracking · Gut health symptom correlation · Food safety insights

## Quick Start (Local Development)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)

### 1. Start Everything

```bash
make up
```

This starts **Azurite** (Azure Table Storage emulator), the **.NET API**, and the **Expo frontend**.

| Service     | URL                             |
| ----------- | ------------------------------- |
| API         | http://localhost:5000           |
| Scalar Docs | http://localhost:5000/scalar/v1 |
| Health      | http://localhost:5000/health    |
| Frontend    | http://localhost:8081           |
| Azurite     | http://localhost:10002 (Table)  |

### 2. Run API Locally (without Docker)

```bash
# Start only Azurite
docker compose up azurite -d

# Run the API with hot reload
cd backend/src/GutAI.Api
dotnet watch run
```

### 3. Start Frontend

```bash
cd frontend
npx expo start
```

Press `w` for web, `a` for Android emulator, or `i` for iOS simulator.

---

## Project Structure

```
gut-ai/
├── docker-compose.yml          # Azurite + API
├── Makefile                    # Dev shortcuts
├── backend/
│   ├── Dockerfile
│   ├── GutAI.sln
│   ├── src/
│   │   ├── GutAI.Api/          # Minimal API endpoints, middleware, Program.cs
│   │   ├── GutAI.Application/  # Interfaces, DTOs, pagination
│   │   ├── GutAI.Domain/       # Entities, enums, value objects
│   │   └── GutAI.Infrastructure/ # Table Storage, Lucene search, JWT, external APIs, domain services
│   └── tests/
│       ├── GutAI.Api.Tests/           # HTTP contract tests (WebApplicationFactory + Testcontainers)
│       ├── GutAI.Infrastructure.Tests/ # Unit tests for domain services
│       └── GutAI.IntegrationTests/    # CRUD + end-to-end tests against Azurite
├── frontend/
│   ├── app/                    # Expo Router file-based routing
│   │   ├── (auth)/             # Login & Register screens
│   │   ├── (tabs)/             # Dashboard, Symptoms, Meals, Scan, Insights, Profile
│   │   └── food/[id].tsx       # Food detail + safety report
│   └── src/
│       ├── api/                # Axios client with auth interceptors
│       ├── stores/             # Zustand state (auth, toast)
│       ├── types/              # TypeScript types matching backend DTOs
│       └── utils/              # Theme, colors, dates, storage, constants
├── infra/                      # Bicep templates for Azure Container Apps
├── scripts/                    # Contract checker, seed data
└── tools/                      # USDA food data generator
```

## Architecture

- **Storage:** Azure Table Storage (single `gutai` table, partition-key-based entity separation). No SQL, no EF Core.
- **Search:** Lucene.NET in-memory index over 7,261 embedded USDA foods.
- **Cache:** In-memory distributed cache (no external cache dependency).
- **Auth:** Custom JWT (HMAC-SHA256 access tokens + refresh token rotation).
- **External APIs:** OpenFoodFacts, Edamam, CalorieNinjas, USDA FoodData Central — all behind composite services with resilience policies.
- **Frontend:** Expo SDK 54 (React Native) with Expo Router, Zustand, TanStack Query, Axios.

## API Endpoints

### Auth (no auth required)

| Method | Path                        | Description          |
| ------ | --------------------------- | -------------------- |
| POST   | `/api/auth/register`        | Create account       |
| POST   | `/api/auth/login`           | Get tokens           |
| POST   | `/api/auth/refresh`         | Rotate refresh token |
| POST   | `/api/auth/logout`          | Revoke tokens        |
| POST   | `/api/auth/change-password` | Change password      |

### Meals

| Method | Path                              | Description             |
| ------ | --------------------------------- | ----------------------- |
| GET    | `/api/meals?date=`                | List meals by date      |
| GET    | `/api/meals/{id}`                 | Get a single meal       |
| POST   | `/api/meals`                      | Log a meal              |
| PUT    | `/api/meals/{id}`                 | Update meal             |
| DELETE | `/api/meals/{id}`                 | Soft-delete meal        |
| POST   | `/api/meals/log-natural`          | Parse NLP text → items  |
| GET    | `/api/meals/daily-summary/{date}` | Daily nutrition totals  |
| GET    | `/api/meals/export?from=&to=`     | Export meals + symptoms |

### Food & Safety

| Method | Path                                | Description                |
| ------ | ----------------------------------- | -------------------------- |
| GET    | `/api/food/search?q=`               | Search foods (local + API) |
| GET    | `/api/food/barcode/{barcode}`       | Barcode lookup             |
| GET    | `/api/food/{id}`                    | Get food product           |
| POST   | `/api/food`                         | Create food product        |
| PUT    | `/api/food/{id}`                    | Update food product        |
| DELETE | `/api/food/{id}`                    | Soft-delete food product   |
| GET    | `/api/food/{id}/safety-report`      | Full safety report         |
| GET    | `/api/food/{id}/gut-risk`           | Gut risk assessment        |
| GET    | `/api/food/{id}/fodmap`             | FODMAP assessment          |
| GET    | `/api/food/{id}/substitutions`      | Healthier substitutions    |
| GET    | `/api/food/{id}/glycemic`           | Glycemic index/load        |
| GET    | `/api/food/{id}/personalized-score` | Personalized health score  |
| GET    | `/api/food/additives`               | List all additives         |
| GET    | `/api/food/additives/{id}`          | Get single additive        |

### Symptoms

| Method | Path                              | Description        |
| ------ | --------------------------------- | ------------------ |
| GET    | `/api/symptoms?date=`             | List symptom logs  |
| GET    | `/api/symptoms/{id}`              | Get single symptom |
| POST   | `/api/symptoms`                   | Log a symptom      |
| PUT    | `/api/symptoms/{id}`              | Update symptom     |
| DELETE | `/api/symptoms/{id}`              | Soft-delete        |
| GET    | `/api/symptoms/types`             | Get symptom types  |
| GET    | `/api/symptoms/history?from=&to=` | Symptom history    |

### Insights

| Method | Path                                    | Description                  |
| ------ | --------------------------------------- | ---------------------------- |
| GET    | `/api/insights/correlations`            | Food-symptom correlations    |
| GET    | `/api/insights/nutrition-trends`        | Daily nutrition trends       |
| GET    | `/api/insights/additive-exposure`       | Additive exposure report     |
| GET    | `/api/insights/trigger-foods`           | Trigger food identification  |
| GET    | `/api/insights/food-diary-analysis`     | Comprehensive diary analysis |
| GET    | `/api/insights/elimination-diet/status` | Elimination diet progress    |

### User

| Method | Path                    | Description            |
| ------ | ----------------------- | ---------------------- |
| GET    | `/api/user/profile`     | Get profile            |
| PUT    | `/api/user/profile`     | Update profile         |
| PUT    | `/api/user/goals`       | Set nutrition goals    |
| GET    | `/api/user/alerts`      | Get additive watchlist |
| POST   | `/api/user/alerts`      | Add alert              |
| DELETE | `/api/user/alerts/{id}` | Remove alert           |
| DELETE | `/api/user/account`     | Delete account         |

## Makefile Commands

```bash
make up              # Start API + Azurite + frontend
make down            # Stop everything
make nuke            # Stop + delete all data
make fresh           # Nuke + no-cache rebuild
make logs            # Follow all logs
make api-logs        # Follow API logs
make frontend-logs   # Follow frontend logs
make frontend        # Start Expo dev server only
make test            # Run all tests + TS type check
make ci              # Full CI pipeline (build → unit → contract → DTO check → TS check)
make check-contracts # Frontend↔backend DTO field matching only
make build           # Build backend + frontend
make eas-dev         # EAS Build (development)
make eas-preview     # EAS Build (preview)
make eas-prod        # EAS Build (production)
make eas-submit      # Submit to app stores
make eas-update      # OTA update via EAS
```

## External APIs (Optional)

Set these via environment variables or `.env` for enhanced features:

| API                                                                   | Purpose                     | Required?                   |
| --------------------------------------------------------------------- | --------------------------- | --------------------------- |
| [CalorieNinjas](https://calorieninjas.com/api)                        | NLP meal parsing            | Optional                    |
| [USDA FoodData Central](https://fdc.nal.usda.gov/api-key-signup.html) | Nutrition data              | Optional (`demo_key` works) |
| [Edamam](https://developer.edamam.com/)                               | Food database + NLP parsing | Optional                    |
| [Open Food Facts](https://world.openfoodfacts.org/)                   | Barcode lookups             | No key needed               |

## Seed Data

On first startup in Development, the database is seeded with:

- **24 symptom types** across 5 categories (Digestive, Neurological, Skin, Energy, Other)
- **40+ food additives** with E-numbers, CSPI ratings, US/EU regulatory status, health concerns
- **7,261 USDA foods** (Foundation Foods + SR Legacy) compiled into a Lucene search index

## Deployment

Azure Container Apps via Bicep (`infra/main.bicep`):

- **Azure Storage Account** for Table Storage
- **Container App** from `ghcr.io` with 0.5 CPU, 1Gi memory, 0–3 replicas
- Health probes on `/health`
- Region: `australiaeast`
