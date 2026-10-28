import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import generateBytefield from "bytefield-svg";

const diagramsDirectory = dirname(fileURLToPath(import.meta.url));
const assetsDirectory = process.env.LEANCORPUS_DOCS_DIAGRAM_OUTPUT
  ? resolve(process.env.LEANCORPUS_DOCS_DIAGRAM_OUTPUT)
  : resolve(diagramsDirectory, "../../../artifacts/docs/content/assets/diagrams");

await mkdir(assetsDirectory, { recursive: true });

const bytefieldSource = await readFile(
  resolve(diagramsDirectory, "codeckit-envelope.edn"),
  "utf8");
const bytefieldSvg = generateBytefield(bytefieldSource);
await writeFile(
  resolve(assetsDirectory, "codeckit-envelope.svg"),
  bytefieldSvg,
  "utf8");
