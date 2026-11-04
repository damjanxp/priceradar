# Lighthouse (DAY-9)

Lighthouse 12.6.0, mobile form factor, 360 px wide, headless Chromium, `http://localhost:5080/`, 2026-11-04.

| Category | Score |
|---|---|
| Accessibility | 100 |
| Best practices | 100 |

Reproduce: `npx lighthouse@12.6.0 http://localhost:5080/ --only-categories=accessibility,best-practices --form-factor=mobile --screenEmulation.width=360`
