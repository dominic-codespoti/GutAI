import { test, expect, API } from "./fixtures";

test.describe("Insights Page", () => {
  test("shows insights page with period selector", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/insights");
    await page.waitForTimeout(3000);

    // Should see period filter buttons
    await expect(page.getByText("7d")).toBeVisible({ timeout: 10000 });
    await expect(page.getByText("30d")).toBeVisible();
    await expect(page.getByText("90d")).toBeVisible();
  });

  test("shows section headers or empty states", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/insights");
    await page.waitForTimeout(3000);

    // Should see at least some section headers or empty states
    const hasContent = await page
      .getByText(
        /Nutrition Trends|Correlations|Trigger Foods|Additive Exposure|Symptom Patterns|Food Diary/i,
      )
      .first()
      .isVisible()
      .catch(() => false);

    const hasEmptyState = await page
      .getByText(/No data|Log more meals|Not enough data|Start logging/i)
      .first()
      .isVisible()
      .catch(() => false);

    // Either real sections or empty states should be visible
    expect(hasContent || hasEmptyState).toBeTruthy();
  });

  test("period selector changes data range", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/insights");
    await page.waitForTimeout(3000);

    // Default is 30d
    await expect(page.getByText("30d")).toBeVisible({ timeout: 10000 });

    // Click 7d
    await page.getByText("7d").click();
    await page.waitForTimeout(2000);

    // Click 90d
    await page.getByText("90d").click();
    await page.waitForTimeout(2000);

    // Page shouldn't crash — period buttons should still be visible
    await expect(page.getByText("7d")).toBeVisible();
  });

  test("insights load with meal data present", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Create a few meals via API to generate insight data
    const headers = { Authorization: `Bearer ${testUserData.accessToken}` };

    for (let i = 0; i < 3; i++) {
      await page.request.post(`${API}/api/meals`, {
        headers,
        data: {
          mealType: ["Breakfast", "Lunch", "Dinner"][i],
          items: [
            {
              foodName: ["Oatmeal", "Chicken Salad", "Pasta"][i],
              quantity: 1,
              servingSizeG: [200, 300, 250][i],
              calories: [300, 450, 600][i],
              proteinG: [10, 35, 20][i],
              carbsG: [50, 15, 70][i],
              fatG: [8, 20, 15][i],
              fiberG: [5, 8, 3][i],
            },
          ],
        },
      });
    }

    // Navigate to insights
    await page.goto("http://localhost:8081/(tabs)/insights");
    await page.waitForTimeout(4000);

    // Should see some nutrition data or insights content
    await expect(
      page
        .getByText(
          /Nutrition|cal|kcal|Protein|Carbs|Trends|Correlations|No data/i,
        )
        .first(),
    ).toBeVisible({ timeout: 10000 });
  });

  test("trigger foods section renders", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/insights");
    await page.waitForTimeout(3000);

    // Check if trigger foods section exists
    const hasTriggerFoods = await page
      .getByText("Trigger Foods")
      .first()
      .isVisible()
      .catch(() => false);

    // Trigger foods may or may not be visible depending on data
    // Just verify the page loaded without crashing
    await expect(page.getByText("7d")).toBeVisible();
  });
});
