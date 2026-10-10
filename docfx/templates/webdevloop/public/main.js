// Gives the mermaid diagrams the WebDevLoop colours (the app's indigo accent), in light and dark.
const dark = document.documentElement.getAttribute("data-bs-theme") === "dark";

export default {
  mermaid: {
    theme: "base",
    themeVariables: dark
      ? {
          background: "#171a22",
          primaryColor: "#2b2f5c",
          primaryTextColor: "#e5e7eb",
          primaryBorderColor: "#818cf8",
          secondaryColor: "#1e222c",
          tertiaryColor: "#171a22",
          lineColor: "#9ca3af",
          textColor: "#e5e7eb",
          noteBkgColor: "#1e222c",
          noteTextColor: "#e5e7eb",
          fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
        }
      : {
          background: "#ffffff",
          primaryColor: "#eef2ff",
          primaryTextColor: "#111827",
          primaryBorderColor: "#4f46e5",
          secondaryColor: "#f3f4f6",
          tertiaryColor: "#ffffff",
          lineColor: "#6b7280",
          textColor: "#111827",
          noteBkgColor: "#fef3c7",
          noteTextColor: "#92400e",
          fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
        },
  },
};

// Wide diagrams would be shrunk until the text is unreadable: keep at least 60% of the natural size and scroll instead.
const keepReadable = (svg) => {
  const width = svg.viewBox?.baseVal?.width;
  if (width) {
    svg.style.minWidth = `${Math.round(width * 0.6)}px`;
  }
};

// Mermaid sets the viewBox after it inserted the svg, so listen for attribute changes too.
new MutationObserver((records) => {
  for (const record of records) {
    const nodes = record.type === "attributes" ? [record.target] : [...record.addedNodes];
    for (const node of nodes) {
      if (!(node instanceof Element)) continue;
      const svgs = node.matches("svg") ? [node] : [...node.querySelectorAll("svg")];
      svgs.filter((svg) => svg.closest(".mermaid")).forEach(keepReadable);
    }
  }
}).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["viewBox"] });

// Click a diagram to see it in full size.
document.addEventListener("click", (event) => {
  const svg = event.target instanceof Element ? event.target.closest(".mermaid svg") : null;
  if (!svg || svg.closest(".diagram-overlay")) return;
  const overlay = document.createElement("div");
  overlay.className = "diagram-overlay";
  overlay.title = "Click to close";
  const copy = svg.cloneNode(true);
  copy.style.minWidth = "";
  copy.style.maxWidth = "none";
  copy.style.width = `${Math.max(window.innerWidth * 0.9, 600)}px`;
  overlay.append(copy);
  overlay.addEventListener("click", () => overlay.remove());
  document.addEventListener("keydown", function close(e) {
    if (e.key === "Escape") { overlay.remove(); document.removeEventListener("keydown", close); }
  });
  document.body.append(overlay);
});
