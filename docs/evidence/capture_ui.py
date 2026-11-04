"""Captures the evidence screenshots of the web UI: price check, catalogue and a book card, at phone width (360 px)
and on a desktop, in light and dark mode.

Needs the local stack with data:  docker compose up -d  (plus the fixture runs or the seeder, see README)
Usage (repo root):  python docs/evidence/capture_ui.py [base_url]
"""
from __future__ import annotations

import sys
from pathlib import Path

from playwright.sync_api import Page, sync_playwright

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5080/").rstrip("/") + "/"
OUT = Path(__file__).resolve().parent

CASES = [
    ("ui-cheap", "Prokleta avlija", "Andric", "300"),
    ("ui-fair", "Prokleta avlija", "Andric", "600"),
    ("ui-expensive", "Prokleta avlija", "Andric", "1500"),
    ("ui-cyrillic", "Проклета авлија", "Андрић", "450"),
    ("ui-ambiguous", "Derviš i smrt", "", "500"),
]

# No wait_for_function anywhere: the page's CSP (no 'unsafe-eval') blocks Playwright's string evaluation.


def lookup(page: Page, title: str, author: str, price: str) -> None:
    page.fill("#title", title)
    page.fill("#author", author)
    page.fill("#price", price)
    with page.expect_response(lambda r: "/api/lookup" in r.url):
        page.click("#submit")
    page.wait_for_selector("#submit:not([disabled])")
    page.wait_for_timeout(350)   # let the stamp animation finish


def open_catalog(page: Page) -> None:
    with page.expect_response(lambda r: "/api/catalog" in r.url):
        page.goto(BASE + "#/catalog")
    page.wait_for_selector("#catalog .cat-card")


def open_book(page: Page) -> None:
    page.click("#catalog .cat-card")
    page.wait_for_selector("#book .chart")


def report(page: Page, label: str, errors: list[str]) -> None:
    width = page.locator("html").evaluate("e => e.scrollWidth")
    viewport = page.viewport_size["width"]
    print(f"{label}: scrollWidth={width} (viewport {viewport}), console errors={errors}")


def main() -> None:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for scheme in ("light", "dark"):
            suffix = "" if scheme == "light" else "-dark"
            phone = browser.new_context(viewport={"width": 360, "height": 780}, device_scale_factor=2,
                                        color_scheme=scheme, is_mobile=True, has_touch=True)
            page = phone.new_page()
            errors: list[str] = []
            page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
            page.goto(BASE + "#/check")
            for name, title, author, price in (CASES if scheme == "light" else CASES[:1]):
                lookup(page, title, author, price)
                page.screenshot(path=OUT / f"{name}{suffix}.png", full_page=True)
                print(f"{name}{suffix}: {page.inner_text('.stamp')}")
            if scheme == "light":
                with page.expect_response(lambda r: "/api/lookup" in r.url):
                    page.click(".candidate .cat-card, .candidates .cat-card")   # confirm the single candidate
                page.wait_for_selector("#submit:not([disabled])")
                page.screenshot(path=OUT / "ui-candidate-picked.png", full_page=True)
                print(f"ui-candidate-picked: {page.inner_text('.stamp')}")
            open_catalog(page)
            page.screenshot(path=OUT / f"ui-catalog-phone{suffix}.png")
            open_book(page)
            page.screenshot(path=OUT / f"ui-book-phone{suffix}.png")
            report(page, f"phone {scheme}", errors)
            phone.close()

            desktop = browser.new_context(viewport={"width": 1280, "height": 860}, color_scheme=scheme)
            page = desktop.new_page()
            errors = []
            page.on("console", lambda m: errors.append(m.text) if m.type == "error" else None)
            page.goto(BASE + "#/check")
            page.screenshot(path=OUT / f"ui-desktop-start{suffix}.png")
            lookup(page, "Prokleta avlija", "Andric", "450")
            page.screenshot(path=OUT / f"ui-desktop-check{suffix}.png")
            open_catalog(page)
            page.screenshot(path=OUT / f"ui-desktop-catalog{suffix}.png")
            open_book(page)
            page.screenshot(path=OUT / f"ui-desktop-book{suffix}.png")
            report(page, f"desktop {scheme}", errors)
            desktop.close()
        browser.close()


if __name__ == "__main__":
    main()
