# GutAI

> Meal logging · Calorie tracking · Gut health symptom correlation · Food safety insights

## Quick Start (Local Development)

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org/)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)

### 1. Start Infrastructure

```bash
make up
```

This starts **PostgreSQL**, **Redis**, **Seq** (log viewer), and the **.NET API** in Docker.

| Service    | URL                            |
|------------|--------------------------------|
| API        | http://localhost:5000           |
| Scalar Docs| http://localhost:5000/scalar/v1 |
| Health     | http://localhost:5000/health    |
| Seq Logs   | http://localhost:5341           |

### 2. Run API Locally (without Docker)

If you prefer running the API outside Docker for faster iteration:

```bash
# Start only infrastructure
docker compose up db redis seq -d

# Run the API with hot reload
cd backend/src/GutAI.Api
dotnet watch run
```

### 3. Start Frontend

```bash
cd frontend
npm start
```

Press `w` for web, `a` for Android emulator, or `i` for iOS simulator.

---

## Project Structure

```
gut-ai/
├── docker-compose.yml          # Postgres + Redis + Seq + API
├── Makefile                    # Dev shortcuts
├── .env                        # API keys (optional)
├── backend/
│   ├── Dockerfile
│   ├── GutAI.sln
│   └── src/
│       ├── GutAI.Api/          # Minimal API endpoints, auth, Program.cs
│       ├── GutAI.Application/  # Interfaces, DTOs, pagination
│       ├── GutAI.Domain/       # Entities, enums, value objects
│       └── GutAI.Infrastructure/ # EF Core, Redis, JWT, external API clients
└── frontend/
    ├── app/                    # Expo Router file-based routing
    │   ├── (auth)/             # Login & Register screens
    │   └── (tabs)/             # Dashboard, Meals, Scan, Symptoms, Profile
    └── src/
        ├── api/                # Axios client with auth interceptors
        ├── stores/             # Zustand state (auth, meals)
        └── types/              # TypeScript types matching backend DTOs
```

## API Endpoints

### Auth (no auth required)
| Method | Path                  | Description         |
|--------|-----------------------|---------------------|
| POST   | `/api/auth/register`  | Create account      |
| POST   | `/api/auth/login`     | Get tokens          |
| POST   | `/api/auth/refresh`   | Rotate refresh token|
| POST   | `/api/auth/logout`    | Revoke tokens       |

### Meals
| Method | Path                      | Description                |
|--------|---------------------------|----------------------------|
| GET    | `/api/meals?date=`        | List meals by date         |
| POST   | `/api/meals`              | Log a meal                 |
| PUT    | `/api/meals/{id}`         | Update meal                |
| DELETE | `/api/meals/{id}`         | Soft-delete meal           |
| POST   | `/api/meals/natural`      | Parse NLP text → items     |
| GET    | `/api/meals/daily-summary`| Daily nutrition totals     |

### Food & Safety
| Method | Path                           | Description               |
|--------|--------------------------------|---------------------------|
| GET    | `/api/food/search?q=`          | Search foods (local + API)|
| GET    | `/api/food/barcode/{code}`     | Barcode lookup             |
| GET    | `/api/food/{id}/safety-report` | Additive safety report     |
| GET    | `/api/food/additives`          | List all additives         |

### Symptoms
| Method | Path                    | Description          |
|--------|-------------------------|----------------------|
| GET    | `/api/symptoms`         | List symptom logs    |
| POST   | `/api/symptoms`         | Log a symptom        |
| DELETE | `/api/symptoms/{id}`    | Soft-delete          |
| GET    | `/api/symptoms/types`   | Get symptom types    |

### Insights
| Method | Path                             | Description              |
|--------|----------------------------------|--------------------------|
| GET    | `/api/insights/correlations`     | Food-symptom correlations|
| GET    | `/api/insights/nutrition-trends` | Daily nutrition trends   |
| GET    | `/api/insights/additive-exposure`| Additive exposure report |

### User
| Method | Path                         | Description            |
|--------|------------------------------|------------------------|
| GET    | `/api/user/profile`          | Get profile            |
| PUT    | `/api/user/profile`          | Update profile         |
| PUT    | `/api/user/goals`            | Set nutrition goals    |
| GET    | `/api/user/alerts`           | Get additive watchlist |
| POST   | `/api/user/alerts`           | Add alert              |
| DELETE | `/api/user/alerts/{id}`      | Remove alert           |

## Makefile Commands

```bash
make up          # Start all services
make down        # Stop all services
make nuke        # Stop + delete all data
make logs        # Follow all logs
make api-logs    # Follow API logs only
make db-shell    # Open psql shell
make redis-shell # Open redis-cli
make api         # Run API locally with hot reload
make frontend    # Start Expo dev server
make build       # Build everything
```

## Database Migrations

```bash
# Create a new migration
cd backend/src/GutAI.Api
dotnet ef migrations add MigrationName --project ../GutAI.Infrastructure

# Apply migrations (also runs automatically in Development)
dotnet ef database update
```

## External APIs (Optional)

Copy `.env.example` to `.env` and add keys for enhanced features:

| API | Purpose | Required? |
|-----|---------|-----------|
| [CalorieNinjas](https://calorieninjas.com/api) | NLP meal parsing | Optional |
| [USDA FoodData Central](https://fdc.nal.usda.gov/api-key-signup.html) | Nutrition data | Optional (demo_key works) |
| Open Food Facts | Barcode lookups | No key needed |

## Seed Data

On first startup, the database is seeded with:
- **24 symptom types** across 5 categories (Digestive, Neurological, Skin, Energy, Other)
- **25 food additives** with CSPI ratings, US/EU regulatory status, health concerns
