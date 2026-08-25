((root, factory) => {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    if (root) root.CropQcUploadFeedback = api;
})(typeof window !== "undefined" ? window : globalThis, () => {
    "use strict";

    function create(form, options = {}) {
        if (!form) throw new Error("An upload form is required.");
        const status = options.status || form.querySelector("[data-upload-feedback]");
        const message = status?.querySelector("[data-upload-feedback-message]") || status;
        const spinner = status?.querySelector("[data-upload-feedback-spinner]");
        const progress = status?.querySelector("[data-upload-feedback-progress]");
        let busy = false;
        let lockedControls = [];

        function setStatus(text, isError = false, showSpinner = false) {
            if (!status || !message) return;
            message.textContent = text || "";
            status.hidden = !text;
            status.classList?.toggle("upload-feedback-error", isError);
            if (spinner) spinner.hidden = !showSpinner;
        }

        function begin(text, controls = []) {
            if (busy) return false;
            busy = true;
            form.setAttribute("aria-busy", "true");
            form.classList?.add("upload-workflow-busy");
            lockedControls = [...new Set(controls.filter(Boolean))];
            for (const control of lockedControls) {
                control.dataset.uploadFeedbackWasDisabled = control.disabled ? "true" : "false";
                if (control.matches?.("button[type='submit']")) {
                    control.dataset.uploadFeedbackOriginalHtml = control.innerHTML;
                    control.textContent = text;
                }
                control.disabled = true;
            }
            setStatus(text, false, true);
            return true;
        }

        function update(text) {
            if (busy) setStatus(text, false, true);
        }

        function setProgress(percent = null) {
            if (!progress) return;
            progress.hidden = false;
            if (percent === null) progress.removeAttribute("value");
            else progress.value = Math.max(0, Math.min(100, percent));
        }

        function release(text = "", isError = false) {
            for (const control of lockedControls) {
                control.disabled = control.dataset.uploadFeedbackWasDisabled === "true";
                delete control.dataset.uploadFeedbackWasDisabled;
                if (control.dataset.uploadFeedbackOriginalHtml !== undefined) {
                    control.innerHTML = control.dataset.uploadFeedbackOriginalHtml;
                    delete control.dataset.uploadFeedbackOriginalHtml;
                }
            }
            lockedControls = [];
            busy = false;
            form.removeAttribute("aria-busy");
            form.classList?.remove("upload-workflow-busy");
            if (progress) {
                progress.hidden = true;
                progress.value = 0;
            }
            setStatus(text, isError, false);
        }

        return {
            begin,
            update,
            setProgress,
            fail: text => release(text, true),
            release,
            isBusy: () => busy
        };
    }

    function bind(form) {
        if (!form || form.dataset.uploadFeedbackBound === "true") return null;
        form.dataset.uploadFeedbackBound = "true";
        const submit = form.querySelector("button[type='submit'], input[type='submit']");
        const controller = create(form);
        form.addEventListener("submit", event => {
            const text = form.dataset.uploadBusyText || "Uploading files...";
            if (!controller.begin(text, [submit])) event.preventDefault();
        });
        return controller;
    }

    function bindProgressUpload(form) {
        if (!form || form.dataset.uploadProgressBound === "true") return null;
        form.dataset.uploadProgressBound = "true";
        const submit = form.querySelector("button[type='submit'], input[type='submit']");
        const fileInput = form.querySelector("input[type='file']");
        const controller = create(form);
        form.addEventListener("submit", event => {
            event.preventDefault();
            const files = [...(fileInput?.files || [])];
            const body = new FormData(form);
            const initialText = form.dataset.uploadBusyText || "Uploading files...";
            if (!controller.begin(initialText, [submit, fileInput])) return;
            controller.setProgress(0);

            const request = new XMLHttpRequest();
            request.open((form.method || "POST").toUpperCase(), form.action, true);
            request.setRequestHeader("X-Requested-With", "XMLHttpRequest");
            request.upload.addEventListener("progress", uploadEvent => {
                if (!uploadEvent.lengthComputable) {
                    controller.setProgress(null);
                    controller.update("Uploading Packout document...");
                    return;
                }
                const percent = Math.min(100, Math.round((uploadEvent.loaded / uploadEvent.total) * 100));
                controller.setProgress(percent);
                const label = files.length === 1 ? files[0].name : `${files.length} Packout documents`;
                controller.update(`Uploading ${label} — ${percent}%`);
            });
            request.upload.addEventListener("load", () => {
                controller.setProgress(null);
                controller.update("Upload complete — processing report and saving the original...");
            });
            request.addEventListener("load", () => {
                let payload = null;
                try { payload = JSON.parse(request.responseText || "{}"); } catch { /* handled below */ }
                if (request.status >= 200 && request.status < 300 && payload?.success) {
                    controller.setProgress(100);
                    controller.update(payload.message || "Packout document saved.");
                    if (payload.redirectUrl) window.location.assign(payload.redirectUrl);
                    else controller.release(payload.message || "Packout document saved.");
                    return;
                }
                controller.fail(payload?.message || "The Packout document could not be uploaded. The selected file remains available to retry.");
            });
            request.addEventListener("error", () => controller.fail("The Packout document upload was interrupted. The selected file remains available to retry."));
            request.addEventListener("abort", () => controller.fail("The Packout document upload was canceled. The selected file remains available to retry."));
            request.send(body);
        });
        return controller;
    }

    function initialize(root = document) {
        root.querySelectorAll("[data-upload-feedback-form]:not([data-upload-progress-form])").forEach(bind);
        root.querySelectorAll("[data-upload-progress-form]").forEach(bindProgressUpload);
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => initialize());
        else initialize();
    }

    return { create, bind, bindProgressUpload, initialize };
});
