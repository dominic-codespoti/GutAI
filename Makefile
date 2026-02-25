.PHONY: up down logs api-logs seed test ci check-contracts azure-setup azure-deploy

# ── Start everything (fresh build) ──
up:
	docker compose up -d --build
	cd frontend && npm install
	@setsid sh -c 'cd frontend && npx expo start --web --port 8081' > /tmp/gutai-frontend.log 2>&1 & echo $$! > /tmp/gutai-frontend.pid
	@echo ""
	@echo "🚀 GutAI local environment is running!"
	@echo "   API:       http://localhost:5000"
	@echo "   Scalar UI: http://localhost:5000/scalar/v1"
	@echo "   Frontend:  http://localhost:8081"
	@echo "   Azurite:   http://localhost:10002 (Table Storage)"
	@echo ""

# ── Stop everything (clean) ──
down:
	docker compose down --remove-orphans
	@if [ -f /tmp/gutai-frontend.pid ]; then PID=$$(cat /tmp/gutai-frontend.pid); kill -TERM -- -$$PID 2>/dev/null || kill -TERM $$PID 2>/dev/null; rm -f /tmp/gutai-frontend.pid /tmp/gutai-frontend.log; echo "⏹  Frontend stopped."; fi
	rm -rf frontend/node_modules/.cache

# ── Nuke everything (delete volumes) ──
nuke:
	docker compose down -v
	@if [ -f /tmp/gutai-frontend.pid ]; then PID=$$(cat /tmp/gutai-frontend.pid); kill -TERM -- -$$PID 2>/dev/null || kill -TERM $$PID 2>/dev/null; rm -f /tmp/gutai-frontend.pid /tmp/gutai-frontend.log; fi
	@echo "💥 All data wiped."

# ── View logs ──
logs:
	docker compose logs -f

api-logs:
	docker compose logs -f api

frontend-logs:
	@tail -f /tmp/gutai-frontend.log

# ── Shell access ──
fresh:
	docker compose down -v
	docker compose build --no-cache
	docker compose up -d
	@echo "🧹 Fresh rebuild complete."

# ── Tests ──
test:
	cd backend && dotnet test --verbosity minimal
	cd frontend && npx tsc --noEmit

# ── Full CI Pipeline ──
ci:
	@echo "🔨 Building backend..."
	cd backend && dotnet build --verbosity quiet
	@echo ""
	@echo "🧪 Running unit tests..."
	cd backend && dotnet test tests/GutAI.Infrastructure.Tests --verbosity minimal --no-build
	@echo ""
	@echo "🌐 Running API contract tests..."
	cd backend && dotnet test tests/GutAI.Api.Tests --verbosity minimal
	@echo ""
	@echo "📋 Checking frontend↔backend contracts..."
	node scripts/check-contracts.js
	@echo ""
	@echo "📝 TypeScript type check..."
	cd frontend && npx tsc --noEmit
	@echo ""
	@echo "✅ All CI checks passed!"

# ── Contract Check Only ──
check-contracts:
	node scripts/check-contracts.js

# ── Frontend ──
frontend:
	cd frontend && npx expo start

# ── Build all ──
build:
	cd backend && dotnet build
	cd frontend && npm run build 2>/dev/null || true

# ── EAS Build & Deploy ──
eas-setup:
	@echo "📱 Setting up EAS..."
	cd frontend && eas login
	cd frontend && eas init
	@echo "✅ EAS project initialized. Update app.json projectId + owner."

eas-dev:
	cd frontend && eas build --profile development --platform all

eas-preview:
	cd frontend && eas build --profile preview --platform all

eas-prod:
	cd frontend && eas build --profile production --platform all

eas-submit-ios:
	cd frontend && eas submit --platform ios --profile production

eas-submit-android:
	cd frontend && eas submit --platform android --profile production

eas-submit:
	cd frontend && eas submit --platform all --profile production

eas-update:
	cd frontend && eas update --channel production

# ── Azure Deployment ──
azure-setup:
	./scripts/azure-setup.sh

azure-deploy:
	./scripts/azure-setup.sh --deploy
