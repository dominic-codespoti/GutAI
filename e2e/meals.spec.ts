import { test, expect, API } from "./fixtures";

test.describe("Meals Tab", () => {
  test("shows empty state when no meals logged", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Should see empty state message
    await expect(
      page.getByText(/No meals logged for this date|No meals found/i).first(),
    ).toBeVisible({ timeout: 10000 });

    // Should see helper text
    await expect(page.getByText(/Tap \+ to log/i).first()).toBeVisible({
      timeout: 5000,
    });
  });

  test("FAB opens with three options: Describe, Manual, Search", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Click the main FAB (borderRadius: 28 = 56/2)
    await page
      .locator('[style*="border-radius: 28"]')
      .last()
      .click({ force: true });
    await page.waitForTimeout(1000);

    // Should see 3 FAB action labels
    await expect(page.getByText("Describe")).toBeVisible({ timeout: 5000 });
    await expect(page.getByText("Manual")).toBeVisible();
    await expect(page.getByText("Search")).toBeVisible();
  });

  test("can open the describe meal sheet", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Open FAB
    await page
      .locator('[style*="border-radius: 28"]')
      .last()
      .click({ force: true });
    await page.waitForTimeout(1000);

    // Click Describe action label
    await page.getByText("Describe").click({ force: true });
    await page.waitForTimeout(1500);

    // Should see the AddMealSheet in describe mode
    await expect(
      page.getByText(/Describe|What did you eat|Log Meal/i).first(),
    ).toBeVisible({ timeout: 5000 });
  });

  test("can open the manual meal sheet", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Open FAB
    await page
      .locator('[style*="border-radius: 28"]')
      .last()
      .click({ force: true });
    await page.waitForTimeout(1000);

    // Click Manual action label
    await page.getByText("Manual").click({ force: true });
    await page.waitForTimeout(1500);

    // Should see manual entry mode
    await expect(
      page.getByText(/Manual|Search foods|Add Food|Log Meal/i).first(),
    ).toBeVisible({ timeout: 5000 });
  });

  test("meal type chips are visible", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Check that filter chips are present
    await expect(page.getByText("All", { exact: true }).first()).toBeVisible({
      timeout: 10000,
    });
    await expect(
      page.getByText("Breakfast", { exact: true }).first(),
    ).toBeVisible();
    await expect(
      page.getByText("Lunch", { exact: true }).first(),
    ).toBeVisible();
    await expect(
      page.getByText("Dinner", { exact: true }).first(),
    ).toBeVisible();
    await expect(
      page.getByText("Snack", { exact: true }).first(),
    ).toBeVisible();
  });

  test("date navigation shows today", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(3000);

    // Should see "Today" label
    await expect(page.getByText("Today").first()).toBeVisible({
      timeout: 5000,
    });
  });

  test("daily summary loads after creating meal via API", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Create a meal via API
    const res = await page.request.post(`${API}/api/meals`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        mealType: "Breakfast",
        items: [
          {
            foodName: "Banana",
            quantity: 1,
            servingSizeG: 118,
            calories: 105,
            proteinG: 1.3,
            carbsG: 27,
            fatG: 0.4,
            fiberG: 3.1,
          },
        ],
      },
    });
    expect(res.ok()).toBeTruthy();

    // Navigate to meals tab
    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(4000);

    // Should see the meal with Banana
    await expect(page.getByText("Banana").first()).toBeVisible({
      timeout: 15000,
    });

    // Should see Breakfast label
    await expect(page.getByText("Breakfast").first()).toBeVisible();
  });

  test("multiple meals show grouped by type", async ({
    authedPage: page,
    testUserData,
  }) => {
    // Create breakfast
    await page.request.post(`${API}/api/meals`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        mealType: "Breakfast",
        items: [
          {
            foodName: "Oatmeal",
            quantity: 1,
            servingSizeG: 200,
            calories: 150,
            proteinG: 5,
            carbsG: 27,
            fatG: 3,
            fiberG: 4,
          },
        ],
      },
    });

    // Create lunch
    await page.request.post(`${API}/api/meals`, {
      headers: { Authorization: `Bearer ${testUserData.accessToken}` },
      data: {
        mealType: "Lunch",
        items: [
          {
            foodName: "Chicken Salad",
            quantity: 1,
            servingSizeG: 300,
            calories: 350,
            proteinG: 30,
            carbsG: 15,
            fatG: 18,
            fiberG: 5,
          },
        ],
      },
    });

    await page.goto("http://localhost:8081/(tabs)/meals");
    await page.waitForTimeout(4000);

    // Should see both meal items
    await expect(page.getByText("Oatmeal").first()).toBeVisible({
      timeout: 15000,
    });
    await expect(page.getByText("Chicken Salad").first()).toBeVisible({
      timeout: 10000,
    });
  });
});
