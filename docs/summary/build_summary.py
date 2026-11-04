"""Builds docs/summary/PriceRadar-Summary.pdf from PriceRadar-Summary.md, reusing the spec's stylesheet.

Usage (repo root):  python docs/summary/build_summary.py [--png]
Requires the same packages as docs/spec/tools/build_pdf.py.
"""
from __future__ import annotations

import sys
from pathlib import Path

import markdown
import pymupdf
from playwright.sync_api import sync_playwright
from pygments.formatters import HtmlFormatter

HERE = Path(__file__).resolve().parent
MD_PATH = HERE / "PriceRadar-Summary.md"
HTML_PATH = HERE / "PriceRadar-Summary.html"   # next to the .md so ../evidence/*.png resolves; git-ignored
PDF_PATH = HERE / "PriceRadar-Summary.pdf"
SPEC_CSS = HERE.parent / "spec" / "tools" / "spec.css"

EXTRA_CSS = """
body { font-size: 9.5pt; }
h1 { font-size: 20pt; color: var(--accent); margin: 0 0 2pt; }
h1 + p em { color: var(--muted); }
h2 { break-before: auto !important; page-break-before: auto !important; margin-top: 12pt; }
.shots { display: flex; gap: 6mm; justify-content: center; margin: 6pt 0 4pt; break-inside: avoid; }
.shots img { width: 29mm; border: 1px solid var(--rule); border-radius: 3mm; }
table { break-inside: auto; }
tr { break-inside: avoid; }
"""


def main() -> None:
    body = markdown.markdown(MD_PATH.read_text(encoding="utf-8"), extensions=["extra", "sane_lists", "codehilite"],
                             extension_configs={"codehilite": {"guess_lang": False}})
    css = SPEC_CSS.read_text(encoding="utf-8") + HtmlFormatter(style="friendly").get_style_defs(".codehilite") + EXTRA_CSS
    doc = f'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>PriceRadar summary</title><style>{css}</style></head><body>{body}</body></html>'
    HTML_PATH.write_text(doc, encoding="utf-8")
    tmp = HERE / "_raw.pdf"
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page()
        page.goto(HTML_PATH.as_uri(), wait_until="networkidle")
        page.pdf(path=str(tmp), format="A4", print_background=True, prefer_css_page_size=True)
        browser.close()
    with pymupdf.open(tmp) as d:
        total = d.page_count
        for i, page in enumerate(d, start=1):
            y = page.rect.height - 28
            page.insert_text((45, y), "PriceRadar — project summary", fontsize=7.5, fontname="helv", color=(0.35, 0.4, 0.47))
            page.insert_text((page.rect.width - 95, y), f"Page {i} of {total}", fontsize=7.5, fontname="helv", color=(0.35, 0.4, 0.47))
        d.save(PDF_PATH, garbage=3, deflate=True)
    tmp.unlink()
    HTML_PATH.unlink()
    print(f"wrote {PDF_PATH} ({total} pages)")
    if "--png" in sys.argv:
        out = HERE / "pages"
        out.mkdir(exist_ok=True)
        with pymupdf.open(PDF_PATH) as d:
            for i, page in enumerate(d, start=1):
                page.get_pixmap(dpi=80).save(out / f"page-{i}.png")
        print(f"page images in {out}")


if __name__ == "__main__":
    main()
