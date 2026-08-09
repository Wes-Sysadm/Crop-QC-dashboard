(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    root.CropQcCameraControls = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const storageKey = "cropqc.deviceCapture.cameraControls";
    const scalarNames = ["focusDistance", "brightness", "contrast"];

    function asFiniteNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    function normalizeRange(capability) {
        if (!capability || typeof capability !== "object") return null;
        const min = asFiniteNumber(capability.min);
        const max = asFiniteNumber(capability.max);
        if (min === null || max === null || max < min) return null;
        const requestedStep = asFiniteNumber(capability.step);
        return { min, max, step: requestedStep !== null && requestedStep > 0 ? requestedStep : 1 };
    }

    function clampToRange(value, capability) {
        const range = normalizeRange(capability);
        const number = asFiniteNumber(value);
        if (!range || number === null) return null;
        const clamped = Math.min(range.max, Math.max(range.min, number));
        const stepped = range.min + Math.round((clamped - range.min) / range.step) * range.step;
        const precision = Math.max(
            String(range.min).split(".")[1]?.length || 0,
            String(range.step).split(".")[1]?.length || 0,
            6);
        return Number(Math.min(range.max, Math.max(range.min, stepped)).toFixed(precision));
    }

    function focusModes(capabilities) {
        return Array.isArray(capabilities?.focusMode)
            ? capabilities.focusMode.filter(value => typeof value === "string" && value.trim())
            : [];
    }

    function preferredAutoFocusMode(capabilities) {
        const modes = focusModes(capabilities);
        return modes.find(mode => mode.toLowerCase() === "continuous")
            || modes.find(mode => mode.toLowerCase() !== "manual")
            || null;
    }

    function manualFocusMode(capabilities) {
        return focusModes(capabilities).find(mode => mode.toLowerCase() === "manual") || null;
    }

    function describeCapabilities(capabilities) {
        const safe = capabilities && typeof capabilities === "object" ? capabilities : {};
        const autoMode = preferredAutoFocusMode(safe);
        const manualMode = manualFocusMode(safe);
        const focusDistance = normalizeRange(safe.focusDistance);
        return {
            autoFocusMode: autoMode,
            manualFocusMode: manualMode,
            focusDistance,
            manualFocusSupported: Boolean(manualMode && focusDistance),
            brightness: normalizeRange(safe.brightness),
            contrast: normalizeRange(safe.contrast)
        };
    }

    function sanitizeValues(values, capabilities) {
        const source = values && typeof values === "object" ? values : {};
        const safeCapabilities = capabilities && typeof capabilities === "object" ? capabilities : {};
        const supportedModes = focusModes(safeCapabilities);
        const sanitized = {};
        const discarded = [];

        if (typeof source.focusMode === "string" && supportedModes.includes(source.focusMode)) {
            sanitized.focusMode = source.focusMode;
        } else if (source.focusMode !== undefined) {
            discarded.push("focusMode");
        }

        for (const name of scalarNames) {
            if (source[name] === undefined) continue;
            const value = clampToRange(source[name], safeCapabilities[name]);
            if (value === null) discarded.push(name);
            else sanitized[name] = value;
        }

        const manualMode = manualFocusMode(safeCapabilities);
        if (sanitized.focusDistance !== undefined
            && (!manualMode || (sanitized.focusMode && sanitized.focusMode !== manualMode))) {
            delete sanitized.focusDistance;
            discarded.push("focusDistance");
        }

        return { values: sanitized, discarded: [...new Set(discarded)] };
    }

    function settingsForPersistence(settings, capabilities) {
        const source = settings && typeof settings === "object" ? settings : {};
        const candidate = {};
        for (const name of ["focusMode", ...scalarNames]) {
            if (source[name] !== undefined) candidate[name] = source[name];
        }
        return sanitizeValues(candidate, capabilities).values;
    }

    function readDictionary(storage) {
        try {
            const parsed = JSON.parse(storage?.getItem?.(storageKey) || "{}");
            return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : {};
        } catch {
            return {};
        }
    }

    function savedControls(storage, deviceId) {
        if (!deviceId) return {};
        const saved = readDictionary(storage)[deviceId];
        return saved && typeof saved === "object" && !Array.isArray(saved) ? { ...saved } : {};
    }

    function saveControls(storage, deviceId, values) {
        if (!deviceId || !storage?.setItem) return;
        const dictionary = readDictionary(storage);
        if (values && Object.keys(values).length > 0) dictionary[deviceId] = { ...values };
        else delete dictionary[deviceId];
        storage.setItem(storageKey, JSON.stringify(dictionary));
    }

    function clearControls(storage, deviceId) {
        saveControls(storage, deviceId, {});
    }

    function safeCapabilities(track) {
        try {
            return typeof track?.getCapabilities === "function" ? track.getCapabilities() || {} : {};
        } catch {
            return {};
        }
    }

    function safeSettings(track) {
        try {
            return typeof track?.getSettings === "function" ? track.getSettings() || {} : {};
        } catch {
            return {};
        }
    }

    class CameraControlSession {
        constructor(track) {
            this.track = track;
            this.capabilities = safeCapabilities(track);
            this.pending = {};
            this.flushPromise = null;
        }

        getSettings() {
            return safeSettings(this.track);
        }

        apply(values) {
            const sanitized = sanitizeValues(values, this.capabilities).values;
            Object.assign(this.pending, sanitized);
            if (Object.keys(sanitized).length === 0) return Promise.resolve(this.getSettings());
            if (!this.flushPromise) this.flushPromise = Promise.resolve().then(() => this.flush());
            return this.flushPromise;
        }

        async flush() {
            try {
                while (Object.keys(this.pending).length > 0) {
                    const next = this.pending;
                    this.pending = {};
                    if (typeof this.track?.applyConstraints !== "function") {
                        throw new DOMException("Camera controls are not supported.", "NotSupportedError");
                    }
                    await this.track.applyConstraints({ advanced: [next] });
                }
                return this.getSettings();
            } catch (error) {
                this.pending = {};
                throw error;
            } finally {
                this.flushPromise = null;
            }
        }
    }

    return Object.freeze({
        storageKey,
        normalizeRange,
        clampToRange,
        preferredAutoFocusMode,
        manualFocusMode,
        describeCapabilities,
        sanitizeValues,
        settingsForPersistence,
        savedControls,
        saveControls,
        clearControls,
        safeCapabilities,
        safeSettings,
        CameraControlSession
    });
});
