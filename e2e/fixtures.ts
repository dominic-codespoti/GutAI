import { test as base, expect, Page } from "@playwright/test";

/* ------------------------------------------------------------------ */
/*  Constants                                                          */
/* ------------------------------------------------------------------ */
const API = "http://localhost:5000";

/** Generate a unique user for each test run */
function testUser() {
  const id = Math.random().toString(36).slice(2, 8);
  return {
    email: `e2e-${id}@test.com`,
    password: "Test1234!",
    displayName: `E2E User ${id}`,
  };
}

/* ------------------------------------------------------------------ */
/*  Helpers                                                            */
/* ------------------------------------------------------------------ */

/**
 * Register a user via the API, complete onboarding, and inject
 * JWT tokens into localStorage so the app boots straight to (tabs).
 */
async function createAuthenticatedUser(page: Page) {
  const user = testUser();

  // 1. Register via API
  const res = await page.request.post(`${API}/api/auth/register`, {
    data: {
      email: user.email,
      password: user.password,
      displayName: user.displayName,
    },
  });

  expect(res.ok(), `Register failed: ${res.status()}`).toBeTruthy();
  const body = await res.json();
  const { accessToken, refreshToken } = body;
  expect(accessToken).toBeTruthy();

  // 2. Complete onboarding via API
  await page.request.put(`${API}/api/user/profile`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: {
      onboardingCompleted: true,
      allergies: ["Gluten"],
      dietaryPreferences: ["None"],
      gutConditions: [],
    },
  });

  await page.request.put(`${API}/api/user/goals`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: {
      dailyCalorieGoal: 2000,
      dailyProteinGoalG: 50,
      dailyCarbGoalG: 250,
      dailyFatGoalG: 65,
      dailyFiberGoalG: 25,
    },
  });

  // 3. Inject tokens into localStorage so the app hydrates as logged-in
  await page.addInitScript(
    ({ accessToken, refreshToken }) => {
      localStorage.setItem("accessToken", accessToken);
      localStorage.setItem("refreshToken", refreshToken);
    },
    { accessToken, refreshToken },
  );

  return { ...user, accessToken, refreshToken };
}

/** Wait for the app to finish loading (auth hydration) */
async function waitForAppReady(page: Page) {
  // The app shows "GutLens" on login OR the tab bar once authenticated
  // Wait for either the Home tab content or login screen
  await page.waitForLoadState("networkidle");
  // Give the React app time to hydrate
  await page.waitForTimeout(2000);
}

/** Navigate to a tab by clicking its label in the tab bar */
async function navigateToTab(page: Page, tabName: string) {
  await page.getByText(tabName, { exact: true }).click();
  await page.waitForTimeout(500);
}

/* ------------------------------------------------------------------ */
/*  Custom test fixture                                                */
/* ------------------------------------------------------------------ */

type Fixtures = {
  /** Authenticated page — tokens pre-injected, lands on home tab */
  authedPage: Page;
  /** The user credentials that were registered */
  testUserData: {
    email: string;
    password: string;
    displayName: string;
    accessToken: string;
    refreshToken: string;
  };
};

export const test = base.extend<Fixtures>({
  authedPage: async ({ page }, use) => {
    const user = await createAuthenticatedUser(page);
    await page.goto("/");
    await waitForAppReady(page);
    // Store user data on the page for other fixtures
    (page as any).__testUser = user;
    await use(page);
  },

  testUserData: async ({ authedPage }, use) => {
    await use((authedPage as any).__testUser);
  },
});

export { expect, createAuthenticatedUser, waitForAppReady, navigateToTab, API };
