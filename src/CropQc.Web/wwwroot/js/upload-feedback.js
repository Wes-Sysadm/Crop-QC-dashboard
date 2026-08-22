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
            setStatus(text, isError, false);
        }

        return {
            begin,
            update,
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

    function initialize(root = document) {
        root.querySelectorAll("[data-upload-feedback-form]").forEach(bind);
    }

    if (typeof document !== "undefined") {
        if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => initialize());
        else initialize();
    }

    return { create, bind, initialize };
});
