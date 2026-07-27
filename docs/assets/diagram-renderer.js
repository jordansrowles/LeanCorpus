const rendererScript = document.currentScript;

document.addEventListener("DOMContentLoaded", async () => {
  const blocks = Array.from(
    document.querySelectorAll("pre code.lang-mermaid-latest"));

  if (blocks.length === 0 || !rendererScript) {
    return;
  }

  if (!globalThis.mermaid) {
    const mermaidScript = document.createElement("script");
    mermaidScript.src = new URL(
      "diagrams/mermaid-11.16.0.min.js",
      rendererScript.src);

    await new Promise((resolve, reject) => {
      mermaidScript.addEventListener("load", resolve, { once: true });
      mermaidScript.addEventListener("error", reject, { once: true });
      document.head.appendChild(mermaidScript);
    });
  }

  globalThis.mermaid.initialize({
    startOnLoad: false,
    securityLevel: "strict",
    theme: document.documentElement.dataset.bsTheme === "dark"
      ? "dark"
      : "default",
  });

  const nodes = blocks.map((block) => {
    const diagram = document.createElement("div");
    diagram.className = "mermaid";
    diagram.textContent = block.textContent;
    block.closest("pre").replaceWith(diagram);
    return diagram;
  });

  await globalThis.mermaid.run({ nodes });
});
