const test = require("node:test");
const assert = require("node:assert/strict");
const controls = require("../../src/CropQc.Web/wwwroot/js/device-camera-controls.js");

class MemoryStorage {
    constructor() { this.values = new Map(); }
    getItem(key) { return this.values.get(key) ?? null; }
    setItem(key, value) { this.values.set(key, value); }
}

class MockTrack {
    constructor(capabilities = {}, settings = {}) {
        this.capabilities = capabilities;
        this.settings = { ...settings };
        this.applied = [];
        this.stopped = false;
        this.failure = null;
    }

    getCapabilities() { return this.capabilities; }
    getSettings() { return { ...this.settings }; }
    stop() { this.stopped = true; }

    async applyConstraints(constraints) {
        this.applied.push(constraints);
        if (this.failure) throw this.failure;
        Object.assign(this.settings, constraints.advanced?.[0] || {});
    }
}

const fullCapabilities = {
    focusMode: ["single-shot", "continuous", "manual"],
    focusDistance: { min: 0, max: 10, step: 0.5 },
    brightness: { min: -64, max: 64, step: 1 },
    contrast: { min: 0, max: 100, step: 5 }
};

test("full Logitech-like capabilities expose focus, brightness, and contrast", () => {
    const result = controls.describeCapabilities(fullCapabilities);
    assert.equal(result.autoFocusMode, "continuous");
    assert.equal(result.manualFocusMode, "manual");
    assert.equal(result.manualFocusSupported, true);
    assert.deepEqual(result.focusDistance, { min: 0, max: 10, step: 0.5 });
    assert.deepEqual(result.brightness, { min: -64, max: 64, step: 1 });
    assert.deepEqual(result.contrast, { min: 0, max: 100, step: 5 });
    assert.equal(controls.preferredAutoFocusMode({ focusMode: ["single-shot", "manual"] }), "single-shot");
});

test("partial and basic cameras expose only real capabilities", () => {
    const brightnessOnly = controls.describeCapabilities({ brightness: { min: 1, max: 9, step: 2 } });
    assert.deepEqual(brightnessOnly.brightness, { min: 1, max: 9, step: 2 });
    assert.equal(brightnessOnly.manualFocusSupported, false);
    assert.equal(brightnessOnly.contrast, null);

    const basic = controls.describeCapabilities({ width: { min: 320, max: 1920 } });
    assert.equal(basic.autoFocusMode, null);
    assert.equal(basic.manualFocusSupported, false);
    assert.equal(basic.brightness, null);
    assert.equal(basic.contrast, null);
});

test("manual focus applies supported hardware mode and distance", async () => {
    const track = new MockTrack(fullCapabilities, {
        focusMode: "continuous",
        focusDistance: 2,
        brightness: 0,
        contrast: 50
    });
    const session = new controls.CameraControlSession(track);

    const actual = await session.apply({ focusMode: "manual", focusDistance: 7.5 });

    assert.deepEqual(track.applied, [{ advanced: [{ focusMode: "manual", focusDistance: 7.5 }] }]);
    assert.equal(actual.focusMode, "manual");
    assert.equal(actual.focusDistance, 7.5);
});

test("brightness and contrast apply only the selected hardware property", async () => {
    const track = new MockTrack(fullCapabilities, { brightness: 0, contrast: 50 });
    const session = new controls.CameraControlSession(track);

    await session.apply({ brightness: 12 });
    await session.apply({ contrast: 65 });

    assert.deepEqual(track.applied, [
        { advanced: [{ brightness: 12 }] },
        { advanced: [{ contrast: 65 }] }
    ]);
    assert.equal(track.getSettings().brightness, 12);
    assert.equal(track.getSettings().contrast, 65);
});

test("saved settings are camera-specific and stale ranges are clamped", () => {
    const storage = new MemoryStorage();
    controls.saveControls(storage, "camera-a", {
        focusMode: "manual",
        focusDistance: 9,
        brightness: 60,
        contrast: 95
    });

    assert.deepEqual(controls.savedControls(storage, "camera-b"), {});
    const sanitized = controls.sanitizeValues(controls.savedControls(storage, "camera-a"), {
        ...fullCapabilities,
        focusDistance: { min: 1, max: 5, step: 0.5 },
        brightness: { min: -10, max: 10, step: 1 },
        contrast: { min: 20, max: 80, step: 10 }
    });

    assert.deepEqual(sanitized.values, {
        focusMode: "manual",
        focusDistance: 5,
        brightness: 10,
        contrast: 80
    });
});

test("unsupported saved values are discarded without touching video track", async () => {
    const track = new MockTrack({ brightness: { min: 0, max: 10, step: 1 } }, { brightness: 4 });
    const sanitized = controls.sanitizeValues({
        focusMode: "manual",
        focusDistance: 3,
        brightness: 7,
        contrast: 50
    }, track.getCapabilities());
    const actual = await new controls.CameraControlSession(track).apply(sanitized.values);

    assert.deepEqual(sanitized.values, { brightness: 7 });
    assert.deepEqual(new Set(sanitized.discarded), new Set(["focusMode", "focusDistance", "contrast"]));
    assert.equal(actual.brightness, 7);
    assert.equal(track.stopped, false);
});

test("constraint failure preserves the stream and authoritative settings", async () => {
    const track = new MockTrack(fullCapabilities, { brightness: 3 });
    track.failure = new DOMException("rejected", "OverconstrainedError");
    const session = new controls.CameraControlSession(track);

    await assert.rejects(session.apply({ brightness: 8 }), { name: "OverconstrainedError" });
    assert.equal(session.getSettings().brightness, 3);
    assert.equal(track.stopped, false);
});

test("rapid updates are coalesced and final value wins", async () => {
    const track = new MockTrack({ contrast: { min: 0, max: 100, step: 1 } }, { contrast: 10 });
    const session = new controls.CameraControlSession(track);

    const first = session.apply({ contrast: 20 });
    const second = session.apply({ contrast: 40 });
    const third = session.apply({ contrast: 75 });
    await Promise.all([first, second, third]);

    assert.equal(track.applied.length, 1);
    assert.deepEqual(track.applied[0], { advanced: [{ contrast: 75 }] });
    assert.equal(track.getSettings().contrast, 75);
});

test("reset clears only the selected camera's saved controls", () => {
    const storage = new MemoryStorage();
    controls.saveControls(storage, "camera-a", { brightness: 5 });
    controls.saveControls(storage, "camera-b", { contrast: 8 });

    controls.clearControls(storage, "camera-a");

    assert.deepEqual(controls.savedControls(storage, "camera-a"), {});
    assert.deepEqual(controls.savedControls(storage, "camera-b"), { contrast: 8 });
});
