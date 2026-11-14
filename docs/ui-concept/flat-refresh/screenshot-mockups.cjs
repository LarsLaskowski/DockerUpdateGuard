// Renders the static mockups in mockups/ to PNG previews next to this script.
// Usage: node screenshot-mockups.cjs   (needs the "playwright" package and its Chromium)
const { chromium } = require('playwright');
const { join } = require('path');

const mockups = join(__dirname, 'mockups');
const out = __dirname;

const pages = [
    'dashboard',
    'dashboard-all-clear',
    'dashboard-dark',
    'runtime-containers',
    'runtime-container-detail',
    'observed-images',
    'vulnerabilities',
    'my-images',
    'base-images',
    'docker-instances',
    'scan-history',
];

const jobs = pages.map(name => ({ file: name + '.html', name: 'preview-' + name, w: 1360, h: 480 }));

(async () => {
    const browser = await chromium.launch();
    let failures = 0;

    for (const job of jobs) {
        const page = await browser.newPage({ viewport: { width: job.w, height: job.h }, deviceScaleFactor: 2 });

        await page.goto('file://' + join(mockups, job.file));
        await page.evaluate(() => document.fonts.ready);
        await page.waitForTimeout(150);

        const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
        const innerWidth = await page.evaluate(() => window.innerWidth);

        if (scrollWidth > innerWidth) {
            failures++;
            console.log(`OVERFLOW  ${job.name}: scrollWidth=${scrollWidth} > innerWidth=${innerWidth}`);
        } else {
            console.log(`OK        ${job.name}: scrollWidth=${scrollWidth} <= innerWidth=${innerWidth}`);
        }

        await page.screenshot({ path: join(out, job.name + '.png'), fullPage: true });
        await page.close();
    }

    await browser.close();
    console.log(failures === 0 ? 'ALL OK' : `${failures} OVERFLOW FAILURE(S)`);
    process.exit(failures === 0 ? 0 : 1);
})();
