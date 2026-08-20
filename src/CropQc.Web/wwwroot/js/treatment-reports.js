(() => {
    const allowed = new Set(["application/pdf", "image/jpeg", "image/png", "image/webp"]);
    const maxBytes = 15 * 1024 * 1024;
    const allUrls = new Set();

    function sizeLabel(bytes) {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    }

    function initialize(form) {
        const inputs = [...form.querySelectorAll("[data-treatment-report-input]")];
        const preview = form.querySelector("[data-treatment-report-previews]");
        const empty = form.querySelector("[data-treatment-report-empty]");
        const validation = form.querySelector("[data-treatment-report-validation]");
        const submit = form.querySelector("[data-treatment-report-submit]");
        const urls = new Set();
        if (!preview || !empty) return;

        function render() {
            for (const url of urls) {
                URL.revokeObjectURL(url);
                allUrls.delete(url);
            }
            urls.clear();
            preview.replaceChildren();
            let count = 0;
            for (const input of inputs) {
                [...input.files].forEach((file, index) => {
                    count++;
                    const card = document.createElement("article");
                    card.className = "treatment-report-preview-card";
                    if (file.type.startsWith("image/")) {
                        const url = URL.createObjectURL(file);
                        urls.add(url);
                        allUrls.add(url);
                        const image = document.createElement("img");
                        image.src = url;
                        image.alt = `Preview of ${file.name}`;
                        card.append(image);
                    } else {
                        const badge = document.createElement("span");
                        badge.className = "treatment-report-pdf";
                        badge.textContent = "PDF";
                        card.append(badge);
                    }
                    const details = document.createElement("div");
                    const name = document.createElement("strong");
                    name.textContent = file.name;
                    const size = document.createElement("small");
                    size.textContent = `${file.type === "application/pdf" ? "PDF · " : ""}${sizeLabel(file.size)}`;
                    details.append(name, document.createElement("br"), size);
                    const remove = document.createElement("button");
                    remove.type = "button";
                    remove.className = "danger-button treatment-report-remove-stage";
                    remove.textContent = "Remove";
                    remove.setAttribute("aria-label", `Remove ${file.name}`);
                    remove.addEventListener("click", () => {
                        const transfer = new DataTransfer();
                        [...input.files].forEach((candidate, candidateIndex) => {
                            if (candidateIndex !== index) transfer.items.add(candidate);
                        });
                        input.files = transfer.files;
                        render();
                    });
                    card.append(details, remove);
                    preview.append(card);
                });
            }
            empty.hidden = count > 0;
            if (submit && submit.textContent.includes("Upload")) submit.disabled = count === 0;
        }

        for (const input of inputs) {
            input.addEventListener("change", () => {
                const transfer = new DataTransfer();
                let rejected = 0;
                [...input.files].forEach(file => {
                    if (file.size === 0 || file.size > maxBytes || !allowed.has(file.type)) {
                        rejected++;
                        return;
                    }
                    transfer.items.add(file);
                });
                input.files = transfer.files;
                if (validation) {
                    validation.textContent = rejected === 0
                        ? ""
                        : `${rejected} file${rejected === 1 ? " was" : "s were"} not selected. Use PDF, JPG, PNG, or WEBP files no larger than 15 MB.`;
                }
                render();
            });
        }
        form.addEventListener("submit", () => {
            if (submit) submit.disabled = true;
        });
        render();
    }

    document.querySelectorAll("[data-treatment-report-form]").forEach(initialize);
    window.addEventListener("pagehide", () => {
        for (const url of allUrls) URL.revokeObjectURL(url);
        allUrls.clear();
    });
})();
