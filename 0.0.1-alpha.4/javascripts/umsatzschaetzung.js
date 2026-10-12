(() => {
  const key = "us-os";
  const names = { windows: "Windows", macos: "Mac" };
  const store = value => { try { localStorage.setItem(key, value); } catch {} };
  const stored = () => { try { return localStorage.getItem(key); } catch { return null; } };
  const guess = () => /mac|iphone|ipad/i.test(navigator.userAgentData?.platform || navigator.platform) ? "macos" : "windows";

  const os = new URLSearchParams(location.search).get("os")
    || location.pathname.match(/install-(windows|macos)/)?.[1]
    || stored()
    || guess();

  const apply = value => {
    if (!names[value]) return;
    document.documentElement.dataset.usOs = value;
    store(value);
  };
  apply(os);

  for (const card of document.querySelectorAll(".admonition.windows, .admonition.macos")) {
    const own = card.classList.contains("windows") ? "windows" : "macos";
    const other = own === "windows" ? "macos" : "windows";
    const link = document.createElement("a");
    link.className = "us-os-switch";
    link.href = "#";
    link.textContent = other === "macos" ? "Sie arbeiten am Mac?" : "Sie arbeiten unter Windows?";
    link.addEventListener("click", event => { event.preventDefault(); apply(other); });
    card.querySelector(".admonition-title")?.append(link);
  }

  // Screenshots are taken at two pixels per point; they show at their point size, and only a
  // thin strip may span the page.
  for (const img of document.querySelectorAll('.md-typeset img[src*="img/"]')) {
    const fit = () => {
      img.style.width = img.naturalWidth / 2 + "px";
      img.classList.toggle("us-wide", img.naturalWidth >= 4 * img.naturalHeight);
    };
    if (img.complete && img.naturalWidth) fit();
    else img.addEventListener("load", fit, { once: true });
  }

  if (location.pathname.includes("/guide/")) {
    for (const code of document.querySelectorAll(".md-typeset td > code:only-child, .md-typeset code.copy")) {
      if (!code.classList.contains("copy") && code.parentElement.textContent.trim() !== code.textContent) continue;
      const button = document.createElement("button");
      button.className = "us-copy";
      button.title = "In Zwischenablage kopieren";
      button.dataset.clipboardText = code.textContent;
      code.after(button);
    }
  }
})();
