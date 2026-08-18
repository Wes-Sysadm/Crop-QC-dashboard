(function (root, factory) {
    const api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    root.CropQcCameraControls = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const storageKey = "cropqc.deviceCapture.cameraControls";
    const modeNames = ["exposureMode", "whiteBalanceMode", "focusMode"];
    const scalarNames = [
        "exposureCompensation",
        "exposureTime",
        "colorTemperature",
        "brightness",
        "contrast",
        "saturation",
        "sharpness",
        "iso",
        "focusDistance"
    ];

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

    function supportedModes(capabilities, name) {
        return Array.isArray(capabilities?.[name])
            ? capabilities[name].filter(value => typeof value === "string" && value.trim())
            : [];
    }

    function preferredAutoMode(capabilities, name) {
        const modes = supportedModes(capabilities, name);
        return modes.find(mode => mode.toLowerCase() === "continuous")
            || modes.find(mode => !["manual", "none"].includes(mode.toLowerCase()))
            || null;
    }

    function manualMode(capabilities, name) {
        return supportedModes(capabilities, name).find(mode => mode.toLowerCase() === "manual") || null;
    }

    function preferredAutoFocusMode(capabilities) {
        return preferredAutoMode(capabilities, "focusMode");
    }

    function manualFocusMode(capabilities) {
        return manualMode(capabilities, "focusMode");
    }

    function describeCapabilities(capabilities) {
        const safe = capabilities && typeof capabilities === "object" ? capabilities : {};
        const autoFocusMode = preferredAutoFocusMode(safe);
        const manualFocus = manualFocusMode(safe);
        const autoExposureMode = preferredAutoMode(safe, "exposureMode");
        const manualExposureMode = manualMode(safe, "exposureMode");
        const autoWhiteBalanceMode = preferredAutoMode(safe, "whiteBalanceMode");
        const manualWhiteBalanceMode = manualMode(safe, "whiteBalanceMode");
        const ranges = Object.fromEntries(scalarNames.map(name => [name, normalizeRange(safe[name])]));
        return {
            autoFocusMode,
            manualFocusMode: manualFocus,
            manualFocusSupported: Boolean(manualFocus && ranges.focusDistance),
            autoExposureMode,
            manualExposureMode,
            automaticExposureSupported: Boolean(autoExposureMode),
            manualExposureSupported: Boolean(manualExposureMode && ranges.exposureTime),
            autoWhiteBalanceMode,
            manualWhiteBalanceMode,
            automaticWhiteBalanceSupported: Boolean(autoWhiteBalanceMode),
            manualWhiteBalanceSupported: Boolean(manualWhiteBalanceMode && ranges.colorTemperature),
            ...ranges
        };
    }

    function sanitizeValues(values, capabilities) {
        const source = values && typeof values === "object" ? values : {};
        const safeCapabilities = capabilities && typeof capabilities === "object" ? capabilities : {};
        const sanitized = {};
        const discarded = [];

        for (const name of modeNames) {
            const modes = supportedModes(safeCapabilities, name);
            if (typeof source[name] === "string" && modes.includes(source[name])) {
                sanitized[name] = source[name];
            } else if (source[name] !== undefined) {
                discarded.push(name);
            }
        }

        for (const name of scalarNames) {
            if (source[name] === undefined) continue;
            const value = clampToRange(source[name], safeCapabilities[name]);
            if (value === null) discarded.push(name);
            else sanitized[name] = value;
        }

        const dependencies = [
            ["exposureTime", "exposureMode"],
            ["colorTemperature", "whiteBalanceMode"],
            ["focusDistance", "focusMode"]
        ];
        for (const [valueName, modeName] of dependencies) {
            if (sanitized[valueName] === undefined) continue;
            const manual = manualMode(safeCapabilities, modeName);
            if (!manual || sanitized[modeName] !== manual) {
                delete sanitized[valueName];
                discarded.push(valueName);
            }
        }

        return { values: sanitized, discarded: [...new Set(discarded)] };
    }

    function settingsForPersistence(settings, capabilities) {
        const source = settings && typeof settings === "object" ? settings : {};
        const safeCapabilities = capabilities && typeof capabilities === "object" ? capabilities : {};
        const persisted = {};
        for (const name of modeNames) {
            if (supportedModes(safeCapabilities, name).includes(source[name])) persisted[name] = source[name];
        }
        for (const name of scalarNames) {
            const range = normalizeRange(safeCapabilities[name]);
            const value = asFiniteNumber(source[name]);
            if (!range || value === null) continue;
            persisted[name] = Math.min(range.max, Math.max(range.min, value));
        }
        for (const [valueName, modeName] of [
            ["exposureTime", "exposureMode"],
            ["colorTemperature", "whiteBalanceMode"],
            ["focusDistance", "focusMode"]
        ]) {
            if (persisted[valueName] !== undefined && persisted[modeName] !== manualMode(safeCapabilities, modeName)) {
                delete persisted[valueName];
            }
        }
        return persisted;
    }

    function automaticColorExposureValues(capabilities) {
        const values = {};
        const exposureMode = preferredAutoMode(capabilities, "exposureMode");
        const whiteBalanceMode = preferredAutoMode(capabilities, "whiteBalanceMode");
        if (exposureMode) values.exposureMode = exposureMode;
        if (whiteBalanceMode) values.whiteBalanceMode = whiteBalanceMode;
        return values;
    }

    function lockCurrentColorExposureValues(capabilities, settings) {
        const safeSettings = settings && typeof settings === "object" ? settings : {};
        const description = describeCapabilities(capabilities);
        const values = {};
        const locked = [];
        const unsupported = [];

        const exposureTime = clampToRange(safeSettings.exposureTime, description.exposureTime);
        if (description.manualExposureSupported && exposureTime !== null) {
            values.exposureMode = description.manualExposureMode;
            values.exposureTime = exposureTime;
            locked.push("Exposure");
        } else {
            unsupported.push("Exposure");
        }

        const colorTemperature = clampToRange(safeSettings.colorTemperature, description.colorTemperature);
        if (description.manualWhiteBalanceSupported && colorTemperature !== null) {
            values.whiteBalanceMode = description.manualWhiteBalanceMode;
            values.colorTemperature = colorTemperature;
            locked.push("White balance");
        } else {
            unsupported.push("White balance");
        }

        return { values, locked, unsupported, canLock: locked.length > 0 };
    }

    function friendlyMode(value) {
        if (!value) return "Unknown";
        if (value.toLowerCase() === "manual") return "Manual";
        return "Auto";
    }

    function importantSettingsSnapshot(settings, capabilities) {
        const safe = settings && typeof settings === "object" ? settings : {};
        const description = describeCapabilities(capabilities);
        const snapshot = [];
        if (description.autoExposureMode || description.manualExposureMode) {
            const detail = safe.exposureTime !== undefined ? ` · ${safe.exposureTime}` : "";
            snapshot.push({ label: "Exposure", value: friendlyMode(safe.exposureMode) + detail });
        }
        if (description.autoWhiteBalanceMode || description.manualWhiteBalanceMode) {
            const detail = safe.colorTemperature !== undefined ? ` · ${safe.colorTemperature} K` : "";
            snapshot.push({ label: "White balance", value: friendlyMode(safe.whiteBalanceMode) + detail });
        }
        for (const [name, label] of [["brightness", "Brightness"], ["contrast", "Contrast"], ["saturation", "Saturation"]]) {
            if (description[name] && safe[name] !== undefined) snapshot.push({ label, value: String(safe[name]) });
        }
        return snapshot;
    }

    function cameraDetails(label, settings, capabilities) {
        const description = describeCapabilities(capabilities);
        const supported = [
            ...modeNames.filter(name => supportedModes(capabilities, name).length > 0),
            ...scalarNames.filter(name => description[name])
        ];
        const current = {};
        for (const name of [...modeNames, ...scalarNames]) {
            if (supported.includes(name) && settings?.[name] !== undefined) current[name] = settings[name];
        }
        return { camera: label || "Camera", supportedControls: supported, currentSettings: current };
    }

    async function captureJpegBlob(video, canvas, quality = 0.92) {
        const width = video?.videoWidth || 1280;
        const height = video?.videoHeight || 720;
        canvas.width = width;
        canvas.height = height;
        canvas.getContext("2d").drawImage(video, 0, 0, width, height);
        return new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", quality));
    }

    class TestPhotoBuffer {
        constructor(urlApi) {
            this.urlApi = urlApi;
            this.items = [];
        }

        add(blob, settingsSnapshot) {
            const item = { url: this.urlApi.createObjectURL(blob), settings: settingsSnapshot || [] };
            this.items.push(item);
            while (this.items.length > 2) {
                const removed = this.items.shift();
                this.urlApi.revokeObjectURL(removed.url);
            }
            return this.items.slice();
        }

        clear() {
            for (const item of this.items) this.urlApi.revokeObjectURL(item.url);
            this.items = [];
        }
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
            const candidate = { ...(values || {}) };
            const inferredModes = [];
            const current = this.getSettings();
            for (const [valueName, modeName] of [
                ["exposureTime", "exposureMode"],
                ["colorTemperature", "whiteBalanceMode"],
                ["focusDistance", "focusMode"]
            ]) {
                if (candidate[valueName] !== undefined && candidate[modeName] === undefined && current[modeName] !== undefined) {
                    candidate[modeName] = current[modeName];
                    inferredModes.push(modeName);
                }
            }
            const sanitized = sanitizeValues(candidate, this.capabilities).values;
            for (const modeName of inferredModes) delete sanitized[modeName];
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
        supportedModes,
        preferredAutoMode,
        manualMode,
        preferredAutoFocusMode,
        manualFocusMode,
        describeCapabilities,
        sanitizeValues,
        settingsForPersistence,
        automaticColorExposureValues,
        lockCurrentColorExposureValues,
        importantSettingsSnapshot,
        cameraDetails,
        captureJpegBlob,
        TestPhotoBuffer,
        savedControls,
        saveControls,
        clearControls,
        safeCapabilities,
        safeSettings,
        CameraControlSession
    });
});
