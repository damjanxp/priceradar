"""Captures the DAY-9 evidence screenshots of the lookup page at phone width (360 px).

Needs the local stack with seeded data:  docker compose up -d ; dotnet run --project src/PriceRadar.Seeder
Usage (repo root):  python docs/evidence/capture_ui.py [base_url]
"""
from __future__ import annotations

import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5080/"
OUT = Path(__file__).resolve().parent

CASES = [
    ("ui-cheap", "Prokleta avlija", "Andric", "300"),
    ("ui-fair", "Prokleta avlija", "Andric", "600"),
    ("ui-expensive", "Prokleta avlija", "Andric", "1500"),
    ("ui-cyrillic", "Проклета авлија", "Андрић", "450"),
    ("ui-ambiguous", "Derviš i smrt", "", "500"),
]


def lookup(page, title: str, author: str, price: str) -> None:
    page.fill("#title", title)
    page.fill("#author", author)
    page.fill("#price", price)
    # No wait_for_function: the page's CSP (no 'unsafe-eval') blocks Playwright's string evaluation.
    with page.expect_response(lambda r: "/api/lookup" in r.url):
        page.click("#submit")
    page.wait_for_selector("#submit:not([disabled])")


def main() -> None:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for scheme in ("light", "dark"):
            context = browser.new_context(viewport={"width": 360, "height": 780}, device_scale_factor=2,
                                          color_scheme=scheme, is_mobile=True, has_touch=True)
            page = context.new_page()
            errors: list[str] = []
            page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
            page.goto(BASE)
            cases = CASES if scheme == "light" else CASES[:1]
            for name, title, author, price in cases:
                lookup(page, title, author, price)
                suffix = "" if scheme == "light" else "-dark"
                page.screenshot(path=OUT / f"{name}{suffix}.png", full_page=True)
                print(f"{name}{suffix}: {page.inner_text('.verdict')}")
            if scheme == "light":
                with page.expect_response(lambda r: "/api/lookup" in r.url):
                    page.click(".candidate")   # tap the candidate of the last ambiguous case: re-runs with the author
                page.wait_for_selector("#submit:not([disabled])")
                page.screenshot(path=OUT / "ui-candidate-picked.png", full_page=True)
                print(f"ui-candidate-picked: {page.inner_text('.verdict')}")
            width = page.locator("html").evaluate("e => e.scrollWidth")
            print(f"{scheme}: scrollWidth={width} (no horizontal scroll if <= 360), console errors={errors}")
            context.close()
        browser.close()


if __name__ == "__main__":
    main()
