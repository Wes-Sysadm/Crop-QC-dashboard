const test = require("node:test");
const assert = require("node:assert/strict");
const { initialize } = require("../../src/CropQc.Web/wwwroot/js/site-navigation.js");

class FakeTarget {
    constructor(tag = "div") {
        this.tag = tag;
        this.dataset = {};
        this.attributes = new Map();
        this.listeners = new Map();
        this.children = new Map();
        this.open = false;
        this.focusCount = 0;
        this.parent = null;
    }
    addEventListener(type, handler) {
        const handlers = this.listeners.get(type) || [];
        handlers.push(handler);
        this.listeners.set(type, handlers);
    }
    fire(type, event = {}) {
        const payload = { target: this, ...event };
        for (const handler of this.listeners.get(type) || []) handler(payload);
    }
    setAttribute(name, value) { this.attributes.set(name, value); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    querySelector(selector) { return this.children.get(selector) ?? null; }
    querySelectorAll(selector) { return this.children.get(selector) ?? []; }
    contains(target) {
        for (let current = target; current; current = current.parent) if (current === this) return true;
        return false;
    }
    closest(selector) { return selector === "a" && this.tag === "a" ? this : null; }
    focus() { this.focusCount += 1; }
}

class FakeDocument extends FakeTarget {
    constructor() { super("document"); }
}

function fixture(compact = true) {
    const document = new FakeDocument();
    const header = new FakeTarget("header");
    const button = new FakeTarget("button");
    const label = new FakeTarget("span");
    const navigation = new FakeTarget("nav");
    const first = category("runs");
    const second = category("admin");
    const mediaListeners = [];
    const query = {
        matches: compact,
        addEventListener(type, handler) { if (type === "change") mediaListeners.push(handler); }
    };
    button.children.set("[data-mobile-menu-label]", label);
    navigation.children.set("[data-nav-category]", [first.element, second.element]);
    header.children.set("nav", navigation);
    navigation.parent = header;
    button.parent = header;
    document.children.set("[data-mobile-menu-button]", button);
    document.children.set("[data-primary-navigation]", navigation);
    document.children.set("[data-site-header]", header);
    const viewport = { matchMedia() { return query; } };
    const api = initialize(document, viewport);
    return { document, header, button, label, navigation, first, second, query, mediaListeners, api };
}

function category(key) {
    const element = new FakeTarget("details");
    element.dataset.navCategory = key;
    const summary = new FakeTarget("summary");
    summary.parent = element;
    element.children.set("summary", summary);
    return { element, summary };
}

test("initializes once without duplicate event binding", () => {
    const f = fixture();
    const clickCount = f.button.listeners.get("click").length;
    assert.equal(initialize(f.document, { matchMedia: () => f.query }), null);
    assert.equal(f.button.listeners.get("click").length, clickCount);
});

test("mobile hamburger opens and updates its accessible state", () => {
    const f = fixture();
    f.button.fire("click");
    assert.equal(f.navigation.dataset.mobileOpen, "true");
    assert.equal(f.button.getAttribute("aria-expanded"), "true");
    assert.equal(f.label.textContent, "Close");
});

test("mobile hamburger closes the menu and all categories", () => {
    const f = fixture();
    f.button.fire("click");
    f.first.element.open = true;
    f.first.element.fire("toggle");
    f.button.fire("click");
    assert.equal(f.navigation.dataset.mobileOpen, "false");
    assert.equal(f.first.element.open, false);
    assert.equal(f.label.textContent, "Menu");
});

test("opening a category closes every other category", () => {
    const f = fixture();
    f.first.element.open = true;
    f.first.element.fire("toggle");
    f.second.element.open = true;
    f.second.element.fire("toggle");
    assert.equal(f.first.element.open, false);
    assert.equal(f.second.element.open, true);
    assert.equal(f.second.summary.getAttribute("aria-expanded"), "true");
});

test("outside click closes dropdowns and compact menu", () => {
    const f = fixture();
    f.button.fire("click");
    f.first.element.open = true;
    f.first.element.fire("toggle");
    f.document.fire("click", { target: new FakeTarget("main") });
    assert.equal(f.first.element.open, false);
    assert.equal(f.navigation.dataset.mobileOpen, "false");
});

test("Escape closes compact navigation and restores focus to the visible menu button", () => {
    const f = fixture();
    f.first.element.open = true;
    f.first.element.fire("toggle");
    f.document.fire("keydown", { key: "Escape" });
    assert.equal(f.first.element.open, false);
    assert.equal(f.button.focusCount, 1);
    assert.equal(f.first.summary.focusCount, 0);
});

test("Escape closes a desktop dropdown and restores focus to its summary", () => {
    const f = fixture(false);
    f.first.element.open = true;
    f.first.element.fire("toggle");
    f.document.fire("keydown", { key: "Escape" });
    assert.equal(f.first.element.open, false);
    assert.equal(f.first.summary.focusCount, 1);
});

test("following a link closes compact navigation", () => {
    const f = fixture();
    f.button.fire("click");
    const link = new FakeTarget("a");
    link.parent = f.navigation;
    f.navigation.fire("click", { target: link });
    assert.equal(f.navigation.dataset.mobileOpen, "false");
});

test("leaving compact mode resets mobile navigation", () => {
    const f = fixture();
    f.button.fire("click");
    f.mediaListeners[0]({ matches: false });
    assert.equal(f.navigation.dataset.mobileOpen, "false");
});
