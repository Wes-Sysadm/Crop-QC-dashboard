(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    root.CropQcSiteNavigation = api;
    if (root.document) {
        const start = () => api.initialize(root.document, root);
        if (root.document.readyState === "loading") root.document.addEventListener("DOMContentLoaded", start, { once: true });
        else start();
    }
})(typeof globalThis === "undefined" ? this : globalThis, function () {
    "use strict";

    function initialize(document, viewport) {
        const menuButton = document.querySelector("[data-mobile-menu-button]");
        const menuLabel = menuButton?.querySelector("[data-mobile-menu-label]");
        const navigation = document.querySelector("[data-primary-navigation]");
        const header = document.querySelector("[data-site-header]");
        if (!navigation || navigation.dataset.navigationInitialized === "true") return null;

        navigation.dataset.navigationInitialized = "true";
        const categories = Array.from(navigation.querySelectorAll("[data-nav-category]"));
        const summaries = new Map(categories.map(category => [category, category.querySelector("summary")]));
        const compactQuery = viewport.matchMedia("(max-width: 1180px)");
        let lastOpenedSummary = null;

        function closeCategories(except) {
            for (const category of categories) {
                if (category === except) continue;
                category.open = false;
                summaries.get(category)?.setAttribute("aria-expanded", "false");
            }
        }

        function setMenu(open) {
            if (!menuButton) return;
            navigation.dataset.mobileOpen = open ? "true" : "false";
            menuButton.setAttribute("aria-expanded", open ? "true" : "false");
            if (menuLabel) menuLabel.textContent = open ? "Close" : "Menu";
            if (!open) closeCategories();
        }

        menuButton?.addEventListener("click", () => {
            setMenu(menuButton.getAttribute("aria-expanded") !== "true");
        });

        for (const category of categories) {
            const summary = summaries.get(category);
            summary?.setAttribute("aria-expanded", category.open ? "true" : "false");
            summary?.addEventListener("click", () => {
                lastOpenedSummary = summary;
            });
            category.addEventListener("toggle", () => {
                summary?.setAttribute("aria-expanded", category.open ? "true" : "false");
                if (!category.open) return;
                lastOpenedSummary = summary;
                closeCategories(category);
            });
        }

        navigation.addEventListener("click", event => {
            if (compactQuery.matches && event.target.closest?.("a")) setMenu(false);
        });

        document.addEventListener("click", event => {
            if (header?.contains(event.target)) return;
            closeCategories();
            if (compactQuery.matches) setMenu(false);
        });

        document.addEventListener("keydown", event => {
            if (event.key !== "Escape") return;
            const hadOpenCategory = categories.some(category => category.open);
            closeCategories();
            if (compactQuery.matches) setMenu(false);
            if (hadOpenCategory) (compactQuery.matches ? menuButton : lastOpenedSummary)?.focus();
        });

        compactQuery.addEventListener?.("change", event => {
            if (!event.matches) setMenu(false);
        });

        return { setMenu, closeCategories, categories };
    }

    return { initialize };
});
