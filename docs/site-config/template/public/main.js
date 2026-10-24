const compactNavigation = window.matchMedia("(max-width: 1199.98px)");

function normaliseSearchPunctuation() {
  document.addEventListener("input", event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || input.id !== "search-query" || !input.value.includes(".")) return;

    const caret = input.selectionStart;
    const value = input.value;
    const normalised = value.replace(/\./g, " ");
    input.value = normalised;
    if (caret !== null) {
      const normalisedCaret = value.slice(0, caret).replace(/\./g, " ").length;
      input.setSelectionRange(normalisedCaret, normalisedCaret);
    }
  }, true);
}

function findSiteRoot() {
  const logo = document.querySelector("#autocollapse .navbar-brand");
  if (!logo) return null;
  return new URL(logo.getAttribute("href") || "index.html", window.location.href);
}

function normalisePath(value, siteRoot) {
  const path = decodeURIComponent(new URL(value, siteRoot).pathname);
  if (path === "/") return "/index.html";
  return path.endsWith("/") ? `${path}index.html` : path;
}

function containsCurrentPage(item, currentPath, siteRoot) {
  if (item.href && normalisePath(item.href, siteRoot) === currentPath) return true;
  return Array.isArray(item.items) && item.items.some(child => containsCurrentPage(child, currentPath, siteRoot));
}

function createLink(item, siteRoot, className, currentPath) {
  const link = document.createElement("a");
  link.className = className;
  link.textContent = item.name || "Documentation page";
  if (item.href) {
    link.href = new URL(item.href, siteRoot).href;
    if (normalisePath(item.href, siteRoot) === currentPath) link.setAttribute("aria-current", "page");
  }
  return link;
}

function setSectionExpanded(section, expanded) {
  const toggle = section.querySelector(".lc-menu-section-toggle");
  if (!toggle) return;
  section.classList.toggle("is-expanded", expanded);
  toggle.setAttribute("aria-expanded", String(expanded));
  const action = expanded ? "Hide" : "Show";
  toggle.setAttribute("aria-label", `${action} pages in ${toggle.dataset.sectionName}`);
}

function updateSectionDisclosure(section, isCompact) {
  setSectionExpanded(section, !isCompact);
}

function closeOpenCategories(nav, returnFocus = false) {
  for (const category of nav.querySelectorAll(".lc-menu-category.is-open")) {
    category.classList.remove("is-open");
    const toggle = category.querySelector(":scope > .lc-menu-category-toggle");
    const panel = category.querySelector(":scope > .lc-menu-panel");
    toggle?.setAttribute("aria-expanded", "false");
    if (panel) panel.hidden = true;
    if (returnFocus) toggle?.focus();
  }
}

function buildNavigation(navbar, toc, siteRoot) {
  const categories = Array.isArray(toc.items) ? toc.items : [];
  const currentPath = normalisePath(window.location.href, siteRoot);
  const nav = document.createElement("nav");
  nav.className = "lc-primary-nav";
  nav.setAttribute("aria-label", "Documentation sections");
  const list = document.createElement("ul");
  list.className = "lc-category-list";

  categories.forEach((category, categoryIndex) => {
    if (!Array.isArray(category.items) || category.items.length === 0) return;
    const categoryItem = document.createElement("li");
    categoryItem.className = "lc-menu-category";
    const categoryToggle = document.createElement("button");
    categoryToggle.type = "button";
    categoryToggle.className = "lc-menu-category-toggle";
    categoryToggle.setAttribute("aria-label", category.name);
    categoryToggle.setAttribute("aria-expanded", "false");
    categoryToggle.dataset.active = String(containsCurrentPage(category, currentPath, siteRoot));

    const categoryLabel = document.createElement("span");
    categoryLabel.textContent = category.name;
    const categoryChevron = document.createElement("span");
    categoryChevron.className = "lc-menu-chevron";
    categoryChevron.setAttribute("aria-hidden", "true");
    categoryChevron.textContent = "▾";
    categoryToggle.append(categoryLabel, categoryChevron);

    const panel = document.createElement("div");
    panel.className = "lc-menu-panel";
    panel.id = `lc-menu-panel-${categoryIndex}`;
    panel.hidden = true;
    panel.setAttribute("role", "group");
    panel.setAttribute("aria-label", `${category.name} pages`);
    categoryToggle.setAttribute("aria-controls", panel.id);

    const grid = document.createElement("div");
    grid.className = "lc-menu-grid";
    category.items.forEach((sectionItem, sectionIndex) => {
      const section = document.createElement("section");
      section.className = "lc-menu-section is-expanded";
      section.classList.toggle("is-current", containsCurrentPage(sectionItem, currentPath, siteRoot));
      const heading = document.createElement("div");
      heading.className = "lc-menu-section-heading";
      if (sectionItem.href) {
        heading.append(createLink(sectionItem, siteRoot, "lc-menu-section-link", currentPath));
      } else {
        const label = document.createElement("span");
        label.className = "lc-menu-section-link";
        label.textContent = sectionItem.name || "Section";
        heading.append(label);
      }

      const pages = Array.isArray(sectionItem.items) ? sectionItem.items : [];
      if (pages.length > 0) {
        const sectionToggle = document.createElement("button");
        sectionToggle.type = "button";
        sectionToggle.className = "lc-menu-section-toggle";
        sectionToggle.dataset.sectionName = sectionItem.name || "this section";
        sectionToggle.setAttribute("aria-expanded", "true");
        sectionToggle.setAttribute("aria-controls", `lc-menu-pages-${categoryIndex}-${sectionIndex}`);
        sectionToggle.textContent = "▾";
        heading.append(sectionToggle);

        const pageList = document.createElement("ul");
        pageList.className = "lc-menu-pages";
        pageList.id = `lc-menu-pages-${categoryIndex}-${sectionIndex}`;
        for (const pageItem of pages) {
          const pageRow = document.createElement("li");
          pageRow.append(createLink(pageItem, siteRoot, "lc-menu-page-link", currentPath));
          pageList.append(pageRow);
        }

        sectionToggle.addEventListener("click", () => {
          setSectionExpanded(section, !section.classList.contains("is-expanded"));
        });
        section.append(heading, pageList);
      } else {
        section.append(heading);
      }
      grid.append(section);
    });

    panel.append(grid);
    categoryItem.append(categoryToggle, panel);
    categoryToggle.addEventListener("click", () => {
      const shouldOpen = !categoryItem.classList.contains("is-open");
      closeOpenCategories(nav);
      if (shouldOpen) {
        categoryItem.classList.add("is-open");
        categoryToggle.setAttribute("aria-expanded", "true");
        panel.hidden = false;
      }
    });
    panel.addEventListener("click", event => {
      if (event.target instanceof HTMLAnchorElement) closeOpenCategories(nav);
    });
    list.append(categoryItem);
  });

  nav.append(list);
  navbar.insertBefore(nav, navbar.firstChild);
  for (const section of nav.querySelectorAll(".lc-menu-section")) {
    updateSectionDisclosure(section, compactNavigation.matches);
  }

  compactNavigation.addEventListener("change", event => {
    for (const section of nav.querySelectorAll(".lc-menu-section")) {
      updateSectionDisclosure(section, event.matches);
    }
  });
  document.addEventListener("pointerdown", event => {
    if (event.target instanceof Node && !nav.contains(event.target)) closeOpenCategories(nav);
  });
  document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    const expandedSection = document.activeElement?.closest(".lc-menu-section.is-expanded");
    const sectionToggle = expandedSection?.querySelector(".lc-menu-section-toggle");
    if (sectionToggle && compactNavigation.matches) {
      setSectionExpanded(expandedSection, false);
      sectionToggle.focus();
      event.preventDefault();
      return;
    }

    const openCategory = nav.querySelector(".lc-menu-category.is-open");
    if (openCategory) {
      closeOpenCategories(nav, true);
      event.preventDefault();
    }
  });
  nav.addEventListener("focusout", () => {
    window.setTimeout(() => {
      if (!nav.contains(document.activeElement)) closeOpenCategories(nav);
    }, 0);
  });

  const nativeLists = navbar.querySelectorAll("ul.navbar-nav");
  for (const nativeList of nativeLists) {
    nativeList.hidden = true;
    nativeList.setAttribute("aria-hidden", "true");
  }

  const nativeObserver = new MutationObserver(() => {
    for (const nativeList of navbar.querySelectorAll("ul.navbar-nav")) {
      nativeList.hidden = true;
      nativeList.setAttribute("aria-hidden", "true");
    }
  });
  nativeObserver.observe(navbar, { childList: true, subtree: true });
  navbar.classList.add("lc-navigation-ready");
}

async function startNavigation() {
  const navbar = document.getElementById("navbar");
  const siteRoot = findSiteRoot();
  if (!navbar || !siteRoot) return;

  try {
    const response = await fetch(new URL("toc.json", siteRoot));
    if (!response.ok) return;
    const toc = await response.json();
    if (Array.isArray(toc.items) && toc.items.length > 0) buildNavigation(navbar, toc, siteRoot);
  } catch {
    // Keep DocFX's native navigation available when the generated TOC cannot be loaded.
  }
}

export default {
  iconLinks: [
    {
      icon: "github",
      href: "https://github.com/jordansrowles/LeanCorpus",
      title: "GitHub"
    }
  ],
  start: () => {
    normaliseSearchPunctuation();
    void startNavigation();
  }
};
