import { copyFile, mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import generateBytefield from "bytefield-svg";

const diagramsDirectory = dirname(fileURLToPath(import.meta.url));
const assetsDirectory = resolve(diagramsDirectory, "../assets/diagrams");

await mkdir(assetsDirectory, { recursive: true });

const bytefieldSource = await readFile(
  resolve(diagramsDirectory, "codeckit-envelope.edn"),
  "utf8");
const bytefieldSvg = generateBytefield(bytefieldSource);
await writeFile(
  resolve(assetsDirectory, "codeckit-envelope.svg"),
  bytefieldSvg,
  "utf8");

await copyFile(
  resolve(diagramsDirectory, "node_modules/mermaid/dist/mermaid.min.js"),
  resolve(assetsDirectory, "mermaid-11.16.0.min.js"));
