# Lighthouse

Lighthouse 12.6.0, mobile form factor, 360 px wide, headless Chromium, 2026-11-04, after the library-theme redesign.

| Page | Accessibility | Best practices |
|---|---|---|
| `http://localhost:5080/#/check` (price check) | 100 | 100 |
| `http://localhost:5080/#/catalog` (catalogue) | 100 | 100 |

The first catalogue run scored 99 / 96 (heading order, accessible names of the cards, 11.5 px labels); all three were
fixed before this run.

Reproduce: `npx lighthouse@12.6.0 "http://localhost:5080/#/catalog" --only-categories=accessibility,best-practices --form-factor=mobile --screenEmulation.width=360`
