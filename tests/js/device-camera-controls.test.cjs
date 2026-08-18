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
        this.acceptedOverride = null;
    }

    getCapabilities() { return this.capabilities; }
    getSettings() { return { ...this.settings }; }
    stop() { this.stopped = true; }

    async applyConstraints(constraints) {
        this.applied.push(constraints);
        if (this.failure) throw this.failure;
        Object.assign(this.settings, this.acceptedOverride || constraints.advanced?.[0] || {});
    }
}

const fullCapabilities = {
    exposureMode: ["continuous", "manual"],
    exposureCompensation: { min: -2, max: 2, step: 0.25 },
    exposureTime: { min: 5, max: 1000, step: 5 },
    whiteBalanceMode: ["continuous", "manual"],
    colorTemperature: { min: 2800, max: 6500, step: 100 },
    focusMode: ["single-shot", "continuous", "manual"],
    focusDistance: { min: 0, max: 10, step: 0.5 },
    brightness: { min: -64, max: 64, step: 1 },
    contrast: { min: 0, max: 100, step: 5 },
    saturation: { min: 0, max: 100, step: 1 },
    sharpness: { min: 0, max: 10, step: 1 },
    iso: { min: 100, max: 3200, step: 100 }
};

test("full Logitech-like capabilities expose focus, lighting, color, and advanced controls", () => {
    const result = controls.describeCapabilities(fullCapabilities);
    assert.equal(result.autoFocusMode, "continuous");
    assert.equal(result.manualFocusMode, "manual");
    assert.equal(result.manualFocusSupported, true);
    assert.deepEqual(result.focusDistance, { min: 0, max: 10, step: 0.5 });
    assert.deepEqual(result.brightness, { min: -64, max: 64, step: 1 });
    assert.deepEqual(result.contrast, { min: 0, max: 100, step: 5 });
    assert.deepEqual(result.saturation, { min: 0, max: 100, step: 1 });
    assert.deepEqual(result.sharpness, { min: 0, max: 10, step: 1 });
    assert.deepEqual(result.iso, { min: 100, max: 3200, step: 100 });
    assert.equal(result.autoExposureMode, "continuous");
    assert.equal(result.manualExposureMode, "manual");
    assert.equal(result.manualExposureSupported, true);
    assert.equal(result.autoWhiteBalanceMode, "continuous");
    assert.equal(result.manualWhiteBalanceMode, "manual");
    assert.equal(result.manualWhiteBalanceSupported, true);
    assert.deepEqual(result.exposureCompensation, { min: -2, max: 2, step: 0.25 });
    assert.deepEqual(result.exposureTime, { min: 5, max: 1000, step: 5 });
    assert.deepEqual(result.colorTemperature, { min: 2800, max: 6500, step: 100 });
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
    assert.equal(basic.iso, null);
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

test("exposure and white-balance modes and ranges sanitize against actual capabilities", () => {
    const sanitized = controls.sanitizeValues({
        exposureMode: "manual",
        exposureCompensation: 3,
        exposureTime: 1003,
        whiteBalanceMode: "manual",
        colorTemperature: 4219,
        saturation: 150,
        sharpness: -2,
        iso: 3333
    }, fullCapabilities);

    assert.deepEqual(sanitized.values, {
        exposureMode: "manual",
        whiteBalanceMode: "manual",
        exposureCompensation: 2,
        exposureTime: 1000,
        colorTemperature: 4200,
        saturation: 100,
        sharpness: 0,
        iso: 3200
    });
});

test("automatic color and exposure never send manual dependent values", async () => {
    const values = controls.automaticColorExposureValues(fullCapabilities);
    assert.deepEqual(values, { exposureMode: "continuous", whiteBalanceMode: "continuous" });
    assert.equal(values.exposureTime, undefined);
    assert.equal(values.colorTemperature, undefined);

    const track = new MockTrack(fullCapabilities, { exposureMode: "manual", whiteBalanceMode: "manual" });
    await new controls.CameraControlSession(track).apply(values);
    assert.deepEqual(track.applied, [{ advanced: [{ exposureMode: "continuous", whiteBalanceMode: "continuous" }] }]);
});

test("manual exposure and white balance apply current-mode scalar values", async () => {
    const track = new MockTrack(fullCapabilities, {
        exposureMode: "manual",
        exposureTime: 100,
        whiteBalanceMode: "manual",
        colorTemperature: 4000
    });
    const session = new controls.CameraControlSession(track);

    await session.apply({ exposureTime: 205 });
    await session.apply({ colorTemperature: 4300 });

    assert.deepEqual(track.applied, [
        { advanced: [{ exposureTime: 205 }] },
        { advanced: [{ colorTemperature: 4300 }] }
    ]);
});

test("manual-only values are rejected while the camera is in automatic modes", async () => {
    const track = new MockTrack(fullCapabilities, {
        exposureMode: "continuous",
        exposureTime: 100,
        whiteBalanceMode: "continuous",
        colorTemperature: 4000
    });
    const session = new controls.CameraControlSession(track);

    await session.apply({ exposureTime: 205, colorTemperature: 4300 });

    assert.deepEqual(track.applied, []);
});

test("lock current color and exposure uses actual settings and supports partial lock", () => {
    const full = controls.lockCurrentColorExposureValues(fullCapabilities, {
        exposureTime: 250,
        colorTemperature: 4200
    });
    assert.equal(full.canLock, true);
    assert.deepEqual(full.locked, ["Exposure", "White balance"]);
    assert.deepEqual(full.values, {
        exposureMode: "manual",
        exposureTime: 250,
        whiteBalanceMode: "manual",
        colorTemperature: 4200
    });

    const exposureOnlyCapabilities = {
        exposureMode: ["continuous", "manual"],
        exposureTime: { min: 5, max: 1000, step: 5 }
    };
    const partial = controls.lockCurrentColorExposureValues(exposureOnlyCapabilities, { exposureTime: 100 });
    assert.equal(partial.canLock, true);
    assert.deepEqual(partial.locked, ["Exposure"]);
    assert.deepEqual(partial.unsupported, ["White balance"]);

    const unavailable = controls.lockCurrentColorExposureValues(fullCapabilities, {});
    assert.equal(unavailable.canLock, false);
    assert.deepEqual(unavailable.values, {});
});

test("actual accepted settings are persisted instead of requested values", async () => {
    const track = new MockTrack(fullCapabilities, { saturation: 40 });
    track.acceptedOverride = { saturation: 63.5 };
    const session = new controls.CameraControlSession(track);

    const actual = await session.apply({ saturation: 64 });
    const persisted = controls.settingsForPersistence(actual, fullCapabilities);

    assert.equal(actual.saturation, 63.5);
    assert.equal(persisted.saturation, 63.5);
});

test("test-photo settings snapshots and camera details omit unsupported fields and device ids", () => {
    const settings = {
        deviceId: "secret-device-id",
        exposureMode: "manual",
        exposureTime: 250,
        whiteBalanceMode: "manual",
        colorTemperature: 4200,
        brightness: 2,
        contrast: 51,
        saturation: 64
    };
    assert.deepEqual(controls.importantSettingsSnapshot(settings, fullCapabilities), [
        { label: "Exposure", value: "Manual · 250" },
        { label: "White balance", value: "Manual · 4200 K" },
        { label: "Brightness", value: "2" },
        { label: "Contrast", value: "51" },
        { label: "Saturation", value: "64" }
    ]);
    const details = controls.cameraDetails("Logitech", settings, { brightness: fullCapabilities.brightness });
    assert.deepEqual(details.supportedControls, ["brightness"]);
    assert.deepEqual(details.currentSettings, { brightness: 2 });
    assert.equal(JSON.stringify(details).includes("secret-device-id"), false);
});

test("test-photo capture uses the normal direct JPEG path at quality 0.92", async () => {
    const calls = [];
    const canvas = {
        width: 0,
        height: 0,
        getContext: () => ({ drawImage: (...args) => calls.push(["drawImage", ...args]) }),
        toBlob: (callback, type, quality) => {
            calls.push(["toBlob", type, quality]);
            callback({ type, quality });
        }
    };
    const video = { videoWidth: 1920, videoHeight: 1080 };

    const blob = await controls.captureJpegBlob(video, canvas, 0.92);

    assert.equal(canvas.width, 1920);
    assert.equal(canvas.height, 1080);
    assert.deepEqual(calls[1], ["toBlob", "image/jpeg", 0.92]);
    assert.deepEqual(blob, { type: "image/jpeg", quality: 0.92 });
});

test("test-photo buffer keeps the newest two and revokes replaced and cleared URLs", () => {
    let number = 0;
    const revoked = [];
    const buffer = new controls.TestPhotoBuffer({
        createObjectURL: () => `blob:test-${++number}`,
        revokeObjectURL: value => revoked.push(value)
    });

    buffer.add({}, [{ label: "Exposure", value: "Auto" }]);
    buffer.add({}, []);
    buffer.add({}, []);

    assert.deepEqual(buffer.items.map(item => item.url), ["blob:test-2", "blob:test-3"]);
    assert.deepEqual(revoked, ["blob:test-1"]);
    buffer.clear();
    assert.deepEqual(revoked, ["blob:test-1", "blob:test-2", "blob:test-3"]);
    assert.deepEqual(buffer.items, []);
});

test("reset clears only the selected camera's saved controls", () => {
    const storage = new MemoryStorage();
    controls.saveControls(storage, "camera-a", { brightness: 5 });
    controls.saveControls(storage, "camera-b", { contrast: 8 });

    controls.clearControls(storage, "camera-a");

    assert.deepEqual(controls.savedControls(storage, "camera-a"), {});
    assert.deepEqual(controls.savedControls(storage, "camera-b"), { contrast: 8 });
});
