import { test, expect } from '@playwright/test';

test.describe('Lines (Routes) View', () => {
  test('should show empty state if no line found', async ({ page }) => {
    await page.goto('/lines');
    
    const searchInput = page.getByPlaceholder('Hat numarası veya adı ile ara...');
    await searchInput.fill('XYZ_NON_EXISTENT');
    
    // Check empty state
    await expect(page.locator('.empty-state', { hasText: 'Hat bulunamadı.' })).toBeVisible();
  });
  
  test('should navigate to line detail and parse headsigns properly', async ({ page }) => {
    await page.goto('/lines');
    
    // We expect some lines to load
    const lineCard = page.locator('.itinerary-card').first();
    await expect(lineCard).toBeVisible();
    await page.waitForTimeout(1000); // Wait for React to attach click listeners
    
    // Click on a line
    await lineCard.click({ force: true });
    await page.waitForTimeout(500); // Give React time to re-render
    
    // It should load the line detail view
    const backBtn = page.locator('.clear-btn').first();
    await expect(backBtn).toBeVisible({ timeout: 10000 });
    
    // Headsign logic check
    const dirBtn = page.locator('.action-btn').first();
    await expect(dirBtn).toBeVisible();
    const btnText = await dirBtn.textContent();
    expect(btnText).toContain('Yön');
  });
});
