import { defineConfig, devices } from "@playwright/test";

/**
 * GutAI / GutLens — Playwright E2E Configuration
 *
 * Expects:
 *   - Backend API at http://localhost:5000  (docker compose up -d)
 *   - Frontend at  http://localhost:8081   (npx expo start --web --port 8081)
 *
 * Start both with `make up` before running tests, or let the webServer
 * entries below handle it automatically.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false, // sequential — tests share Azurite state
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI ? "github" : "html",
  timeout: 60_000,

  use: {
    baseURL: "http://localhost:8081",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    actionTimeout: 10_000,
  },

  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 430, height: 932 }, // iPhone 15 Pro Max-ish
      },
    },
  ],

  /* Uncomment to auto-start services when running `npx playwright test` */
  // webServer: [
  //   {
  //     command: "docker compose up -d --build && docker compose wait api",
  //     url: "http://localhost:5000/health",
  //     reuseExistingServer: true,
  //     timeout: 120_000,
  //   },
  //   {
  //     command: "cd frontend && npx expo start --web --port 8081",
  //     url: "http://localhost:8081",
  //     reuseExistingServer: true,
  //     timeout: 120_000,
  //   },
  // ],
});
