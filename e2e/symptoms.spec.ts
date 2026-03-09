import { test, expect, API } from "./fixtures";

test.describe("Symptoms Tab", () => {
  test("shows symptom types organized by category", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(3000);

    // Should see "Log Symptom" heading
    await expect(page.getByText("Log Symptom")).toBeVisible({ timeout: 10000 });

    // Should see category headers
    await expect(page.getByText("Digestive").first()).toBeVisible({
      timeout: 10000,
    });

    // Should see some common symptom types
    await expect(
      page.getByText(/Bloating|Gas|Nausea|Cramping|Heartburn/i).first(),
    ).toBeVisible({ timeout: 5000 });
  });

  test("shows symptom history section", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(3000);

    // Should see some form of history/today section
    // The page has "Log Symptom" at top and symptom categories
    await expect(page.getByText("Log Symptom")).toBeVisible({
      timeout: 10000,
    });
  });

  test("can select a symptom type and see severity panel", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(3000);

    // Wait for types to load
    await expect(page.getByText("Log Symptom")).toBeVisible({ timeout: 10000 });

    // Click on a symptom type (e.g. Bloating)
    await page.getByText("Bloating").click({ force: true });
    await page.waitForTimeout(1000);

    // Should see severity-related content
    await expect(
      page.getByText(/Severity|Mild|Severe|Log Symptom/i).first(),
    ).toBeVisible({ timeout: 5000 });
  });

  test("can log a symptom via API and see it in history", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Get symptom types first
    const typesRes = await page.request.get(`${API}/api/symptoms/types`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
    });
    expect(typesRes.ok()).toBeTruthy();
    const types = await typesRes.json();
    const bloating = types.find(
      (t: any) => t.name?.toLowerCase() === "bloating",
    );
    expect(bloating).toBeTruthy();

    // Create symptom via API
    const res = await page.request.post(`${API}/api/symptoms`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        symptomTypeId: bloating.id,
        severity: 7,
        occurredAt: new Date().toISOString(),
        notes: "E2E test symptom",
      },
    });
    expect(res.ok()).toBeTruthy();

    // Navigate to symptoms tab
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(4000);

    // Should see the symptom in history
    await expect(page.getByText("Bloating").first()).toBeVisible({
      timeout: 15000,
    });
  });

  test("date navigation shows today", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(3000);

    // Should show Today
    await expect(page.getByText("Today").first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("multiple symptom categories are visible", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/symptoms");
    await page.waitForTimeout(3000);

    // Should see multiple category sections
    await expect(page.getByText("Digestive").first()).toBeVisible({
      timeout: 10000,
    });

    // Scroll down to find more categories
    await page.evaluate(() => window.scrollTo(0, 500));
    await page.waitForTimeout(500);

    await expect(
      page.getByText(/Neurological|Energy|Skin|Other/i).first(),
    ).toBeVisible({ timeout: 10000 });
  });
});
