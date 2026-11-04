// PriceRadar front end: price check (GET /api/lookup), catalogue (GET /api/catalog) and a book card
// (GET /api/books/{id}/…). Vanilla JS, no build step; no inline script or style attributes because of the CSP.
"use strict";

const $ = (id) => document.getElementById(id);
const SVG_NS = "http://www.w3.org/2000/svg";
const RECENT_KEY = "priceradar.recent";
const SOLD_PROXY_NOTE = "Listings that stopped appearing in two scrapes in a row. Probably sold, but they may also have been withdrawn: marketplaces do not publish sales.";
const SOURCE_NAMES = { fixture: "Fixture", kp: "KP-style", synthetic: "Synthetic", kupindo: "Kupindo" };

const LABELS = {
  cheap: "Cheap",
  fair: "Fair price",
  expensive: "Expensive",
  insufficient_data: "Too few copies",
  unknown: "Not in catalogue",
  ambiguous: "Which book?",
};

const nf = new Intl.NumberFormat("sr-Latn-RS", { maximumFractionDigits: 0 });
const rsd = (v) => (v === null || v === undefined ? "–" : `${nf.format(Math.round(v))} RSD`);
const num = (v) => nf.format(v);
const shortDate = (iso) => (iso ? new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" }) : "–");
const plural = (n, one, many) => `${num(n)} ${n === 1 ? one : many}`;

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") node.className = v;
    else if (k.startsWith("on")) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v === true ? "" : v);
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

function svg(tag, attrs = {}, ...children) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  for (const child of children) node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  return node;
}

async function getJson(url) {
  const response = await fetch(url, { headers: { Accept: "application/json" } });
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    const firstError = body && body.errors ? Object.values(body.errors).flat()[0] : null;
    const error = new Error(firstError || (body && (body.detail || body.title)) || `Request failed (${response.status})`);
    error.status = response.status;
    throw error;
  }
  return body;
}

const friendly = (err) => (err instanceof TypeError ? "No connection to the server. Is the stack running?" : err.message);

/* ---------------- Routing: #/check, #/catalog, #/book/123 ---------------- */

const views = { check: $("view-check"), catalog: $("view-catalog") };
const tabs = { check: $("tab-check"), catalog: $("tab-catalog") };

function route() {
  const hash = location.hash || "#/check";
  const book = hash.match(/^#\/book\/(\d+)$/);
  const name = book || hash.startsWith("#/catalog") ? "catalog" : "check";
  if (views[name].hidden) window.scrollTo(0, 0);   // a tab switch starts at the top; opening a book keeps the place
  for (const key of Object.keys(views)) {
    views[key].hidden = key !== name;
    tabs[key].setAttribute("aria-selected", String(key === name));
    tabs[key].tabIndex = key === name ? 0 : -1;
  }
  document.title = name === "catalog" ? "Catalogue — PriceRadar" : "PriceRadar — used-book price register";
  if (name === "catalog") catalog.ensureLoaded();
  if (book) bookCard.open(Number(book[1]));
  else bookCard.closeQuietly();
}

// Arrow keys move between tabs, as in the WAI-ARIA tabs pattern.
$("tab-check").parentElement.addEventListener("keydown", (event) => {
  if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
  const next = event.target === tabs.check ? tabs.catalog : tabs.check;
  next.focus();
  next.click();
});

/* ---------------- Price check ---------------- */

const form = $("lookup");
const result = $("result");
const formError = $("form-error");
const submit = $("submit");

function readRecent() {
  try { return JSON.parse(localStorage.getItem(RECENT_KEY) || "[]"); } catch { return []; }
}

function remember(entry) {
  try {
    const items = [entry, ...readRecent().filter((r) => r.title !== entry.title || r.author !== entry.author)].slice(0, 6);
    localStorage.setItem(RECENT_KEY, JSON.stringify(items));
  } catch { /* storage unavailable: the page works without it */ }
  renderRecent();
}

function renderRecent() {
  const items = readRecent();
  $("recent-box").hidden = items.length === 0;
  $("recent").replaceChildren(...items.map((r) => el("li", {},
    el("span", {}, el("button", { type: "button", class: "link-btn", onclick: () => fillAndCheck(r) }, r.title),
      r.author ? ` — ${r.author}` : ""),
    el("span", {}, `${rsd(Number(r.askingPrice))} · ${r.verdict}`))));
}

function fillAndCheck(entry) {
  form.title.value = entry.title;
  form.author.value = entry.author || "";
  form.askingPrice.value = entry.askingPrice || "";
  if (entry.askingPrice) lookup({ title: entry.title, author: entry.author || "", askingPrice: String(entry.askingPrice) });
}

function priceScale(stats, asking) {
  if (stats.min === null || stats.max === null) return null;
  const lo = Math.min(stats.min, asking);
  const hi = Math.max(stats.max, asking);
  const span = hi - lo || 1;
  const pct = (v) => `${((v - lo) / span) * 100}%`;

  const iqr = el("div", { class: "scale-iqr" });
  iqr.style.left = pct(stats.p25);
  iqr.style.width = `calc(${pct(stats.p75)} - ${pct(stats.p25)})`;
  const median = el("div", { class: "scale-median", title: `Median ${rsd(stats.median)}` });
  median.style.left = pct(stats.median);
  const you = el("div", { class: "scale-you" }, el("span", {}, `You: ${rsd(asking)}`));
  you.style.left = pct(asking);

  return el("div", { class: "scale", role: "img",
    "aria-label": `Asking price ${rsd(asking)} against a typical range of ${rsd(stats.p25)} to ${rsd(stats.p75)}, median ${rsd(stats.median)}` },
    el("div", { class: "scale-track" }, iqr, median, you),
    el("div", { class: "scale-labels" }, el("span", {}, rsd(lo)), el("span", {}, "typical range"), el("span", {}, rsd(hi))));
}

function figures(stats) {
  const cell = (label, value) => el("div", {}, el("dt", {}, label), el("dd", {}, value));
  return el("dl", { class: "figures" },
    cell("Median", rsd(stats.median)),
    cell("Typical range", `${rsd(stats.p25)} – ${rsd(stats.p75)}`),
    cell("Cheapest – dearest", `${rsd(stats.min)} – ${rsd(stats.max)}`),
    cell("Copies seen, 30 days", num(stats.sampleSize)),
    cell("On sale now", num(stats.activeListings)),
    el("div", {}, el("dt", {}, el("abbr", { title: SOLD_PROXY_NOTE }, "Gone (sold proxy)")), el("dd", {}, num(stats.disappearedInWindow))));
}

function renderLookup(data, query) {
  const match = data.match;
  const key = match.status === "ambiguous" ? "ambiguous" : data.verdict;
  const label = key === "ambiguous" && match.candidates.length === 1 ? "Is it this book?" : LABELS[key] || key;
  const isVerdict = ["cheap", "fair", "expensive"].includes(key);

  const head = el("div", { class: "result-head" },
    match.status === "matched"
      ? el("p", { class: "matched" }, "Matched", el("strong", {}, match.title),
          match.author ? el("span", {}, match.author) : null,
          match.method === "rule" ? null : el("span", { class: "method" }, ` · ${match.method} match, score ${match.score}`))
      : el("p", { class: "matched" }, "You asked about", el("strong", {}, query.title)),
    el("span", { class: `stamp ${key}${isVerdict ? "" : " small"}` }, label));

  const children = [head, el("p", { class: "explanation" }, data.explanation)];
  if (data.stats && isVerdict) children.push(priceScale(data.stats, Number(query.askingPrice)));
  if (data.stats) children.push(figures(data.stats));

  if (match.status === "matched") {
    children.push(el("div", { class: "actions" },
      el("a", { class: "link-btn", href: `#/book/${match.bookId}` }, "Open its catalogue card")));
  }

  if (match.status === "ambiguous" && match.candidates.length > 0) {
    children.push(el("ol", { class: "candidates" }, match.candidates.map((c) => el("li", {},
      catalogCard({ bookId: c.bookId, title: c.title, author: c.author, activeListings: c.activeListings, score: c.score },
        () => lookup({ title: c.title, author: c.author || "", askingPrice: query.askingPrice }))))));
  }

  result.replaceChildren(...children.filter(Boolean));
  result.hidden = false;
  $("intro").hidden = true;
  if (window.matchMedia("(max-width: 859px)").matches) result.scrollIntoView({ behavior: "smooth", block: "start" });
  remember({ title: query.title, author: query.author, askingPrice: query.askingPrice, verdict: label });
}

async function lookup(query) {
  formError.hidden = true;
  submit.disabled = true;
  submit.textContent = "Consulting the register…";
  try {
    const params = new URLSearchParams({ title: query.title, askingPrice: query.askingPrice });
    if (query.author) params.set("author", query.author);
    renderLookup(await getJson(`/api/lookup?${params}`), query);
  } catch (err) {
    formError.textContent = friendly(err);
    formError.hidden = false;
  } finally {
    submit.disabled = false;
    submit.textContent = "Check the price";
  }
}

form.addEventListener("submit", (event) => {
  event.preventDefault();
  const title = form.title.value.trim();
  const author = form.author.value.trim();
  const askingPrice = form.askingPrice.value.replace(/[^\d]/g, "");
  const fail = (message, field) => { formError.textContent = message; formError.hidden = false; field.focus(); };
  if (title.length < 2) return fail("Enter at least 2 characters of the title.", form.title);
  if (!askingPrice || Number(askingPrice) < 1) return fail("Enter the asking price in dinars.", form.askingPrice);
  lookup({ title, author, askingPrice });
});

/* ---------------- Catalogue ---------------- */

function catalogCard(book, onClick) {
  const inStock = book.activeListings > 0;
  const sources = (book.sources || "").split(",").filter(Boolean);
  return el("button", { type: "button", class: "cat-card", onclick: onClick },
    el("span", { class: "cat-no" }, el("span", {}, "No. ", el("b", {}, book.bookId)),
      el("span", {}, sources.map((s) => SOURCE_NAMES[s] || s).join(" · "))),
    el("h2", { class: "cat-title" }, book.title),
    el("p", { class: "cat-author" }, book.author || "Author unknown"),
    book.score !== undefined
      ? el("div", { class: "cat-price" }, el("small", {}, `Match score ${book.score} · tap to check this book`))
      : el("div", { class: "cat-price" },
          el("strong", {}, inStock ? rsd(book.medianPrice) : "—"),
          el("small", {}, inStock ? "median asking price" : "nothing on sale")),
    book.score === undefined && inStock && book.minPrice !== book.maxPrice
      ? el("p", { class: "cat-range" }, `${rsd(book.minPrice)} – ${rsd(book.maxPrice)}`) : null,
    el("span", { class: "cat-meta" },
      inStock ? el("span", { class: "chip stock" }, `${plural(book.activeListings, "copy", "copies")} on sale`)
              : el("span", { class: "chip out" }, "Not on sale"),
      book.disappearedListings ? el("span", { class: "chip", title: SOLD_PROXY_NOTE }, `${num(book.disappearedListings)} gone`) : null,
      book.lastSeenAtUtc ? el("span", { class: "chip" }, `seen ${shortDate(book.lastSeenAtUtc)}`) : null));
}

const catalog = (() => {
  const list = $("catalog");
  const status = $("catalog-status");
  const more = $("more");
  const filters = $("catalog-filters");
  const state = { q: "", sort: "-active", inStock: true, page: 1, total: 0, loaded: false, request: 0 };
  const PAGE_SIZE = 24;

  function totals(t) {
    const cell = (value, label) => el("div", {}, el("strong", {}, num(value)), el("span", {}, label));
    $("totals").replaceChildren(
      cell(t.books, "books in the catalogue"),
      cell(t.booksInStock, "titles on sale now"),
      cell(t.activeListings, "copies on sale"),
      cell(t.disappearedListings, "copies gone (sold proxy)"));
  }

  async function load(append) {
    const request = ++state.request;
    const params = new URLSearchParams({ sort: state.sort, inStock: String(state.inStock), page: String(state.page), pageSize: String(PAGE_SIZE) });
    if (state.q.length >= 2) params.set("q", state.q);
    if (!append) {
      list.replaceChildren(...Array.from({ length: 6 }, () => el("li", { class: "skeleton", "aria-hidden": "true" })));
      status.textContent = "Opening the drawers…";
    }
    more.disabled = true;
    try {
      const data = await getJson(`/api/catalog?${params}`);
      if (request !== state.request) return;   // a newer search superseded this one
      totals(data.totals);
      state.total = data.totalCount;
      const cards = data.items.map((b) => el("li", {}, catalogCard(b, () => { location.hash = `#/book/${b.bookId}`; })));
      if (append) list.append(...cards); else list.replaceChildren(...cards);
      const shown = list.children.length;
      if (shown === 0) {
        list.replaceChildren(el("li", { class: "empty" }, state.q ? `No book matches “${state.q}”.` : "The catalogue is empty. Run an ingestion first."));
      }
      status.textContent = shown ? `Showing ${num(shown)} of ${plural(state.total, "book", "books")}${state.q ? ` matching “${state.q}”` : ""}.` : "";
      more.hidden = shown >= state.total;
    } catch (err) {
      if (request !== state.request) return;
      list.replaceChildren();
      status.textContent = friendly(err);
    } finally {
      more.disabled = false;
    }
  }

  function reset() { state.page = 1; load(false); }

  let debounce;
  filters.q.addEventListener("input", () => {
    clearTimeout(debounce);
    debounce = setTimeout(() => {
      const q = filters.q.value.trim();
      if (q === state.q || (q.length === 1)) return;
      state.q = q;
      reset();
    }, 280);
  });
  filters.addEventListener("submit", (event) => { event.preventDefault(); clearTimeout(debounce); state.q = filters.q.value.trim(); reset(); });
  filters.sort.addEventListener("change", () => { state.sort = filters.sort.value; reset(); });
  filters.inStock.addEventListener("change", () => { state.inStock = filters.inStock.checked; reset(); });
  more.addEventListener("click", () => { state.page += 1; load(true); });

  return {
    ensureLoaded() { if (!state.loaded) { state.loaded = true; load(false); } },
  };
})();

/* ---------------- Book card (dialog) ---------------- */

function historyChart(points) {
  if (points.length < 2) return el("p", { class: "chart-note" }, "Not enough history yet for a chart.");
  const W = 640, H = 200, L = 52, R = 12, T = 12, B = 28;
  const times = points.map((p) => new Date(p.period).getTime());
  const lo = Math.min(...points.map((p) => p.minPrice));
  const hi = Math.max(...points.map((p) => p.maxPrice));
  const t0 = times[0], t1 = times[times.length - 1];
  const x = (t) => L + ((t - t0) / (t1 - t0 || 1)) * (W - L - R);
  const y = (v) => T + (1 - (v - lo) / (hi - lo || 1)) * (H - T - B);

  const band = points.map((p, i) => `${x(times[i])},${y(p.maxPrice)}`).join(" ") + " " +
    points.map((p, i) => `${x(times[i])},${y(p.minPrice)}`).reverse().join(" ");
  const line = points.map((p, i) => `${x(times[i])},${y(p.medianPrice)}`).join(" ");

  const ticks = [lo, (lo + hi) / 2, hi];
  const months = [];
  for (let i = 0; i < points.length; i++) {
    const d = new Date(times[i]);
    if (i === 0 || d.getMonth() !== new Date(times[i - 1]).getMonth()) months.push([times[i], d.toLocaleDateString("en-GB", { month: "short" })]);
  }

  const last = points[points.length - 1];
  return svg("svg", { class: "chart", viewBox: `0 0 ${W} ${H}`, role: "img",
    "aria-label": `Weekly asking prices from ${shortDate(points[0].period)} to ${shortDate(last.period)}: median now ${rsd(last.medianPrice)}, lowest ${rsd(lo)}, highest ${rsd(hi)}` },
    ...ticks.map((v) => svg("line", { class: "grid", x1: L, x2: W - R, y1: y(v), y2: y(v) })),
    ...ticks.map((v) => svg("text", { x: L - 6, y: y(v) + 4, "text-anchor": "end" }, nf.format(Math.round(v)))),
    ...months.map(([t, label]) => svg("text", { x: x(t), y: H - 8, "text-anchor": "middle" }, label)),
    svg("line", { class: "axis", x1: L, x2: W - R, y1: H - B, y2: H - B }),
    svg("polygon", { class: "band", points: band }),
    svg("polyline", { class: "line", points: line }),
    svg("circle", { class: "dot", cx: x(t1), cy: y(last.medianPrice), r: 4 }));
}

function listingsTable(page) {
  if (page.items.length === 0) return el("p", { class: "chart-note" }, "No copies on sale right now.");
  return el("table", { class: "listings" },
    el("thead", {}, el("tr", {}, el("th", {}, "Listing"), el("th", {}, "Source"), el("th", { class: "num" }, "Price"))),
    el("tbody", {}, page.items.map((l) => el("tr", {},
      el("td", {}, l.title, l.condition ? el("div", { class: "src" }, l.condition) : null),
      el("td", { class: "src" }, SOURCE_NAMES[l.source] || l.source, el("div", {}, `since ${shortDate(l.firstSeenAtUtc)}`)),
      el("td", { class: "num" }, rsd(l.priceRsd))))));
}

const bookCard = (() => {
  const dialog = $("book");
  const body = $("book-body");
  let current = null;

  dialog.addEventListener("close", () => {
    current = null;
    if (/^#\/book\//.test(location.hash)) history.replaceState(null, "", "#/catalog");
  });
  dialog.addEventListener("click", (event) => { if (event.target === dialog) dialog.close(); });   // click on the backdrop

  async function open(id) {
    if (current === id && dialog.open) return;
    current = id;
    body.replaceChildren(el("div", { class: "skeleton", "aria-label": "Loading" }));
    if (!dialog.open) dialog.showModal();
    const to = new Date();
    const from = new Date(to.getTime() - 180 * 86400000);
    const day = (d) => d.toISOString().slice(0, 10);
    try {
      const [book, stats, listings, history] = await Promise.all([
        getJson(`/api/books/${id}`),
        getJson(`/api/books/${id}/stats?windowDays=30`),
        getJson(`/api/books/${id}/listings?status=active&sort=price&pageSize=8`),
        getJson(`/api/books/${id}/price-history?bucket=week&from=${day(from)}&to=${day(to)}`),
      ]);
      if (current !== id) return;
      document.title = `${book.title} — PriceRadar`;
      body.replaceChildren(
        el("div", { class: "dialog-head" },
          el("div", {}, el("span", { class: "cat-no" }, el("span", {}, "No. ", el("b", {}, book.bookId))),
            el("h2", { id: "book-title" }, book.title), el("p", {}, book.author || "Author unknown")),
          el("button", { type: "button", class: "close", "aria-label": "Close", onclick: () => dialog.close() }, "×")),
        el("h3", { class: "section-title" }, "Last 30 days"),
        stats.sampleSize ? figures(stats) : el("p", { class: "chart-note" }, "No copies seen in the last 30 days."),
        el("h3", { class: "section-title" }, "Asking prices, last 6 months"),
        historyChart(history),
        el("p", { class: "chart-note" }, "Line: weekly median. Band: cheapest to dearest copy that week."),
        el("h3", { class: "section-title" }, `Cheapest copies on sale (${num(listings.totalCount)})`),
        listingsTable(listings),
        el("div", { class: "actions" },
          el("button", { type: "button", class: "btn btn-primary", onclick: () => checkThis(book) }, "Check a price for this book")));
    } catch (err) {
      if (current !== id) return;
      body.replaceChildren(
        el("div", { class: "dialog-head" }, el("h2", { id: "book-title" }, err.status === 404 ? "No such book" : "Could not open the card"),
          el("button", { type: "button", class: "close", "aria-label": "Close", onclick: () => dialog.close() }, "×")),
        el("p", {}, friendly(err)));
    }
  }

  function checkThis(book) {
    form.title.value = book.title;
    form.author.value = book.author || "";
    form.askingPrice.value = "";
    dialog.close();
    location.hash = "#/check";
    form.askingPrice.focus();
  }

  return {
    open,
    closeQuietly() { if (dialog.open) dialog.close(); },
  };
})();

window.addEventListener("hashchange", route);
renderRecent();
route();
