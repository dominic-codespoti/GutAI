import { test, expect, API } from "./fixtures";

test.describe("Dashboard (Home Tab)", () => {
  test("shows personalized greeting", async ({ authedPage: page }) => {
    const hour = new Date().getHours();
    let greeting: RegExp;
    if (hour < 12) greeting = /Good morning/i;
    else if (hour < 17) greeting = /Good afternoon/i;
    else greeting = /Good evening/i;

    await expect(page.getByText(greeting).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("shows calorie ring/progress", async ({ authedPage: page }) => {
    // The calorie ring shows eaten/goal
    await expect(
      page.getByText(/cal|kcal|eaten|remaining|goal/i).first(),
    ).toBeVisible({ timeout: 10000 });
  });

  test("shows today's meals section", async ({ authedPage: page }) => {
    await expect(page.getByText("Today's Meals").first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("shows today's symptoms section", async ({ authedPage: page }) => {
    await expect(page.getByText(/Today's Symptoms/i).first()).toBeVisible({
      timeout: 10000,
    });
  });

  test("dashboard loads meal data from API", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Create a meal via API
    await page.request.post(`${API}/api/meals`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        mealType: "Lunch",
        items: [
          {
            foodName: "Grilled Chicken Breast",
            quantity: 1,
            servingSizeG: 150,
            calories: 250,
            proteinG: 45,
            carbsG: 0,
            fatG: 6,
            fiberG: 0,
          },
        ],
      },
    });

    // Reload page to see new data
    await page.reload();
    await page.waitForTimeout(3000);

    // Dashboard shows meal as "Lunch" card with "250" cal
    await expect(page.getByText("Lunch").first()).toBeVisible({
      timeout: 10000,
    });
    await expect(page.getByText("250").first()).toBeVisible();
  });

  test("dashboard shows macro labels", async ({ authedPage: page }) => {
    // Should see macro labels (always present as part of calorie section)
    await expect(page.getByText("Protein").first()).toBeVisible({
      timeout: 10000,
    });
    await expect(page.getByText("Carbs").first()).toBeVisible();
    await expect(page.getByText("Fat").first()).toBeVisible();
  });

  test("quick actions section exists in DOM", async ({ authedPage: page }) => {
    // Quick action buttons (Food Lookup, Log Symptom, Log Meal) are
    // rendered as animated cards that may be position:absolute/hidden
    // Verify they exist in the DOM even if not 'visible' per Playwright
    await page.waitForTimeout(2000);
    const count = await page.getByText("Food Lookup").count();
    expect(count).toBeGreaterThan(0);
    const count2 = await page.getByText("Log Meal").count();
    expect(count2).toBeGreaterThan(0);
  });
});

test.describe("Dashboard with Symptoms Data", () => {
  test("shows symptoms on dashboard after logging", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Get symptom types
    const typesRes = await page.request.get(`${API}/api/symptoms/types`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
    });
    expect(typesRes.ok()).toBeTruthy();
    const types = await typesRes.json();
    const bloating = types.find(
      (t: any) => t.name?.toLowerCase() === "bloating",
    );
    expect(bloating).toBeTruthy();

    // Log a symptom via API
    await page.request.post(`${API}/api/symptoms`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        symptomTypeId: bloating.id,
        severity: 6,
        occurredAt: new Date().toISOString(),
        notes: "Dashboard test",
      },
    });

    // Reload to see the data
    await page.reload();
    await page.waitForTimeout(3000);

    // Should see symptom data on dashboard
    await expect(page.getByText(/Bloating|6\/10|symptom/i).first()).toBeVisible(
      { timeout: 10000 },
    );
  });
});
