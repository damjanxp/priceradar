// PriceRadar lookup page (FR-16): calls GET /api/lookup and shows the verdict. No framework, no build step.
"use strict";

const form = document.getElementById("lookup");
const result = document.getElementById("result");
const formError = document.getElementById("form-error");
const submit = document.getElementById("submit");
const recentBox = document.getElementById("recent-box");
const recentList = document.getElementById("recent");
const RECENT_KEY = "priceradar.recent";
const SOLD_PROXY_NOTE = "Listings that stopped appearing in two scrapes in a row. Probably sold, but it could also be withdrawn: marketplaces do not publish sales.";

const rsd = (v) => (v === null || v === undefined ? "–" : `${Math.round(v).toLocaleString("sr-RS")} RSD`);

const LABELS = {
  cheap: "Cheap",
  fair: "Fair price",
  expensive: "Expensive",
  insufficient_data: "Not enough data",
  unknown: "Book not found",
  ambiguous: "Which book?",
};

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === "class") node.className = v;
    else node.setAttribute(k, v);
  }
  for (const child of children) node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  return node;
}

function readRecent() {
  try { return JSON.parse(localStorage.getItem(RECENT_KEY) || "[]"); } catch { return []; }
}

function remember(entry) {
  try {
    const items = [entry, ...readRecent().filter((r) => r.title !== entry.title || r.author !== entry.author)].slice(0, 5);
    localStorage.setItem(RECENT_KEY, JSON.stringify(items));
  } catch { /* storage unavailable: the page works without it */ }
  renderRecent();
}

function renderRecent() {
  const items = readRecent();
  recentBox.hidden = items.length === 0;
  recentList.replaceChildren(...items.map((r) => el("li", {}, `${r.title}${r.author ? " — " + r.author : ""}: ${r.verdict}`)));
}

function renderStats(stats) {
  const cell = (label, value) => el("div", {}, el("dt", {}, label), el("dd", {}, value));
  return el("dl", { class: "stats" },
    cell("Median", rsd(stats.median)),
    cell("Typical range (P25–P75)", `${rsd(stats.p25)} – ${rsd(stats.p75)}`),
    cell("Cheapest / dearest", `${rsd(stats.min)} – ${rsd(stats.max)}`),
    cell("Listings seen (30 days)", stats.sampleSize),
    cell("On sale now", stats.activeListings),
    el("div", {}, el("dt", {}, el("abbr", { title: SOLD_PROXY_NOTE }, "Disappeared (sold proxy)")), el("dd", {}, stats.disappearedInWindow)));
}

function render(data, query) {
  const match = data.match;
  const key = match.status === "ambiguous" ? "ambiguous" : data.verdict;
  const label = key === "ambiguous" && match.candidates.length === 1 ? "Is it this book?" : LABELS[key] || key;
  const children = [el("span", { class: `verdict ${key}` }, label), el("p", { class: "explanation" }, data.explanation)];

  if (match.status === "matched") {
    children.push(el("p", { class: "book" }, "Matched ", el("strong", {}, `${match.title}${match.author ? " — " + match.author : ""}`),
      match.method === "rule" ? "" : ` (${match.method}, score ${match.score})`));
  }

  if (data.stats) children.push(renderStats(data.stats));

  if (match.status === "ambiguous" && match.candidates.length > 0) {
    children.push(el("ul", { class: "candidates" }, ...match.candidates.map((c) => {
      const button = el("button", { type: "button", class: "candidate" }, `${c.title}`,
        el("small", {}, `${c.author || "unknown author"} · ${c.activeListings} on sale · score ${c.score}`));
      button.addEventListener("click", () => lookup({ title: c.title, author: c.author || "", askingPrice: query.askingPrice }));
      return el("li", {}, button);
    })));
  }

  result.replaceChildren(...children);
  result.hidden = false;
  result.scrollIntoView({ behavior: "smooth", block: "start" });
  if (data.verdict || match.status === "ambiguous") {
    remember({ title: query.title, author: query.author, verdict: label });
  }
}

async function lookup(query) {
  formError.hidden = true;
  submit.disabled = true;
  submit.textContent = "Checking…";
  try {
    const params = new URLSearchParams({ title: query.title, askingPrice: query.askingPrice });
    if (query.author) params.set("author", query.author);
    const response = await fetch(`/api/lookup?${params}`, { headers: { Accept: "application/json" } });
    const body = await response.json().catch(() => null);
    if (!response.ok) {
      const firstError = body && body.errors ? Object.values(body.errors).flat()[0] : null;
      throw new Error(firstError || (body && body.title) || `Request failed (${response.status})`);
    }
    render(body, query);
  } catch (err) {
    formError.textContent = err.message === "Failed to fetch" ? "No connection to the server." : err.message;
    formError.hidden = false;
  } finally {
    submit.disabled = false;
    submit.textContent = "Check price";
  }
}

form.addEventListener("submit", (event) => {
  event.preventDefault();
  const title = form.title.value.trim();
  const author = form.author.value.trim();
  const askingPrice = form.askingPrice.value.replace(/[^\d]/g, "");
  if (title.length < 2) { formError.textContent = "Enter at least 2 characters of the title."; formError.hidden = false; return; }
  if (!askingPrice || Number(askingPrice) < 1) { formError.textContent = "Enter the asking price in dinars."; formError.hidden = false; return; }
  lookup({ title, author, askingPrice });
});

renderRecent();
