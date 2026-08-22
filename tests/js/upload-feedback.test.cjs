const test = require("node:test");
const assert = require("node:assert/strict");
const feedback = require("../../src/CropQc.Web/wwwroot/js/upload-feedback.js");

class Classes {
    constructor() { this.values = new Set(); }
    add(value) { this.values.add(value); }
    remove(value) { this.values.delete(value); }
    toggle(value, enabled) { enabled ? this.values.add(value) : this.values.delete(value); }
    contains(value) { return this.values.has(value); }
}

class Control {
    constructor(text = "Save Photo") {
        this.dataset = {};
        this.disabled = false;
        this.innerHTML = text;
        this.textContent = text;
    }
    matches(selector) { return selector === "button[type='submit']"; }
}

class Status {
    constructor() {
        this.hidden = true;
        this.classList = new Classes();
        this.message = { textContent: "" };
        this.spinner = { hidden: true };
    }
    querySelector(selector) {
        if (selector === "[data-upload-feedback-message]") return this.message;
        if (selector === "[data-upload-feedback-spinner]") return this.spinner;
        return null;
    }
}

class Form {
    constructor(status, submit) {
        this.status = status;
        this.submit = submit;
        this.dataset = {};
        this.attributes = new Map();
        this.classList = new Classes();
        this.listeners = new Map();
    }
    querySelector(selector) {
        if (selector === "[data-upload-feedback]") return this.status;
        if (selector.includes("submit")) return this.submit;
        return null;
    }
    setAttribute(name, value) { this.attributes.set(name, value); }
    removeAttribute(name) { this.attributes.delete(name); }
    addEventListener(name, listener) { this.listeners.set(name, listener); }
}

test("first valid submit immediately locks the button and announces indeterminate progress", () => {
    const status = new Status();
    const submit = new Control();
    const form = new Form(status, submit);
    const controller = feedback.create(form);

    assert.equal(controller.begin("Uploading photo...", [submit]), true);
    assert.equal(submit.disabled, true);
    assert.equal(submit.textContent, "Uploading photo...");
    assert.equal(status.hidden, false);
    assert.equal(status.spinner.hidden, false);
    assert.equal(status.message.textContent, "Uploading photo...");
    assert.equal(form.attributes.get("aria-busy"), "true");
});

test("a second submit is rejected while a delayed first request remains busy", async () => {
    const status = new Status();
    const submit = new Control();
    const form = new Form(status, submit);
    const controller = feedback.create(form);
    let requests = 0;
    let releaseRequest;
    const delayedRequest = new Promise(resolve => { releaseRequest = resolve; });

    const send = async () => {
        if (!controller.begin("Uploading photo...", [submit])) return false;
        requests++;
        await delayedRequest;
        controller.release("Photo uploaded successfully.");
        return true;
    };

    const first = send();
    assert.equal(await send(), false);
    assert.equal(requests, 1);
    assert.equal(controller.isBusy(), true);
    releaseRequest();
    assert.equal(await first, true);
    assert.equal(status.message.textContent, "Photo uploaded successfully.");
    assert.equal(status.spinner.hidden, true);
});

test("delayed failure clears busy state, restores controls, and permits retry", () => {
    const status = new Status();
    const submit = new Control();
    const form = new Form(status, submit);
    const controller = feedback.create(form);

    controller.begin("Uploading 3 photos...", [submit]);
    controller.fail("Photos could not be uploaded. Retry when ready.");

    assert.equal(controller.isBusy(), false);
    assert.equal(submit.disabled, false);
    assert.equal(submit.innerHTML, "Save Photo");
    assert.equal(form.attributes.has("aria-busy"), false);
    assert.equal(status.classList.contains("upload-feedback-error"), true);
    assert.equal(controller.begin("Uploading 3 photos...", [submit]), true);
});

test("generic form binding prevents Enter, double-click, and requestSubmit repeats", () => {
    const status = new Status();
    const submit = new Control("Upload Packout Result");
    const form = new Form(status, submit);
    form.dataset.uploadBusyText = "Uploading packout report files...";
    feedback.bind(form);
    const handler = form.listeners.get("submit");
    let prevented = 0;

    handler({ preventDefault: () => prevented++ });
    handler({ preventDefault: () => prevented++ });
    handler({ preventDefault: () => prevented++ });

    assert.equal(prevented, 2);
    assert.equal(submit.disabled, true);
    assert.equal(status.message.textContent, "Uploading packout report files...");
});
