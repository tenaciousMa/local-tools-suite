import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright");
const baseUrl = process.env.SITE_URL || "http://127.0.0.1:4174";
const chromePath =
  process.env.CHROME_PATH || "C:/Program Files/Google/Chrome/Application/chrome.exe";

const browser = await chromium.launch({
  headless: true,
  executablePath: chromePath,
});

try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  page.on("pageerror", (error) => errors.push(error.message));

  const pages = ["/", "/renamer/", "/video-grid/"];
  const results = [];
  for (const path of pages) {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(new URL(path, baseUrl).href, { waitUntil: "networkidle" });
    const desktop = await page.evaluate(() => ({
      title: document.title,
      h1: document.querySelector("h1")?.textContent.trim(),
      viewportWidth: window.innerWidth,
      scrollWidth: document.documentElement.scrollWidth,
      brokenImages: [...document.images]
        .filter((image) => !image.complete || image.naturalWidth === 0)
        .map((image) => image.src),
      downloadLinks: [...document.querySelectorAll('a[href*="/releases/"]')].map((link) => link.href),
    }));

    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload({ waitUntil: "networkidle" });
    const mobile = await page.evaluate(() => ({
      viewportWidth: window.innerWidth,
      scrollWidth: document.documentElement.scrollWidth,
      headerHeight: document.querySelector(".site-header")?.getBoundingClientRect().height,
      offenders: [...document.querySelectorAll("body *")]
        .map((element) => {
          const rect = element.getBoundingClientRect();
          return {
            tag: element.tagName.toLowerCase(),
            className: element.className?.toString() || "",
            width: Math.round(rect.width),
            left: Math.round(rect.left),
            right: Math.round(rect.right),
          };
        })
        .filter((item) => item.right > window.innerWidth + 1 || item.left < -1)
        .slice(0, 12),
    }));

    results.push({ path, desktop, mobile });
    if (
      desktop.brokenImages.length ||
      desktop.scrollWidth > desktop.viewportWidth ||
      mobile.scrollWidth > mobile.viewportWidth
    ) {
      process.exitCode = 1;
    }
  }

  console.log(JSON.stringify({ pages: results, errors }, null, 2));
  if (errors.length) {
    process.exitCode = 1;
  }
} finally {
  await browser.close();
}
