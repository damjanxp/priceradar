"""Builds docs/spec/PriceRadar-Spec.pdf from PriceRadar-Spec.md.

Pipeline: Markdown -> HTML (python-markdown + Pygments) -> PDF (Playwright Chromium)
-> PyMuPDF post-processing (TOC page numbers, footers, bookmarks).

Usage (from the repo root):
    node docs/spec/tools/render-dot.mjs          # only when a diagrams/*.dot file changed
    python docs/spec/tools/build_pdf.py [--png]  # --png also rasterises every page for QA
Requires: pip install markdown pygments playwright pymupdf ; python -m playwright install chromium
"""
from __future__ import annotations

import html
import re
import sys
from pathlib import Path

import markdown
import pymupdf
from playwright.sync_api import sync_playwright
from pygments.formatters import HtmlFormatter

SPEC_DIR = Path(__file__).resolve().parent.parent
MD_PATH = SPEC_DIR / "PriceRadar-Spec.md"
HTML_PATH = SPEC_DIR / "build" / "PriceRadar-Spec.html"
PDF_PATH = SPEC_DIR / "PriceRadar-Spec.pdf"
PNG_DIR = SPEC_DIR / "build" / "pages"
CSS_PATH = Path(__file__).resolve().parent / "spec.css"

FOOTER_LEFT = "PriceRadar — Technical Specification v1.4"
# h2 sections that always start on a new page (ids produced by the toc extension)
NEW_PAGE_SECTIONS = ("4-architecture", "13-the-10-day-plan")


def md_to_html(text: str) -> tuple[str, list]:
    md = markdown.Markdown(
        extensions=["extra", "sane_lists", "toc", "codehilite"],
        extension_configs={
            "codehilite": {"guess_lang": False, "css_class": "codehilite"},
            "toc": {"toc_depth": "2-3"},
        },
    )
    body = md.convert(text)
    return body, md.toc_tokens


def postprocess(body: str) -> str:
    # the HTML is written to build/, so point images at the diagrams folder explicitly
    body = body.replace('src="diagrams/', f'src="{(SPEC_DIR / "diagrams").as_uri()}/')
    # "\|" escapes a pipe inside a table cell; python-markdown leaves the backslash in code spans
    body = body.replace("\\|", "|")
    # stable IDs (FR-1, D-07, R-10 …) must not wrap inside narrow first columns
    body = re.sub(r"<td>((?:FR|NFR|G|D|R|S|DAY)-\d+)</td>", r'<td class="id">\1</td>', body)
    # GitHub-style task lists: "- [ ] text" -> checkbox glyph
    body = body.replace("<li>[ ] ", '<li class="task">').replace("<li>[x] ", '<li class="task done">')
    body = re.sub(r"<ul>\s*(<li class=\"task)", r'<ul class="tasks">\1', body)
    # page breaks before selected major sections
    for sid in NEW_PAGE_SECTIONS:
        body = body.replace(f'<h2 id="{sid}">', f'<h2 id="{sid}" class="newpage">')
    # DAY headings get a coloured band; the Decision Log table gets fixed column widths
    body = re.sub(r'<h3 id="(day-\d+[^"]*)">', r'<h3 id="\1" class="day">', body)
    i = body.find('<h2 id="15-decision-log"')
    if i >= 0:
        j = body.find("<table>", i)
        body = body[:j] + '<table class="decisions">' + body[j + len("<table>"):]
    # "HOW TO TEST THIS PHASE" headings get an accent style
    body = body.replace('<h4 id="how-to-test-this-phase', '<h4 class="howto" id="how-to-test-this-phase')
    return body


def build_toc(tokens: list, pages: dict[str, int] | None) -> str:
    def entry(tok, level):
        page = str(pages.get(tok["id"], "")) if pages else "000"
        name = re.sub(r"<[^>]+>", "", html.unescape(tok["name"]))
        return (f'<li class="toc-l{level}"><a href="#{tok["id"]}"><span class="toc-text">{html.escape(name)}</span>'
                f'<span class="toc-dots"></span><span class="toc-page">{page}</span></a></li>')

    items = []
    for h2 in tokens:
        items.append(entry(h2, 2))
        for h3 in h2.get("children", []):
            items.append(entry(h3, 3))
    return '<nav class="toc"><h2 class="toc-title">Contents</h2><ul>' + "".join(items) + "</ul></nav>"


def build_html(pages: dict[str, int] | None) -> tuple[str, list]:
    text = MD_PATH.read_text(encoding="utf-8")
    split = text.index("\n## ")
    title_md, body_md = text[:split], text[split + 1:]
    title_html, _ = md_to_html(title_md)
    body_html, tokens = md_to_html(body_md)
    body_html = postprocess(body_html)
    css = CSS_PATH.read_text(encoding="utf-8") + HtmlFormatter(style="friendly").get_style_defs(".codehilite")
    doc = f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>PriceRadar — Technical Specification</title><style>{css}</style></head>
<body>
<section class="titlepage">
  <div class="kicker">Portfolio project · Backend C# / .NET 9</div>
  {title_html}
  <div class="title-foot">Source of truth: <code>docs/spec/PriceRadar-Spec.md</code> · IDs FR-x, NFR-x, D-xx, R-x, DAY-x are stable across versions.</div>
</section>
{build_toc(tokens, pages)}
<main>{body_html}</main>
</body></html>"""
    return doc, tokens


def render_pdf(doc: str, out: Path) -> None:
    HTML_PATH.parent.mkdir(parents=True, exist_ok=True)
    HTML_PATH.write_text(doc, encoding="utf-8")
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page()
        page.goto(HTML_PATH.as_uri(), wait_until="networkidle")
        page.pdf(path=str(out), format="A4", print_background=True, prefer_css_page_size=True)
        browser.close()


def flatten(tokens: list) -> list:
    out = []
    for t in tokens:
        out.append((2, t))
        out.extend((3, c) for c in t.get("children", []))
    return out


def heading_pages(pdf: Path, tokens: list) -> dict[str, int]:
    """Resolve each heading's page from the TOC's internal links (1-based page numbers)."""
    ids = [t["id"] for _, t in flatten(tokens)]
    result: dict[str, int] = {}
    with pymupdf.open(pdf) as d:
        for page in d:
            for link in page.get_links():
                dest = link.get("nameddest") or link.get("name") or ""
                dest = dest.lstrip("#")
                if dest in ids and dest not in result and link.get("page", -1) >= 0:
                    result[dest] = link["page"] + 1
    missing = [i for i in ids if i not in result]
    if missing:
        raise SystemExit(f"Could not resolve TOC pages for: {missing[:5]} …")
    return result


def finish(pdf: Path, tokens: list, pages: dict[str, int]) -> int:
    with pymupdf.open(pdf) as d:
        total = d.page_count
        for i, page in enumerate(d):
            if i == 0:
                continue  # title page: no footer
            w, h = page.rect.width, page.rect.height
            y = h - 28
            page.draw_line((45, y - 9), (w - 45, y - 9), color=(0.75, 0.79, 0.84), width=0.5)
            page.insert_text((45, y), FOOTER_LEFT, fontsize=7.5, fontname="helv", color=(0.35, 0.4, 0.47))
            label = f"Page {i + 1} of {total}"
            tw = pymupdf.get_text_length(label, fontname="helv", fontsize=7.5)
            page.insert_text((w - 45 - tw, y), label, fontsize=7.5, fontname="helv", color=(0.35, 0.4, 0.47))
        toc = [[lvl - 1, re.sub(r"<[^>]+>", "", html.unescape(t["name"])), pages[t["id"]]] for lvl, t in flatten(tokens)]
        d.set_toc(toc)
        d.set_metadata({"title": "PriceRadar — Technical Specification", "author": "Damjan Petrov",
                        "subject": "MVP specification and 10-day plan", "creator": "build_pdf.py (Chromium + PyMuPDF)"})
        d.save(PDF_PATH, garbage=3, deflate=True)
    return total


def rasterise(pdf: Path) -> None:
    PNG_DIR.mkdir(parents=True, exist_ok=True)
    for old in PNG_DIR.glob("*.png"):
        old.unlink()
    with pymupdf.open(pdf) as d:
        for i, page in enumerate(d):
            page.get_pixmap(dpi=80).save(PNG_DIR / f"page-{i + 1:02d}.png")


def main() -> None:
    tmp1 = SPEC_DIR / "build" / "pass1.pdf"
    tmp2 = SPEC_DIR / "build" / "pass2.pdf"
    doc, tokens = build_html(None)
    render_pdf(doc, tmp1)
    pages = heading_pages(tmp1, tokens)
    doc, tokens = build_html(pages)
    render_pdf(doc, tmp2)
    pages2 = heading_pages(tmp2, tokens)
    if pages2 != pages:  # TOC width changes can shift pages; one more pass settles it
        doc, tokens = build_html(pages2)
        render_pdf(doc, tmp2)
        pages = heading_pages(tmp2, tokens)
    total = finish(tmp2, tokens, pages)
    print(f"wrote {PDF_PATH} ({total} pages)")
    if "--png" in sys.argv:
        rasterise(PDF_PATH)
        print(f"page images in {PNG_DIR}")


if __name__ == "__main__":
    main()
