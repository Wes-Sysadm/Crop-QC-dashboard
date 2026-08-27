(() => {
    function initialize(root) {
        const status = root.querySelector("[data-photo-reclassification-status]");
        let draggedCard = null;

        function showStatus(message, isError = false) {
            if (!status) return;
            status.textContent = message;
            status.classList.toggle("field-validation", isError);
        }

        function updateEmptyStates() {
            for (const zone of root.querySelectorAll("[data-photo-drop-target]")) {
                const empty = zone.querySelector(".photo-empty-message");
                if (empty) empty.hidden = zone.querySelectorAll("[data-photo-card]").length > 0;
            }
        }

        function rebuildMoveOptions(card) {
            const currentType = card.dataset.photoType;
            const select = card.querySelector("[data-photo-move-target]");
            if (!select) return;
            const options = Array.from(root.querySelectorAll("[data-photo-drop-target]"))
                .filter(zone => zone.dataset.photoDropTarget !== currentType)
                .map(zone => ({
                    value: zone.dataset.photoDropTarget,
                    label: zone.querySelector("h3")?.textContent?.trim() || zone.dataset.photoDropTarget
                }));
            select.replaceChildren(...options.map(item => {
                const option = document.createElement("option");
                option.value = item.value;
                option.textContent = item.label;
                return option;
            }));
        }

        async function movePhoto(card, targetPhotoType) {
            const url = card.dataset.reclassifyUrl;
            const form = card.querySelector("[data-photo-move-form]");
            if (!url || !form || !targetPhotoType || targetPhotoType === card.dataset.photoType) return;

            const body = new FormData(form);
            body.set("TargetPhotoType", targetPhotoType);
            card.classList.add("photo-card-saving");
            showStatus("Moving photo...");
            try {
                const response = await fetch(url, {
                    method: "POST",
                    body,
                    credentials: "same-origin",
                    headers: { Accept: "application/json" }
                });
                const payload = await response.json().catch(() => ({}));
                if (!response.ok || payload.succeeded === false) {
                    throw new Error(payload.error || "The photo could not be moved.");
                }

                const destination = root.querySelector(`[data-photo-drop-target="${CSS.escape(targetPhotoType)}"] [data-photo-card-list]`);
                if (!destination) throw new Error("The destination photo group is unavailable.");
                destination.append(card);
                card.dataset.photoType = targetPhotoType;
                rebuildMoveOptions(card);
                updateEmptyStates();
                showStatus(`Photo moved to ${destination.closest("[data-photo-drop-target]")?.querySelector("h3")?.textContent?.trim() || targetPhotoType}.`);
                card.classList.add("photo-card-saved");
                window.setTimeout(() => card.classList.remove("photo-card-saved"), 1600);
            } catch (error) {
                showStatus(error instanceof Error ? error.message : "The photo could not be moved.", true);
            } finally {
                card.classList.remove("photo-card-saving");
            }
        }

        for (const card of root.querySelectorAll("[data-photo-card][draggable=true]")) {
            card.addEventListener("dragstart", event => {
                draggedCard = card;
                event.dataTransfer?.setData("text/plain", card.dataset.photoId || "");
                for (const zone of root.querySelectorAll("[data-photo-drop-target]")) {
                    zone.classList.toggle("photo-drop-valid", zone.dataset.photoDropTarget !== card.dataset.photoType);
                }
            });
            card.addEventListener("dragend", () => {
                draggedCard = null;
                for (const zone of root.querySelectorAll("[data-photo-drop-target]")) {
                    zone.classList.remove("photo-drop-valid", "photo-drop-active");
                }
            });
        }

        for (const zone of root.querySelectorAll("[data-photo-drop-target]")) {
            zone.addEventListener("dragover", event => {
                if (!draggedCard || zone.dataset.photoDropTarget === draggedCard.dataset.photoType) return;
                event.preventDefault();
                zone.classList.add("photo-drop-active");
            });
            zone.addEventListener("dragleave", () => zone.classList.remove("photo-drop-active"));
            zone.addEventListener("drop", event => {
                event.preventDefault();
                zone.classList.remove("photo-drop-active");
                if (draggedCard) void movePhoto(draggedCard, zone.dataset.photoDropTarget);
            });
        }

        for (const form of root.querySelectorAll("[data-photo-move-form]")) {
            form.addEventListener("submit", event => {
                event.preventDefault();
                const card = form.closest("[data-photo-card]");
                const target = form.querySelector("[data-photo-move-target]")?.value;
                if (card && target) void movePhoto(card, target);
            });
        }

        updateEmptyStates();
    }

    for (const root of document.querySelectorAll("[data-photo-reclassification]")) initialize(root);
})();
