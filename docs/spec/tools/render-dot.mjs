// Renders every diagrams/*.dot file to diagrams/*.svg using Graphviz compiled to WASM
// (no native Graphviz install needed). Usage: node render-dot.mjs
import { Graphviz } from "@hpcc-js/wasm-graphviz";
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, "..", "diagrams");
const graphviz = await Graphviz.load();

for (const file of readdirSync(dir).filter((f) => f.endsWith(".dot"))) {
  const svg = graphviz.dot(readFileSync(join(dir, file), "utf8"), "svg");
  writeFileSync(join(dir, file.replace(/\.dot$/, ".svg")), svg);
  console.log(`rendered ${file}`);
}
