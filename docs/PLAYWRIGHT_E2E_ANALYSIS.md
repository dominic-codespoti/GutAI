# Playwright E2E Test Analysis Report — GutAI

**Date**: 2026-03-07
**Scope**: Feasibility analysis and setup plan for Playwright E2E tests against the Expo web export

---

## Executive Summary

GutAI is a React Native Expo (SDK 55) app with full web support via `react-native-web`. The frontend runs on `localhost:8081` via `npx expo start --web`, and the backend API (ASP.NET Core) runs in Docker on `localhost:5000`. Both services are launched with `make up`. Playwright is **not yet set up** anywhere in the project. The app uses JWT auth with `localStorage` on web, making it straightforward to seed auth state. There are **no `testID` attributes** in the app source code — selectors will need to rely on placeholder text, visible text, and ARIA roles.

---

## 1. How the App Is Served for Web

| Property          | Value                                                                                                                               |
| ----------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| **Command**       | `npx expo start --web --port 8081` (via `make up`)                                                                                  |
| **Dev URL**       | `http://localhost:8081`                                                                                                             |
| **Framework**     | Expo Router v55 + react-native-web 0.21                                                                                             |
| **Bundler**       | Metro (Expo's default web bundler)                                                                                                  |
| **Static export** | A `frontend/dist/` folder exists with a pre-built static export, but tests should run against the dev server for full interactivity |
| **Entry point**   | `expo-router/entry` → `frontend/app/_layout.tsx`                                                                                    |

### Important: Web-specific storage

On web, tokens are stored in **`localStorage`** (not SecureStore):

```typescript
// frontend/src/utils/storage.ts
if (isWeb) return localStorage.getItem(key); // getItem
if (isWeb) {
  localStorage.setItem(key, value);
} // setItem
```

This means Playwright can **inject auth tokens directly** via `page.evaluate()` to skip login in most tests.

---

## 2. Backend API Configuration

| Property                 | Value                                                                             |
| ------------------------ | --------------------------------------------------------------------------------- |
| **Base URL (local dev)** | `http://localhost:5000`                                                           |
| **Docker service**       | `api` container, maps `5000:8080`                                                 |
| **Storage**              | Azurite (local Azure Table Storage emulator) on ports 10000-10002                 |
| **Auth**                 | JWT Bearer — secret: `local-dev-secret-key-that-is-at-least-32-characters-long!!` |
| **JWT issuer/audience**  | `GutAI` / `GutAI`                                                                 |
| **Token expiry**         | 60 minutes                                                                        |
| **CORS**                 | `AllowAnyOrigin` in dev — no issues for cross-origin Playwright requests          |
| **Health check**         | `GET /health`                                                                     |
| **API docs**             | `http://localhost:5000/scalar/v1` (OpenAPI / Scalar UI)                           |

### API Endpoints (from `frontend/src/api/index.ts`)

| Domain       | Endpoints                                                                                                                                                                                                    |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Auth**     | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `POST /api/auth/change-password`                                                                       |
| **Meals**    | `GET/POST /api/meals`, `GET/PUT/DELETE /api/meals/:id`, `POST /api/meals/log-natural`, `GET /api/meals/daily-summary/:date`, `GET /api/meals/export`, `GET /api/meals/recent-foods`, `GET /api/meals/streak` |
| **Food**     | `GET /api/food/search?q=`, `GET /api/food/barcode/:code`, `GET /api/food/:id`, `GET /api/food/:id/safety-report`, `GET /api/food/:id/gut-risk`, `GET /api/food/:id/fodmap`, etc.                             |
| **Symptoms** | `GET/POST /api/symptoms`, `GET/PUT/DELETE /api/symptoms/:id`, `GET /api/symptoms/types`, `GET /api/symptoms/history`                                                                                         |
| **Insights** | `GET /api/insights/correlations`, `GET /api/insights/nutrition-trends`, `GET /api/insights/additive-exposure`, `GET /api/insights/trigger-foods`, `GET /api/insights/food-diary-analysis`                    |
| **User**     | `GET/PUT /api/user/profile`, `PUT /api/user/goals`, `GET/POST/DELETE /api/user/alerts`, `DELETE /api/user/account`                                                                                           |
| **Chat**     | `GET /api/chat/history`, `DELETE /api/chat/history` (+ SSE streaming endpoint)                                                                                                                               |

---

## 3. Auth Flow

### Registration Flow

1. User fills: Display Name, Email, Password, Confirm Password
2. Client validation: non-empty, valid email, password ≥ 8 chars, passwords match
3. `POST /api/auth/register` → returns `{ accessToken, refreshToken, user }`
4. Tokens stored: `localStorage.accessToken`, `localStorage.refreshToken`
5. Auth store sets `isAuthenticated = true`, `user = data.user`
6. **AuthGate** detects `isAuthenticated && !user.onboardingCompleted` → redirects to `/onboarding`

### Login Flow

1. User fills: Email, Password
2. `POST /api/auth/login` → returns `{ accessToken, refreshToken, user }`
3. Tokens stored in localStorage
4. AuthGate redirects to `/onboarding` if not completed, else `/(tabs)`

### Hydration (on app load)

1. Check `localStorage.accessToken` and `localStorage.refreshToken`
2. If expired, try `POST /api/auth/refresh`
3. Fetch `GET /api/user/profile` → set user state
4. AuthGate redirects based on `isAuthenticated` + `onboardingCompleted`

### Key implication for tests

- **New user**: register → onboarding (3 steps: allergies, diet/conditions, goals) → tabs
- **Existing user**: login → tabs (if onboarding completed)
- **Shortcut**: inject valid JWT + user profile into localStorage to skip auth entirely

---

## 4. Navigation Structure

### Root Layout (`app/_layout.tsx`)

```
Stack Navigator (headerShown: false)
├── (tabs)       → Tab Navigator (main app)
├── (auth)       → Auth screens
│   ├── login    → Login screen
│   └── register → Register screen
├── onboarding   → Multi-step onboarding wizard
├── food/[id]    → Food detail page
├── settings     → Settings screen
├── sources      → Sources & Disclaimer
└── privacy      → Privacy Policy
```

### Tab Navigator (`app/(tabs)/_layout.tsx`)

```
Custom Tab Bar (5 visible tabs + 2 hidden)
├── index     → "Home"      (dashboard with calorie ring, summaries)
├── symptoms  → "Symptoms"  (log/view symptom entries)
├── meals     → "Meals"     (center FAB, meal logging, date nav)
├── insights  → "Insights"  (correlations, trends, trigger foods)
├── chat      → "Coach"     (AI chat with SSE streaming)
├── profile   → (hidden, accessed via settings icon in header)
└── scan      → (hidden, "Food Lookup" with barcode + search)
```

### URL paths on web

| Route        | Web URL                                                    |
| ------------ | ---------------------------------------------------------- |
| Login        | `http://localhost:8081/(auth)/login` or redirected to root |
| Register     | `http://localhost:8081/(auth)/register`                    |
| Onboarding   | `http://localhost:8081/onboarding`                         |
| Home tab     | `http://localhost:8081/` or `http://localhost:8081/(tabs)` |
| Meals tab    | `http://localhost:8081/(tabs)/meals`                       |
| Symptoms tab | `http://localhost:8081/(tabs)/symptoms`                    |
| Insights tab | `http://localhost:8081/(tabs)/insights`                    |
| Coach tab    | `http://localhost:8081/(tabs)/chat`                        |
| Profile      | `http://localhost:8081/(tabs)/profile`                     |
| Food Lookup  | `http://localhost:8081/(tabs)/scan`                        |
| Food Detail  | `http://localhost:8081/food/[id]`                          |
| Settings     | `http://localhost:8081/settings`                           |

---

## 5. testID / Accessibility Attributes

**Finding: The app source code has ZERO `testID` props.**

All `testID` references found in the search are from the compiled `dist/` bundle (react-native-web runtime) and react-native-web's internal prop handling, not from application code.

### Selector Strategy

Since there are no testIDs, Playwright tests should use this priority:

1. **Placeholder text** — `page.getByPlaceholder('Email')`, `page.getByPlaceholder('Password')`, `page.getByPlaceholder('Display Name')`
2. **Visible text** — `page.getByText('Log In')`, `page.getByText('Create Account')`, `page.getByText('Sign Up')`
3. **Role-based** — `page.getByRole('textbox')`, `page.getByRole('button', { name: 'Log In' })`
4. **CSS selectors** — As a last resort for custom tab bar icons, modals, etc.

### Key UI text anchors by screen

| Screen      | Identifiable Text / Placeholders                                                                                                   |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Login       | Placeholders: "Email", "Password". Buttons: "Log In". Links: "Sign Up", "Sources & Disclaimer", "Privacy Policy". Title: "GutLens" |
| Register    | Placeholders: "Display Name", "Email", "Password (min 8 characters)", "Confirm password". Button: "Create Account". Link: "Log In" |
| Onboarding  | Steps with "Next" / "Finish" buttons, allergy chips, goal input fields                                                             |
| Home        | Calorie ring, greeting text, daily summary                                                                                         |
| Meals       | Date navigation, meal type chips ("Breakfast", "Lunch", etc.), FAB button, meal cards                                              |
| Symptoms    | Symptom type list, severity dots (1-10), "Log Symptom" button                                                                      |
| Insights    | Period selector, correlation cards, trend data                                                                                     |
| Coach       | Chat input, message bubbles, streaming indicator                                                                                   |
| Profile     | Display name, allergies, dietary preferences, goals                                                                                |
| Food Lookup | Placeholders: search text input. Barcode scanner. Food search results                                                              |

### Recommendation: Add testIDs incrementally

For long-term maintainability, `testID` attributes should be added to key interactive elements. On web, `react-native-web` maps `testID` → `data-testid`, which Playwright can select with `page.getByTestId()`.

---

## 6. Key Testable User Flows

### Flow 1: Registration → Onboarding → Dashboard

1. Navigate to register page
2. Fill display name, email, password, confirm password
3. Click "Create Account"
4. Complete onboarding step 1 (allergies — select chips)
5. Complete onboarding step 2 (diet + conditions)
6. Complete onboarding step 3 (nutrition goals)
7. Click "Finish"
8. Verify redirected to Home tab with dashboard

### Flow 2: Login → Dashboard

1. Navigate to login page
2. Fill email, password
3. Click "Log In"
4. Verify Home tab loads with greeting and calorie ring

### Flow 3: Meal Logging

1. From Meals tab, tap the FAB (+) button
2. Choose "Quick Add" or "Search"
3. Search for a food (e.g., "chicken")
4. Select food, choose serving, meal type
5. Confirm add
6. Verify meal appears in the list

### Flow 4: Symptom Logging

1. Navigate to Symptoms tab
2. Tap "Log Symptom" or equivalent
3. Select symptom type
4. Set severity (1-10)
5. Add optional notes/duration
6. Save
7. Verify symptom appears in list

### Flow 5: Food Search & Detail

1. Navigate to Scan/Food Lookup tab
2. Type food name in search
3. Verify search results appear
4. Click a result
5. Verify food detail page shows nutrition info

### Flow 6: Insights Viewing

1. Navigate to Insights tab
2. Verify correlations, nutrition trends load
3. Change period selector
4. Verify data refreshes

### Flow 7: Profile Management

1. Tap settings icon in header → navigate to Profile
2. Edit display name
3. Update allergies
4. Change nutrition goals
5. Save and verify updates persisted

### Flow 8: AI Coach Chat

1. Navigate to Coach tab
2. Type a message
3. Verify streaming response appears
4. (Note: requires Azure OpenAI — may need mock or skip in CI)

---

## 7. Risks, Edge Cases, and Mitigations

| Risk                                       | Impact                                          | Mitigation                                                                                                          |
| ------------------------------------------ | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| **No testIDs**                             | Fragile selectors based on text                 | Add `testID` props to key elements; use `getByPlaceholder` / `getByText` for now                                    |
| **Expo dev server startup time**           | Slow test startup; Metro bundling on first load | Use `webServer` config in Playwright with generous timeout (120s); alternatively test against the pre-built `dist/` |
| **react-native-web rendering differences** | Some RN components render differently on web    | Test what's actually rendered; avoid testing native-only features (haptics, camera, secure store)                   |
| **Camera/barcode scanner**                 | Not available in headless browser               | Skip barcode scan tests; test search-based food lookup only                                                         |
| **RevenueCat / Subscriptions**             | `react-native-purchases` will error on web      | Mock or stub; the `isPro` check is already gated                                                                    |
| **Azure OpenAI for Chat**                  | Chat requires live API key                      | Mock the SSE endpoint, or test just the UI interaction                                                              |
| **Azurite data persistence**               | Tests may leave stale data                      | Use unique email per test run; add cleanup or use `make nuke` between runs                                          |
| **expo-haptics**                           | No-op on web but may warn                       | Acceptable; no test impact                                                                                          |
| **Reanimated animations**                  | May interfere with selectors during transitions | Use `page.waitForLoadState()` and explicit waits for elements                                                       |

---

## 8. Recommended Playwright Project Setup

### 8.1 Installation

```bash
cd /home/dom/projects/gut-ai
npm init -y  # root package.json already exists
npm install -D @playwright/test
npx playwright install --with-deps chromium
```

### 8.2 Directory Structure

```
e2e/
├── playwright.config.ts
├── fixtures/
│   └── auth.ts            # Auth fixture (login helper, token injection)
├── helpers/
│   ├── api.ts             # Direct API helpers (register, create meal, etc.)
│   └── constants.ts       # URLs, test credentials
├── tests/
│   ├── auth.spec.ts       # Login, register, logout
│   ├── onboarding.spec.ts # Full onboarding flow
│   ├── meals.spec.ts      # Meal CRUD
│   ├── symptoms.spec.ts   # Symptom logging
│   ├── food-search.spec.ts# Food search + detail
│   ├── insights.spec.ts   # Insights page loads
│   ├── profile.spec.ts    # Profile editing
│   └── navigation.spec.ts # Tab navigation, routing
└── global-setup.ts        # Create test user via API
```

### 8.3 Playwright Config

```typescript
// e2e/playwright.config.ts
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false, // sequential — shared test user
  retries: process.env.CI ? 2 : 0,
  workers: 1, // single worker to avoid state conflicts
  reporter: [["html", { open: "never" }]],

  use: {
    baseURL: "http://localhost:8081",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "on-first-retry",
  },

  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
    // Optional: mobile viewport
    {
      name: "mobile-chrome",
      use: { ...devices["Pixel 5"] },
    },
  ],

  webServer: [
    {
      // Backend API + Azurite
      command: "docker compose up -d --build && sleep 5",
      url: "http://localhost:5000/health",
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
    },
    {
      // Frontend Expo web
      command: "cd frontend && npx expo start --web --port 8081",
      url: "http://localhost:8081",
      timeout: 120_000,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
```

### 8.4 Auth Fixture (Token Injection)

```typescript
// e2e/fixtures/auth.ts
import { test as base, type Page } from "@playwright/test";
import { request } from "@playwright/test";

const API_URL = "http://localhost:5000";

interface TestUser {
  email: string;
  password: string;
  displayName: string;
  accessToken: string;
  refreshToken: string;
}

async function createTestUser(): Promise<TestUser> {
  const ctx = await request.newContext({ baseURL: API_URL });
  const email = `e2e-${Date.now()}@test.com`;
  const password = "TestPassword123!";
  const displayName = "E2E Test User";

  const res = await ctx.post("/api/auth/register", {
    data: { email, password, displayName },
  });
  const data = await res.json();

  // Complete onboarding
  await ctx.put("/api/user/profile", {
    data: { onboardingCompleted: true },
    headers: { Authorization: `Bearer ${data.accessToken}` },
  });
  await ctx.put("/api/user/goals", {
    data: {
      dailyCalorieGoal: 2000,
      dailyProteinGoalG: 50,
      dailyCarbGoalG: 250,
      dailyFatGoalG: 65,
      dailyFiberGoalG: 25,
    },
    headers: { Authorization: `Bearer ${data.accessToken}` },
  });

  return {
    email,
    password,
    displayName,
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
  };
}

/** Inject tokens into localStorage so the app treats us as logged in */
async function injectAuth(page: Page, user: TestUser) {
  await page.goto("/");
  await page.evaluate(({ accessToken, refreshToken }) => {
    localStorage.setItem("accessToken", accessToken);
    localStorage.setItem("refreshToken", refreshToken);
  }, user);
  await page.reload();
}

export const test = base.extend<{ testUser: TestUser; authedPage: Page }>({
  testUser: async ({}, use) => {
    const user = await createTestUser();
    await use(user);
  },
  authedPage: async ({ page, testUser }, use) => {
    await injectAuth(page, testUser);
    await use(page);
  },
});

export { expect } from "@playwright/test";
```

### 8.5 Sample Test: Auth Flow

```typescript
// e2e/tests/auth.spec.ts
import { test, expect } from "@playwright/test";

test.describe("Authentication", () => {
  test("register a new account → onboarding", async ({ page }) => {
    await page.goto("/");
    // Should redirect to login
    await expect(page.getByText("GutLens")).toBeVisible();

    // Navigate to register
    await page.getByText("Sign Up").click();

    // Fill registration form
    await page.getByPlaceholder("Display Name").fill("Playwright User");
    await page.getByPlaceholder("Email").fill(`pw-${Date.now()}@test.com`);
    await page
      .getByPlaceholder("Password (min 8 characters)")
      .fill("TestPass123!");
    await page.getByPlaceholder("Confirm password").fill("TestPass123!");

    // Submit
    await page.getByText("Create Account").click();

    // Should arrive at onboarding
    await expect(page).toHaveURL(/onboarding/);
  });

  test("login with valid credentials", async ({ page }) => {
    // Pre-create user via API
    const apiCtx = await page.request.newContext();
    // ... (use helper to create user + complete onboarding)

    await page.goto("/");
    await page.getByPlaceholder("Email").fill("test@test.com");
    await page.getByPlaceholder("Password").fill("TestPass123!");
    await page.getByText("Log In").click();

    // Should arrive at home tab
    await expect(page.getByText("Home")).toBeVisible();
  });

  test("shows error on invalid credentials", async ({ page }) => {
    await page.goto("/");
    await page.getByPlaceholder("Email").fill("nobody@test.com");
    await page.getByPlaceholder("Password").fill("wrongpassword");
    await page.getByText("Log In").click();

    // Toast error should appear
    await expect(page.getByText("Invalid credentials")).toBeVisible();
  });
});
```

### 8.6 Makefile Integration

```makefile
# Add to existing Makefile

# ── E2E Tests ──
e2e:
	cd e2e && npx playwright test

e2e\:ui:
	cd e2e && npx playwright test --ui

e2e\:headed:
	cd e2e && npx playwright test --headed

e2e\:report:
	cd e2e && npx playwright show-report
```

---

## 9. Follow-up Actions / Implementation Plan

| Priority | Task                                             | Effort |
| -------- | ------------------------------------------------ | ------ |
| **P0**   | Install Playwright, create `e2e/` dir + config   | 30 min |
| **P0**   | Create auth fixture with token injection         | 30 min |
| **P0**   | Write auth tests (login, register, error states) | 1 hr   |
| **P1**   | Write onboarding flow test                       | 1 hr   |
| **P1**   | Write meal logging test                          | 1-2 hr |
| **P1**   | Write symptom logging test                       | 1 hr   |
| **P1**   | Write food search + detail test                  | 1 hr   |
| **P2**   | Write insights page load test                    | 30 min |
| **P2**   | Write profile editing test                       | 1 hr   |
| **P2**   | Write tab navigation smoke test                  | 30 min |
| **P3**   | Add `testID` props to key interactive elements   | 2-3 hr |
| **P3**   | Add CI pipeline integration                      | 1 hr   |
| **P3**   | Chat/Coach test (requires OpenAI mock)           | 2 hr   |

### Static export alternative

The `frontend/dist/` folder contains a pre-built static export. For CI speed, you could serve it with a simple HTTP server (`npx serve frontend/dist -p 8081`) instead of running Metro. However, the dev server is more representative of actual behavior and supports hot routing correctly.

---

## 10. Summary of Key Facts

| Item                      | Value                                                                         |
| ------------------------- | ----------------------------------------------------------------------------- |
| Frontend URL              | `http://localhost:8081`                                                       |
| Backend API URL           | `http://localhost:5000`                                                       |
| Start everything          | `make up`                                                                     |
| Stop everything           | `make down`                                                                   |
| Web storage               | `localStorage` (accessToken, refreshToken)                                    |
| Auth type                 | JWT Bearer                                                                    |
| testIDs in source         | **None** — use text/placeholder selectors                                     |
| Tab count                 | 5 visible (Home, Symptoms, Meals, Insights, Coach) + 2 hidden (Profile, Scan) |
| Existing E2E setup        | **None** — Playwright not yet installed                                       |
| Blocking features for web | Camera/barcode (mock), Haptics (no-op), RevenueCat (stub)                     |
