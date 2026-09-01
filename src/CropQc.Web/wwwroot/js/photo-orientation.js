(() => {
    function withRevision(value, revision) {
        if (!value) return value;
        const url = new URL(value, window.location.href);
        url.searchParams.set("v", String(revision));
        return url.toString();
    }

    function updatePresentation(card, revision) {
        card.dataset.presentationRevision = String(revision);
        for (const field of card.querySelectorAll("[data-photo-presentation-revision]")) {
            field.value = String(revision);
        }
        for (const image of card.querySelectorAll("img.photo-thumbnail")) {
            image.src = withRevision(image.src, revision);
        }
        for (const link of card.querySelectorAll("a[data-photo-presentation-link]")) {
            link.href = withRevision(link.href, revision);
        }
    }

    function initialize(root) {
        const status = root.querySelector("[data-photo-orientation-status]");
        const showStatus = (message, isError = false) => {
            if (!status) return;
            status.textContent = message;
            status.classList.toggle("field-validation", isError);
        };

        for (const form of root.querySelectorAll("[data-photo-rotate-form]")) {
            if (form.dataset.photoOrientationBound === "true") continue;
            form.dataset.photoOrientationBound = "true";
            form.addEventListener("submit", async event => {
                event.preventDefault();
                const submitter = event.submitter;
                const direction = submitter?.value;
                const card = form.closest("[data-photo-card]");
                if (!card || !direction || card.classList.contains("photo-card-saving")) return;

                const body = new FormData(form);
                body.set("Direction", direction);
                card.classList.add("photo-card-saving");
                for (const button of card.querySelectorAll("[data-photo-rotate-form] button")) button.disabled = true;
                showStatus(direction === "right" ? "Rotating photo right..." : "Rotating photo left...");
                try {
                    const response = await fetch(form.action, {
                        method: "POST",
                        body,
                        credentials: "same-origin",
                        headers: { Accept: "application/json" }
                    });
                    const payload = await response.json().catch(() => ({}));
                    if (Number.isInteger(payload.presentationRevision)) {
                        updatePresentation(card, payload.presentationRevision);
                    }
                    if (!response.ok || payload.succeeded === false) {
                        throw new Error(payload.error || "The photo could not be rotated.");
                    }
                    showStatus(direction === "right" ? "Photo rotated right." : "Photo rotated left.");
                    card.classList.add("photo-card-saved");
                    window.setTimeout(() => card.classList.remove("photo-card-saved"), 1600);
                } catch (error) {
                    showStatus(error instanceof Error ? error.message : "The photo could not be rotated.", true);
                } finally {
                    card.classList.remove("photo-card-saving");
                    for (const button of card.querySelectorAll("[data-photo-rotate-form] button")) button.disabled = false;
                }
            });
        }
    }

    for (const root of document.querySelectorAll("[data-photo-reclassification]")) initialize(root);
})();
