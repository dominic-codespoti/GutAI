import { test, expect, API } from "./fixtures";

test.describe("Food Search & Scan", () => {
  test("can navigate to scan/search screen", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    // Should see the food lookup heading
    await expect(page.getByText("Food Lookup")).toBeVisible({ timeout: 10000 });

    // Should see search input and barcode input
    await expect(page.getByPlaceholder("Search by name...")).toBeVisible({
      timeout: 5000,
    });
    await expect(page.getByPlaceholder("Enter barcode number")).toBeVisible();
  });

  test("search returns results for common foods", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    // Type a search query
    const searchInput = page.getByPlaceholder("Search by name...");
    await searchInput.fill("banana");
    await page.waitForTimeout(3000); // Wait for debounce + API

    // Should see results containing banana
    await expect(page.getByText(/banana/i).first()).toBeVisible({
      timeout: 15000,
    });
  });

  test("search shows no results for gibberish", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    const searchInput = page.getByPlaceholder("Search by name...");
    await searchInput.fill("xyzqwrtyuiop12345");
    await page.waitForTimeout(3000);

    // The search should not show any food results — just the search field
    // No explicit "no results" message exists in the app for text search
    await expect(searchInput).toHaveValue("xyzqwrtyuiop12345");

    // Verify no food result items appeared (no cal/kcal text from food cards)
    const hasFoodResult = await page
      .getByText(/\d+\s*cal/i)
      .first()
      .isVisible()
      .catch(() => false);
    expect(hasFoodResult).toBeFalsy();
  });

  test("can select a food and see its details", async ({
    authedPage: page,
  }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    // Search for a food
    const searchInput = page.getByPlaceholder("Search by name...");
    await searchInput.fill("apple");
    await page.waitForTimeout(3000);

    // Click on the first result
    const firstResult = page.getByText(/apple/i).first();
    await firstResult.click();
    await page.waitForTimeout(2000);

    // Should see food details — calories, nutrition info
    await expect(
      page.getByText(/Calories|cal|kcal|Nutrition|Protein|Carbs/i).first(),
    ).toBeVisible({ timeout: 10000 });
  });

  test("barcode input accepts input", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    // Barcode input field should be visible
    const barcodeInput = page.getByPlaceholder("Enter barcode number");
    await expect(barcodeInput).toBeVisible({ timeout: 5000 });

    // Can type a barcode
    await barcodeInput.fill("0011110838001");
    await page.waitForTimeout(1000);

    // The input should have the value
    await expect(barcodeInput).toHaveValue("0011110838001");
  });

  test("search results show food names", async ({ authedPage: page }) => {
    await page.goto("http://localhost:8081/(tabs)/scan");
    await page.waitForTimeout(3000);

    // Search for a common food
    const searchInput = page.getByPlaceholder("Search by name...");
    await searchInput.fill("chicken");
    await page.waitForTimeout(3000);

    // Should see search results with food name
    await expect(page.getByText(/chicken/i).first()).toBeVisible({
      timeout: 15000,
    });
  });
});
