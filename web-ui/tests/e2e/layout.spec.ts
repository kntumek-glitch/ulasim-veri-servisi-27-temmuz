import { test, expect } from '@playwright/test';

test.describe('Panel Collapse/Expand behavior', () => {
  const checkPanelCollapse = async (page: any, url: string) => {
    await page.goto(url);
    const container = page.locator('.planner-container');
    const toggleBtn = page.locator('.collapse-toggle-btn');
    
    // Should be visible initially
    await expect(container).toBeVisible();
    await expect(container).not.toHaveClass(/collapsed/);
    
    // Collapse
    await toggleBtn.click();
    await expect(container).toHaveClass(/collapsed/);
    
    // Expand
    await toggleBtn.click();
    await expect(container).not.toHaveClass(/collapsed/);
  };

  test('Trip Planner collapse', async ({ page }) => {
    await checkPanelCollapse(page, '/');
  });

  test('Lines collapse', async ({ page }) => {
    await checkPanelCollapse(page, '/lines');
  });

  test('Stops collapse', async ({ page }) => {
    await checkPanelCollapse(page, '/stops');
  });

  test('System Status collapse', async ({ page }) => {
    await checkPanelCollapse(page, '/status');
  });
});
