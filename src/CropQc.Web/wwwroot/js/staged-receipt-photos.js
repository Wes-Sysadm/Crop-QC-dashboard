(() => {
    "use strict";

    const maxPhotoSize = 15 * 1024 * 1024;
    const allowedTypes = new Set(["image/jpeg", "image/jpg", "image/png", "image/webp"]);
    const allowedExtensions = new Set(["jpg", "jpeg", "png", "webp"]);
    const friendlyTypes = { BinTruck: "Truck photo", TopOfTruck: "Top of truck", Other: "Other" };

    function extension(fileName) {
        const parts = (fileName || "").toLowerCase().split(".");
        return parts.length > 1 ? parts.pop() : "";
    }

    function validationError(file) {
        if (!file || file.size <= 0) return "Choose a non-empty photo file.";
        if (file.size > maxPhotoSize) return `${file.name} is larger than 15 MB.`;
        if (["heic", "heif"].includes(extension(file.name)) || ["image/heic", "image/heif"].includes((file.type || "").toLowerCase())) {
            return `${file.name} is an HEIC/HEIF image, which Crop QC cannot safely preview or upload. Use Take Photo, or choose a JPG, PNG, or WEBP image.`;
        }
        if (!allowedTypes.has((file.type || "").toLowerCase()) || !allowedExtensions.has(extension(file.name))) {
            return `${file.name} is not supported. Choose a JPG, PNG, or WEBP image.`;
        }
        return null;
    }

    function trashIcon() {
        const namespace = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(namespace, "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        const path = document.createElementNS(namespace, "path");
        path.setAttribute("d", "M9 3h6l1 2h4v2h-1l-1 14H6L5 7H4V5h4l1-2zm-1.99 4 1 12h7.98l1-12H7.01zM10 9h2v8h-2V9zm4 0h2v8h-2V9z");
        svg.appendChild(path);
        return svg;
    }

    function initialize(section) {
        if (section.dataset.initialized === "true") return;
        section.dataset.initialized = "true";
        const form = section.closest("form");
        const cameraPicker = section.querySelector("[data-staged-photo-camera]");
        const picker = section.querySelector("[data-staged-photo-picker]");
        const typeSelect = section.querySelector("[data-staged-photo-type]");
        const browse = section.querySelector("[data-staged-photo-browse]");
        const takePhoto = section.querySelector("[data-staged-photo-take]");
        const dropZone = section.querySelector("[data-staged-photo-drop-zone]");
        const empty = section.querySelector("[data-staged-photo-empty]");
        const list = section.querySelector("[data-staged-photo-list]");
        const fields = section.querySelector("[data-staged-photo-fields]");
        const message = section.querySelector("[data-staged-photo-message]");
        const items = [];
        let nextId = 1;
        let submitting = false;

        function showMessage(text) {
            message.textContent = text || "";
            message.hidden = !text;
        }

        function rebuildPostFields() {
            fields.replaceChildren();
            items.forEach((item, index) => {
                const transfer = new DataTransfer();
                transfer.items.add(item.file);
                const fileInput = document.createElement("input");
                fileInput.type = "file";
                fileInput.name = `stagedPhotos[${index}].PhotoFile`;
                fileInput.files = transfer.files;
                fields.appendChild(fileInput);

                for (const [name, value] of [["PhotoType", item.photoType], ["PhotoSource", item.photoSource]]) {
                    const input = document.createElement("input");
                    input.type = "hidden";
                    input.name = `stagedPhotos[${index}].${name}`;
                    input.value = value;
                    fields.appendChild(input);
                }
            });
        }

        function remove(item) {
            const index = items.indexOf(item);
            if (index < 0) return;
            items.splice(index, 1);
            URL.revokeObjectURL(item.previewUrl);
            item.card.remove();
            empty.hidden = items.length > 0;
            rebuildPostFields();
            showMessage("");
        }

        function stage(file, photoType, photoSource) {
            const error = validationError(file);
            if (error) {
                showMessage(error);
                return false;
            }
            if (!("DataTransfer" in window)) {
                showMessage("This browser cannot stage receipt photos. Save the receipt, then add the photo from Receipt Photos.");
                return false;
            }

            const item = {
                id: nextId++,
                file,
                photoType: friendlyTypes[photoType] ? photoType : "Other",
                photoSource: photoSource || "Upload File",
                previewUrl: URL.createObjectURL(file),
                card: document.createElement("article")
            };
            item.card.className = "staged-receipt-photo-card";
            item.card.dataset.stagedPhotoId = String(item.id);
            const image = document.createElement("img");
            image.src = item.previewUrl;
            image.alt = `${friendlyTypes[item.photoType]} preview`;
            const body = document.createElement("div");
            body.className = "staged-receipt-photo-card-body";
            const label = document.createElement("strong");
            label.textContent = friendlyTypes[item.photoType];
            const fileName = document.createElement("span");
            fileName.className = "muted";
            fileName.textContent = item.file.name;
            const removeButton = document.createElement("button");
            removeButton.type = "button";
            removeButton.className = "icon-button danger-icon-button";
            removeButton.setAttribute("aria-label", "Remove staged photo");
            removeButton.title = "Remove staged photo";
            removeButton.appendChild(trashIcon());
            removeButton.addEventListener("click", () => remove(item));
            body.append(label, fileName, removeButton);
            item.card.append(image, body);
            items.push(item);
            list.appendChild(item.card);
            empty.hidden = true;
            rebuildPostFields();
            showMessage("");
            return true;
        }

        function stageFiles(files, photoSource = "Upload File") {
            const photoType = typeSelect.value;
            for (const file of Array.from(files || [])) stage(file, photoType, photoSource);
        }

        takePhoto?.addEventListener("click", () => cameraPicker?.click());
        cameraPicker?.addEventListener("change", () => {
            stageFiles(cameraPicker.files, "Mobile Camera");
            cameraPicker.value = "";
        });
        browse.addEventListener("click", () => picker.click());
        picker.addEventListener("change", () => {
            stageFiles(picker.files);
            picker.value = "";
        });
        dropZone.addEventListener("dragover", event => {
            event.preventDefault();
            dropZone.classList.add("drag-over");
        });
        dropZone.addEventListener("dragleave", () => dropZone.classList.remove("drag-over"));
        dropZone.addEventListener("drop", event => {
            event.preventDefault();
            dropZone.classList.remove("drag-over");
            stageFiles(event.dataTransfer?.files);
        });
        window.addEventListener("cropqc:stage-receipt-photo", event => {
            const detail = event.detail || {};
            if (detail.file) stage(detail.file, detail.photoType, detail.photoSource);
        });
        form?.addEventListener("submit", event => {
            if (submitting) {
                event.preventDefault();
                return;
            }
            submitting = true;
            const submit = form.querySelector("[data-receipt-submit]");
            if (submit) {
                submit.disabled = true;
                submit.textContent = "Saving receipt...";
            }
        });
        window.addEventListener("pagehide", () => items.forEach(item => URL.revokeObjectURL(item.previewUrl)));
    }

    document.querySelectorAll("[data-staged-receipt-photos]").forEach(initialize);
})();
