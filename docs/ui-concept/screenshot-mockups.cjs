const { chromium } = require('/opt/node22/lib/node_modules/playwright');
const { join } = require('path');

const mockups = join(__dirname, 'mockups');
const out = __dirname;

const jobs = [
    { file: 'dashboard-mobile.html', name: 'preview-dashboard-mobile', w: 390, h: 844 },
    { file: 'containers-mobile-cards.html', name: 'preview-containers-mobile', w: 390, h: 844 },
    { file: 'detail-mobile.html', name: 'preview-detail-mobile', w: 390, h: 844 },
    { file: 'dashboard-dark-mobile.html', name: 'preview-dashboard-dark-mobile', w: 390, h: 844 },
    { file: 'dashboard-desktop.html', name: 'preview-dashboard-desktop', w: 1280, h: 800 },
];

(async () => {
    const browser = await chromium.launch();
    let failures = 0;
    for (const job of jobs) {
        const page = await browser.newPage({ viewport: { width: job.w, height: job.h }, deviceScaleFactor: 2 });
        await page.goto('file://' + join(mockups, job.file));
        await page.waitForTimeout(150);
        const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
        const innerWidth = await page.evaluate(() => window.innerWidth);
        if (scrollWidth > innerWidth) {
            failures++;
            console.log(`OVERFLOW  ${job.file}: scrollWidth=${scrollWidth} > innerWidth=${innerWidth}`);
        } else {
            console.log(`OK        ${job.file}: scrollWidth=${scrollWidth} <= innerWidth=${innerWidth}`);
        }
        await page.screenshot({ path: join(out, job.name + '.png'), fullPage: true });
        await page.close();
    }
    await browser.close();
    console.log(failures === 0 ? 'ALL OK' : `${failures} OVERFLOW FAILURE(S)`);
    process.exit(failures === 0 ? 0 : 1);
})();
