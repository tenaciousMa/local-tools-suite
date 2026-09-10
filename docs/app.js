function initializePageTransitions() {
  document.body.classList.add("page-enter");

  document.addEventListener("click", (event) => {
    const link = event.target.closest("a");
    if (!link) return;
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }

    const url = new URL(link.href, window.location.href);
    if (url.origin !== window.location.origin || url.hash) return;
    if (link.target && link.target !== "_self") return;

    event.preventDefault();
    document.body.classList.add("page-exit");
    window.setTimeout(() => {
      window.location.href = url.href;
    }, 150);
  });
}

function initializeReveal() {
  const elements = [...document.querySelectorAll(".reveal")];
  if (!elements.length) return;

  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        }
      }
    },
    { threshold: 0.12 }
  );

  elements.forEach((element, index) => {
    element.style.transitionDelay = `${Math.min(index * 35, 180)}ms`;
    observer.observe(element);
  });
}

function initializeDownloadTracking() {
  for (const link of document.querySelectorAll('a[href*="/releases/"]')) {
    link.addEventListener("click", () => {
      link.classList.add("is-downloading");
    });
  }
}

initializePageTransitions();
initializeReveal();
initializeDownloadTracking();
