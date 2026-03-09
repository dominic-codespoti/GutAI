import { test, expect } from "@playwright/test";
import { createAuthenticatedUser, waitForAppReady, API } from "./fixtures";

test.describe("Authentication Flow", () => {
  test("shows login screen by default", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    // Should see the GutLens branding
    await expect(page.getByText("GutLens")).toBeVisible();
    await expect(page.getByText("Track your meals & gut health")).toBeVisible();

    // Should see login form
    await expect(page.getByPlaceholder("Email")).toBeVisible();
    await expect(page.getByPlaceholder("Password")).toBeVisible();
    await expect(page.getByText("Log In", { exact: true })).toBeVisible();
  });

  test("shows validation errors on empty login", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    await page.getByText("Log In", { exact: true }).click();

    // Should show error toast
    await expect(page.getByText("Please fill in all fields")).toBeVisible({
      timeout: 5000,
    });
  });

  test("shows validation error for invalid email", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    await page.getByPlaceholder("Email").fill("not-an-email");
    await page.getByPlaceholder("Password").fill("Test1234!");
    await page.getByText("Log In", { exact: true }).click();

    await expect(
      page.getByText("Please enter a valid email address"),
    ).toBeVisible({ timeout: 5000 });
  });

  test("shows error for wrong credentials", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    await page.getByPlaceholder("Email").fill("nonexistent@test.com");
    await page.getByPlaceholder("Password").fill("WrongPassword1!");
    await page.getByText("Log In", { exact: true }).click();

    // Should show error toast — either "Invalid credentials" or backend message
    await expect(
      page.getByText(/Invalid credentials|Invalid email or password/i),
    ).toBeVisible({ timeout: 10000 });
  });

  test("can navigate to register screen", async ({ page }) => {
    // Navigate directly to register to avoid dual-screen DOM issues
    await page.goto("/(auth)/register");
    await waitForAppReady(page);

    // Should see the Create Account heading
    await expect(page.getByText("Create Account").first()).toBeVisible({
      timeout: 10000,
    });
    await expect(page.getByPlaceholder("Display Name")).toBeVisible();
  });

  test("register form validates required fields", async ({ page }) => {
    await page.goto("/(auth)/register");
    await waitForAppReady(page);

    // Click Create Account button (the submit button, not the heading)
    // The button text is "Create Account" — use last() since heading comes first
    await page.getByText("Create Account").last().click();
    await expect(page.getByText("Please fill in all fields")).toBeVisible({
      timeout: 5000,
    });
  });

  test("register form validates password length", async ({ page }) => {
    await page.goto("/(auth)/register");
    await waitForAppReady(page);

    await page.getByPlaceholder("Display Name").fill("Test User");
    await page.getByPlaceholder("Email").fill("test@example.com");
    await page.getByPlaceholder("Password (min 8 characters)").fill("short");
    await page.getByPlaceholder("Confirm password").fill("short");
    await page.getByText("Create Account").last().click();

    await expect(
      page.getByText("Password must be at least 8 characters"),
    ).toBeVisible({ timeout: 5000 });
  });

  test("register form validates password match", async ({ page }) => {
    await page.goto("/(auth)/register");
    await waitForAppReady(page);

    await page.getByPlaceholder("Display Name").fill("Test User");
    await page.getByPlaceholder("Email").fill("test@example.com");
    await page
      .getByPlaceholder("Password (min 8 characters)")
      .fill("Test1234!");
    await page.getByPlaceholder("Confirm password").fill("Different1!");
    await page.getByText("Create Account").last().click();

    await expect(page.getByText("Passwords do not match")).toBeVisible({
      timeout: 5000,
    });
  });

  test("full registration → onboarding → dashboard flow", async ({ page }) => {
    const id = Math.random().toString(36).slice(2, 8);
    const email = `e2e-reg-${id}@test.com`;

    await page.goto("/(auth)/register");
    await waitForAppReady(page);

    // Fill registration form
    await page.getByPlaceholder("Display Name").fill(`E2E Reg ${id}`);
    await page.getByPlaceholder("Email").fill(email);
    await page
      .getByPlaceholder("Password (min 8 characters)")
      .fill("Test1234!");
    await page.getByPlaceholder("Confirm password").fill("Test1234!");

    // Submit
    await page.getByText("Create Account").last().click();

    // Should navigate to onboarding (5 steps: Welcome, Allergies, Diet, Conditions, Goals)
    await expect(page.getByText("Welcome to GutLens")).toBeVisible({
      timeout: 15000,
    });

    // Step 0: Welcome → Next
    await page.getByText("Next").click();
    await page.waitForTimeout(1000);

    // Step 1: Allergies → Next
    await page.getByText("Next").click();
    await page.waitForTimeout(1000);

    // Step 2: Diet → Next
    await page.getByText("Next").click();
    await page.waitForTimeout(1000);

    // Step 3: Conditions → Next
    await page.getByText("Next").click();
    await page.waitForTimeout(1000);

    // Step 4: Goals → Get Started
    await page.getByText("Get Started").click();
    await page.waitForTimeout(5000);

    // Should land on the dashboard / home tab
    await expect(page.getByText("Today's Meals")).toBeVisible({
      timeout: 15000,
    });
  });

  test("login with valid credentials → dashboard", async ({ page }) => {
    // First register a user via API
    const id = Math.random().toString(36).slice(2, 8);
    const email = `e2e-login-${id}@test.com`;
    const password = "Test1234!";

    const regRes = await page.request.post(`${API}/api/auth/register`, {
      data: { email, password, displayName: `Login Test ${id}` },
    });
    expect(regRes.ok()).toBeTruthy();
    const { accessToken } = await regRes.json();

    // Complete onboarding via API
    await page.request.put(`${API}/api/user/profile`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      data: { onboardingCompleted: true },
    });

    // Now login via UI
    await page.goto("/");
    await waitForAppReady(page);

    await page.getByPlaceholder("Email").fill(email);
    await page.getByPlaceholder("Password").fill(password);
    await page.getByText("Log In", { exact: true }).click();

    // Should navigate to dashboard
    await expect(page.getByText("Today's Meals")).toBeVisible({
      timeout: 15000,
    });
  });

  test("authenticated user is redirected to dashboard on revisit", async ({
    page,
  }) => {
    await createAuthenticatedUser(page);
    await page.goto("/");
    await waitForAppReady(page);

    // Should see dashboard content (not login)
    await expect(page.getByText("Today's Meals")).toBeVisible({
      timeout: 15000,
    });
  });

  test("can navigate to sources & privacy from login", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    // Check Sources link
    await page.getByText("Sources & Disclaimer").click();
    await page.waitForTimeout(2000);
    await expect(page.getByRole("heading", { name: /Sources/i })).toBeVisible({
      timeout: 5000,
    });

    await page.goBack();
    await page.waitForTimeout(1000);

    // Check Privacy link
    await page.getByText("Privacy Policy").click();
    await page.waitForTimeout(2000);
    await expect(page.getByRole("heading", { name: /Privacy/i })).toBeVisible({
      timeout: 5000,
    });
  });
});
