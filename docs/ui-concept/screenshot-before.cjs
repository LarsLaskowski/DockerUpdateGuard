const { chromium } = require('/opt/node22/lib/node_modules/playwright');
const { join } = require('path');
const base = process.env.DUG_BASE_URL || 'http://localhost:8080';
const out = join(__dirname, 'before');
require('fs').mkdirSync(out, { recursive: true });

const pages = [
    { path: '/', name: 'dashboard' },
    { path: '/runtime-containers', name: 'runtime-containers' },
    { path: '/vulnerabilities', name: 'vulnerabilities' },
    { path: '/docker-instances', name: 'docker-instances' },
];
const widths = [ { w: 390, h: 844, tag: '390' }, { w: 768, h: 1024, tag: '768' }, { w: 1280, h: 800, tag: '1280' } ];

(async () => {
    const browser = await chromium.launch();
    for (const p of pages) {
        for (const vp of widths) {
            const page = await browser.newPage({ viewport: { width: vp.w, height: vp.h }, deviceScaleFactor: 1 });
            try {
                await page.goto(base + p.path, { waitUntil: 'networkidle', timeout: 15000 });
            } catch (e) {
                await page.goto(base + p.path, { waitUntil: 'load', timeout: 15000 }).catch(()=>{});
            }
            await page.waitForTimeout(1200);
            const sw = await page.evaluate(() => document.documentElement.scrollWidth);
            const iw = await page.evaluate(() => window.innerWidth);
            const flag = sw > iw ? 'OVERFLOW' : 'ok';
            console.log(`${flag.padEnd(9)} ${p.name} @${vp.tag}: scrollWidth=${sw} innerWidth=${iw}`);
            await page.screenshot({ path: join(out, `${p.name}-${vp.tag}.png`), fullPage: true });
            await page.close();
        }
    }
    await browser.close();
})();
