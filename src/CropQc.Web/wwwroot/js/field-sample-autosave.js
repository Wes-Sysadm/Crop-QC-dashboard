(function () {
    "use strict";

    window.initializeFieldSampleAutosave = function initializeFieldSampleAutosave(options) {
        const rowsForm = document.querySelector("[data-field-rows-form]");
        if (!rowsForm || rowsForm.dataset.autosaveInitialized === "true") return;
        rowsForm.dataset.autosaveInitialized = "true";

        const metadataForm = document.querySelector("[data-field-metadata-form]");
        const statusLabel = document.querySelector("[data-autosave-label]");
        const statusTime = document.querySelector("[data-autosave-time]");
        const errorPanel = document.querySelector("[data-autosave-errors]");
        const updateWarning = document.querySelector("[data-field-server-update]");
        const targetSizeInput = rowsForm.querySelector("[data-field-target-size]");
        const token = rowsForm.querySelector("input[name='__RequestVerificationToken']")?.value || "";
        const storageKey = `cropqc.fieldSample.autosave.${options.sampleId}`;
        const debounceMs = options.debounceMilliseconds || 1000;
        const thresholds = Array.isArray(options.sizeThresholds) ? options.sizeThresholds : [];
        let pending = { metadata: {}, rows: {}, targetSampleSize: null, source: "Browser" };
        let timer = null;
        let saving = false;
        let retryTimer = null;
        let storageSafe = true;
        let bypassSubmit = false;

        function normalize(value) {
            return value === null || value === undefined || value === "" ? null : String(value);
        }

        function sameSubmittedChange(current, submitted) {
            return current
                && normalize(current.value) === normalize(submitted.value)
                && normalize(current.originalValue) === normalize(submitted.originalValue);
        }

        function controlValue(control) {
            return control.type === "checkbox" ? String(control.checked) : normalize(control.value);
        }

        function setControlValue(control, value) {
            if (!control) return;
            if (control.type === "checkbox") control.checked = String(value).toLowerCase() === "true";
            else control.value = value ?? "";
        }

        function setStatus(label, detail) {
            if (statusLabel) statusLabel.textContent = label;
            if (statusTime && detail !== undefined) statusTime.textContent = detail || "";
        }

        function hasPending() {
            return Object.keys(pending.metadata).length > 0
                || Object.values(pending.rows).some(row => Object.keys(row.changes || {}).length > 0)
                || pending.targetSampleSize !== null;
        }

        function persistPending() {
            try {
                if (hasPending()) localStorage.setItem(storageKey, JSON.stringify(pending));
                else localStorage.removeItem(storageKey);
                storageSafe = true;
            } catch {
                storageSafe = false;
            }
        }

        function loadPending() {
            try {
                const saved = localStorage.getItem(storageKey);
                if (saved) pending = JSON.parse(saved);
            } catch {
                storageSafe = false;
            }
            pending.metadata ||= {};
            pending.rows ||= {};
            pending.source ||= "Browser";
        }

        function fieldControl(scope, rowNumber, field) {
            if (scope === "metadata") return document.querySelector(`[data-autosave-field='${CSS.escape(field)}']`);
            const row = document.querySelector(`.fruit-row[data-row-number='${rowNumber}']`);
            if (field === "DefectTypeIds") return row?.querySelector("[data-defect-hidden-inputs]");
            return row?.querySelector(`[data-autosave-row-field='${CSS.escape(field)}']`);
        }

        function initializePersistedValues(root) {
            root?.querySelectorAll("[data-autosave-field], [data-autosave-row-field]").forEach(control => {
                if (control.dataset.persistedValue === undefined) control.dataset.persistedValue = controlValue(control) ?? "";
            });
            root?.querySelectorAll(".fruit-row").forEach(row => {
                const ids = selectedDefectIds(row);
                row.dataset.persistedDefectIds = ids.join(",");
            });
        }

        function selectedDefectIds(row) {
            return Array.from(row?.querySelectorAll("[data-defect-hidden-inputs] input") || [])
                .map(input => Number(input.value)).filter(value => Number.isInteger(value) && value > 0).sort((a, b) => a - b);
        }

        function selectedDefectNames(row) {
            const ids = new Set(selectedDefectIds(row).map(String));
            return Array.from(row?.querySelectorAll("[data-field-defect-id]") || [])
                .filter(button => ids.has(button.dataset.fieldDefectId)).map(button => button.dataset.defectName);
        }

        function updateDefectUi(row) {
            const ids = selectedDefectIds(row);
            const names = selectedDefectNames(row);
            const inspected = row.querySelector("[data-autosave-row-field='DefectsInspected']")?.checked === true;
            const selected = row.querySelector("[data-selected-defects]");
            if (selected) {
                selected.innerHTML = "";
                names.forEach(name => {
                    const chip = document.createElement("span");
                    chip.className = "chip";
                    chip.textContent = name;
                    selected.appendChild(chip);
                });
            }
            const indicator = row.querySelector("[data-defect-indicator]");
            if (indicator) indicator.textContent = !inspected ? "Not inspected" : ids.length === 0 ? "Inspected — none" : `${ids.length} defect(s)`;
            const otherId = Array.from(row.querySelectorAll("[data-field-defect-id]")).find(button => button.dataset.defectName === "Other")?.dataset.fieldDefectId;
            const notes = row.querySelector("[data-autosave-row-field='OtherDefectNotes']");
            if (notes) notes.disabled = !otherId || !ids.includes(Number(otherId));
        }

        function recordMetadata(control, immediate) {
            const field = control.dataset.autosaveField;
            pending.metadata[field] = {
                field,
                value: controlValue(control),
                originalValue: normalize(control.dataset.persistedValue)
            };
            pending.source = control.dataset.changeSource || "Browser";
            delete control.dataset.changeSource;
            queueSave(immediate);
        }

        function recordRow(control, immediate) {
            const row = control.closest(".fruit-row");
            if (!row) return;
            const rowNumber = Number(row.dataset.rowNumber);
            const field = control.dataset.autosaveRowField;
            const rowState = pending.rows[rowNumber] ||= { rowNumber, fieldVersion: Number(row.dataset.fieldVersion || 0), changes: {} };
            rowState.changes[field] = {
                field,
                value: controlValue(control),
                originalValue: normalize(control.dataset.persistedValue)
            };
            pending.source = control.dataset.changeSource || "Browser";
            delete control.dataset.changeSource;
            if (field === "WeightGrams") previewSize(row, control.value);
            if (field === "DefectsInspected") updateDefectUi(row);
            queueSave(immediate || pending.source === "Scale");
        }

        function recordDefects(row, immediate) {
            const rowNumber = Number(row.dataset.rowNumber);
            const rowState = pending.rows[rowNumber] ||= { rowNumber, fieldVersion: Number(row.dataset.fieldVersion || 0), changes: {} };
            rowState.changes.DefectTypeIds = {
                field: "DefectTypeIds",
                value: selectedDefectIds(row).join(","),
                originalValue: normalize(row.dataset.persistedDefectIds)
            };
            const inspected = row.querySelector("[data-autosave-row-field='DefectsInspected']");
            if (selectedDefectIds(row).length > 0 && inspected && !inspected.checked) {
                inspected.checked = true;
                recordRow(inspected, false);
            }
            updateDefectUi(row);
            queueSave(immediate);
        }

        function previewSize(row, rawWeight) {
            const output = row.querySelector("[data-size-category]");
            if (!output) return;
            if (rawWeight === "" || rawWeight === null) {
                output.textContent = "Not calculated";
                return;
            }
            const weight = Number(rawWeight);
            if (!Number.isFinite(weight)) return;
            const match = thresholds.filter(item => weight >= Number(item.minimumWeightGrams))
                .sort((a, b) => Number(b.minimumWeightGrams) - Number(a.minimumWeightGrams))[0];
            output.textContent = match ? String(match.sizeCategory) : "Not calculated";
        }

        function queueSave(immediate) {
            persistPending();
            setStatus(navigator.onLine ? "Unsaved changes" : "Offline — changes waiting", "");
            clearTimeout(timer);
            timer = setTimeout(() => flush(), immediate ? 0 : debounceMs);
        }

        function requestBody(sourceOverride) {
            return {
                changeId: crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`,
                source: sourceOverride || pending.source || "Browser",
                targetSampleSize: pending.targetSampleSize,
                metadataChanges: Object.values(pending.metadata),
                rowChanges: Object.values(pending.rows).map(row => ({
                    rowNumber: row.rowNumber,
                    fieldVersion: row.fieldVersion,
                    changes: Object.values(row.changes || {})
                })).filter(row => row.changes.length > 0)
            };
        }

        function showValidation(result) {
            if (!errorPanel) return;
            const messages = (result.validationErrors || []).map(error => `${error.rowNumber ? `Fruit ${error.rowNumber} ` : ""}${error.field}: ${error.message}`);
            if (result.error) messages.push(result.error);
            errorPanel.textContent = messages.join(" ");
            errorPanel.hidden = messages.length === 0;
        }

        function showConflicts(conflicts) {
            if (!errorPanel) return;
            errorPanel.innerHTML = "";
            const heading = document.createElement("strong");
            heading.textContent = "Conflict detected. Your value is preserved until you choose.";
            errorPanel.appendChild(heading);
            conflicts.forEach(conflict => {
                const line = document.createElement("div");
                line.className = "button-row autosave-conflict";
                const text = document.createElement("span");
                text.textContent = `${conflict.rowNumber ? `Fruit ${conflict.rowNumber} ` : ""}${conflict.field}: server “${conflict.serverValue ?? "blank"}”, yours “${conflict.clientValue ?? "blank"}”.`;
                const server = document.createElement("button");
                server.type = "button";
                server.className = "secondary-button";
                server.textContent = "Use server value";
                server.addEventListener("click", () => resolveConflict(conflict, false));
                const mine = document.createElement("button");
                mine.type = "button";
                mine.textContent = "Keep my value";
                mine.addEventListener("click", () => resolveConflict(conflict, true));
                line.append(text, server, mine);
                errorPanel.appendChild(line);
            });
            errorPanel.hidden = false;
        }

        function resolveConflict(conflict, keepClient) {
            const control = fieldControl(conflict.scope, conflict.rowNumber, conflict.field);
            const collection = conflict.scope === "metadata" ? pending.metadata : pending.rows[conflict.rowNumber]?.changes;
            const change = collection?.[conflict.field];
            if (keepClient && change) {
                change.originalValue = normalize(conflict.serverValue);
                pending.source = "Conflict Resolution";
                queueSave(true);
            } else {
                if (control && conflict.field !== "DefectTypeIds") {
                    setControlValue(control, conflict.serverValue);
                    control.dataset.persistedValue = conflict.serverValue ?? "";
                }
                if (conflict.scope === "row" && conflict.field === "DefectTypeIds") {
                    applyDefectIds(control?.closest?.(".fruit-row") || document.querySelector(`.fruit-row[data-row-number='${conflict.rowNumber}']`), conflict.serverValue);
                }
                if (change) {
                    change.value = normalize(conflict.serverValue);
                    change.originalValue = normalize(conflict.serverValue);
                    pending.source = "Conflict Resolution";
                    queueSave(true);
                } else {
                    cleanupPending();
                    persistPending();
                    errorPanel.hidden = true;
                    setStatus(hasPending() ? "Unsaved changes" : "Saved", "");
                }
            }
        }

        function cleanupPending() {
            Object.keys(pending.rows).forEach(key => {
                if (Object.keys(pending.rows[key].changes || {}).length === 0) delete pending.rows[key];
            });
        }

        function applyDefectIds(row, value) {
            if (!row) return;
            const holder = row.querySelector("[data-defect-hidden-inputs]");
            if (!holder) return;
            holder.innerHTML = "";
            String(value || "").split(",").filter(Boolean).forEach(id => {
                const input = document.createElement("input");
                input.type = "hidden";
                input.name = `Rows[${row.dataset.rowIndex}].DefectTypeIds`;
                input.value = id;
                holder.appendChild(input);
            });
            row.dataset.persistedDefectIds = selectedDefectIds(row).join(",");
            updateDefectUi(row);
        }

        function savedRowValue(saved, field, fallback) {
            const serverValues = {
                Pressure1Lbs: saved.pressure1Lbs,
                Pressure2Lbs: saved.pressure2Lbs,
                WeightGrams: saved.weightGrams,
                StarchScaleValueId: saved.starchScaleValueId,
                GradeId: saved.gradeId,
                DefectsInspected: saved.defectsInspected,
                DefectTypeIds: (saved.defectTypeIds || []).slice().sort((a, b) => a - b).join(","),
                OtherDefectNotes: saved.otherDefectNotes
            };
            return serverValues[field] ?? fallback;
        }

        function applySavedRows(rows, sentBody) {
            for (const saved of rows || []) {
                const row = document.querySelector(`.fruit-row[data-row-number='${saved.rowNumber}']`);
                if (!row) continue;
                row.dataset.fieldVersion = String(saved.fieldVersion || 0);
                const sent = sentBody.rowChanges.find(item => item.rowNumber === saved.rowNumber);
                for (const change of sent?.changes || []) {
                    const control = fieldControl("row", saved.rowNumber, change.field);
                    const serverValue = savedRowValue(saved, change.field, change.value);
                    const current = pending.rows[saved.rowNumber]?.changes?.[change.field];
                    if (sameSubmittedChange(current, change)) {
                        if (change.field === "DefectTypeIds") applyDefectIds(row, serverValue);
                        else if (control) setControlValue(control, serverValue);
                    } else if (current) {
                        current.originalValue = normalize(serverValue);
                    }
                    if (control && change.field !== "DefectTypeIds") control.dataset.persistedValue = normalize(serverValue) ?? "";
                }
                if (!pending.rows[saved.rowNumber]?.changes?.WeightGrams) {
                    row.querySelector("[data-size-category]")?.replaceChildren(document.createTextNode(saved.sizeCategory ?? "Not calculated"));
                }
                if (!pending.rows[saved.rowNumber]?.changes?.Pressure1Lbs && !pending.rows[saved.rowNumber]?.changes?.Pressure2Lbs) {
                    row.querySelector("[data-pressure-average]")?.replaceChildren(document.createTextNode(saved.pressureAverageLbs ?? ""));
                }
            }
        }

        async function flush(sourceOverride) {
            clearTimeout(timer);
            if (saving) return false;
            if (!hasPending()) return true;
            if (!navigator.onLine) {
                setStatus("Offline — changes waiting", "Will retry when the connection returns");
                persistPending();
                return false;
            }
            saving = true;
            const body = requestBody(sourceOverride);
            setStatus("Saving…", "");
            try {
                const response = await fetch(rowsForm.dataset.autosaveUrl, {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
                    body: JSON.stringify(body)
                });
                const result = await response.json();
                if (response.status === 409) {
                    showConflicts(result.conflicts || []);
                    setStatus("Conflict detected", "Resolve the highlighted values before completing or sending");
                    persistPending();
                    return false;
                }
                if (!response.ok || !result.saved) {
                    showValidation(result);
                    setStatus("Save failed — retrying", "Entered values remain on this page");
                    persistPending();
                    scheduleRetry();
                    return false;
                }
                applySavedRows(result.rows, body);
                for (const change of body.metadataChanges) {
                    const control = fieldControl("metadata", null, change.field);
                    const serverValue = result.metadataValues?.[change.field] ?? change.value;
                    const current = pending.metadata[change.field];
                    if (sameSubmittedChange(current, change)) {
                        if (control) setControlValue(control, serverValue);
                        delete pending.metadata[change.field];
                    } else if (current) {
                        current.originalValue = normalize(serverValue);
                    }
                    if (control) control.dataset.persistedValue = normalize(serverValue) ?? "";
                }
                for (const row of body.rowChanges) {
                    const rowState = pending.rows[row.rowNumber];
                    row.changes.forEach(change => {
                        if (rowState && sameSubmittedChange(rowState.changes[change.field], change)) delete rowState.changes[change.field];
                    });
                }
                if (body.targetSampleSize !== null) pending.targetSampleSize = null;
                cleanupPending();
                errorPanel && (errorPanel.hidden = true);
                persistPending();
                const savedAt = result.savedAt ? new Date(result.savedAt) : new Date();
                setStatus(hasPending() ? "Unsaved changes" : "Saved", `Last saved ${savedAt.toLocaleTimeString()}`);
                if (hasPending()) queueSave(false);
                return !hasPending();
            } catch {
                setStatus(navigator.onLine ? "Save failed — retrying" : "Offline — changes waiting", "Entered values are queued on this browser");
                persistPending();
                scheduleRetry();
                return false;
            } finally {
                saving = false;
            }
        }

        function scheduleRetry() {
            clearTimeout(retryTimer);
            retryTimer = setTimeout(() => flush(), 5000);
        }

        function restorePending() {
            const restoredTarget = Number(pending.targetSampleSize || targetSizeInput?.value || 0);
            while (rowsForm.querySelectorAll("tr.fruit-row").length < restoredTarget) cloneRow(false);
            Object.values(pending.metadata).forEach(change => setControlValue(fieldControl("metadata", null, change.field), change.value));
            Object.values(pending.rows).forEach(rowState => {
                Object.values(rowState.changes || {}).forEach(change => {
                    const control = fieldControl("row", rowState.rowNumber, change.field);
                    if (change.field === "DefectTypeIds") applyDefectIds(document.querySelector(`.fruit-row[data-row-number='${rowState.rowNumber}']`), change.value);
                    else setControlValue(control, change.value);
                });
            });
            if (hasPending()) setStatus(navigator.onLine ? "Unsaved changes" : "Offline — changes waiting", "Recovered pending changes from this browser");
        }

        function updateTerminology() {
            const select = document.querySelector("[data-fruit-profile]");
            if (!select) return;
            const fruitType = select.options[select.selectedIndex]?.dataset.fruitType || "";
            const pear = fruitType.toLowerCase() === "pear";
            const apple = fruitType.toLowerCase() === "apple";
            const whole = pear ? "Whole Pear Sample" : apple ? "Whole Apple Sample" : "Whole Fruit Sample";
            const cut = pear ? "Cut Pear" : apple ? "Cut Apple" : "Cut Fruit";
            document.querySelectorAll("[data-device-capture]").forEach(panel => {
                panel.dataset.wholeSampleLabel = whole;
                panel.dataset.cutFruitLabel = cut;
            });
            document.querySelectorAll("[data-fruit-camera-label]").forEach(element => element.textContent = pear ? "Pear camera" : apple ? "Apple camera" : "Fruit camera");
            document.querySelectorAll("[data-whole-sample-action]").forEach(element => element.textContent = `Capture ${whole} Photo`);
            document.querySelectorAll("[data-cut-fruit-action]").forEach(element => element.textContent = `Capture ${cut} Photo`);
            document.querySelectorAll(".photo-type option[value='SampleBeforeCutting']").forEach(element => element.textContent = whole);
            document.querySelectorAll(".photo-type option[value='CutFruit']").forEach(element => element.textContent = cut);
        }

        function cloneRow(queueChange = true) {
            const tbody = rowsForm.querySelector("tbody");
            const rows = Array.from(tbody.querySelectorAll("tr.fruit-row"));
            if (rows.length >= 50) return;
            const source = rows[rows.length - 1];
            const row = source.cloneNode(true);
            const index = rows.length;
            const rowNumber = index + 1;
            row.dataset.rowIndex = String(index);
            row.dataset.rowNumber = String(rowNumber);
            row.dataset.fieldVersion = "0";
            row.querySelector("td").firstChild.textContent = String(rowNumber);
            row.querySelectorAll("[name]").forEach(control => {
                control.name = control.name.replace(/Rows\[\d+\]/, `Rows[${index}]`);
                if (control.name.endsWith(".RowNumber")) control.value = String(rowNumber);
                else if (control.type === "checkbox") control.checked = false;
                else control.value = "";
                delete control.dataset.persistedValue;
            });
            row.querySelector("[data-defect-hidden-inputs]").innerHTML = "";
            row.querySelector("[data-size-category]").textContent = "Not calculated";
            row.querySelector("[data-pressure-average]").textContent = "";
            row.dataset.persistedDefectIds = "";
            rows.forEach(item => item.classList.remove("device-selected-row"));
            row.classList.add("device-selected-row");
            tbody.appendChild(row);
            initializePersistedValues(row);
            updateDefectUi(row);
            targetSizeInput.value = String(rowNumber);
            if (queueChange) {
                pending.targetSampleSize = rowNumber;
                queueSave(true);
            }
        }

        rowsForm.addEventListener("input", event => {
            const control = event.target.closest?.("[data-autosave-row-field]");
            if (control) recordRow(control, false);
        });
        rowsForm.addEventListener("change", event => {
            const control = event.target.closest?.("[data-autosave-row-field]");
            if (control) recordRow(control, true);
        });
        metadataForm?.addEventListener("input", event => {
            const control = event.target.closest?.("[data-autosave-field]");
            if (control) recordMetadata(control, false);
        });
        metadataForm?.addEventListener("change", event => {
            const control = event.target.closest?.("[data-autosave-field]");
            if (control) {
                recordMetadata(control, true);
                if (control.matches("[data-fruit-profile]")) updateTerminology();
            }
        });
        document.addEventListener("click", event => {
            const defect = event.target.closest?.("[data-field-defect-id]");
            if (defect) {
                const row = defect.closest(".fruit-row");
                const holder = row.querySelector("[data-defect-hidden-inputs]");
                const existing = holder.querySelector(`input[value='${defect.dataset.fieldDefectId}']`);
                if (existing) existing.remove();
                else {
                    const input = document.createElement("input");
                    input.type = "hidden";
                    input.name = `Rows[${row.dataset.rowIndex}].DefectTypeIds`;
                    input.value = defect.dataset.fieldDefectId;
                    holder.appendChild(input);
                }
                recordDefects(row, true);
            }
        });
        document.querySelector("[data-add-field-row]")?.addEventListener("click", () => cloneRow(true));
        document.querySelectorAll("[data-save-now]").forEach(button => button.addEventListener("click", () => flush("Manual Save Now")));

        for (const form of [rowsForm, metadataForm].filter(Boolean)) {
            form.addEventListener("submit", event => {
                if (bypassSubmit) return;
                event.preventDefault();
                flush("Manual Save Now");
            });
        }

        document.querySelectorAll("[data-requires-autosave]").forEach(element => {
            element.addEventListener("click", async event => {
                if (!hasPending()) return;
                event.preventDefault();
                const ok = await flush();
                if (!ok) return;
                if (element.tagName === "A") window.location.assign(element.href);
                else {
                    bypassSubmit = true;
                    element.requestSubmit();
                }
            });
            if (element.tagName === "FORM") {
                element.addEventListener("submit", async event => {
                    if (bypassSubmit || !hasPending()) return;
                    event.preventDefault();
                    const ok = await flush();
                    if (ok) { bypassSubmit = true; element.requestSubmit(); }
                });
            }
        });

        async function refreshFieldSample() {
            try {
                const response = await fetch(options.refreshUrl, { credentials: "same-origin", cache: "no-store" });
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                const data = await response.json();
                updateStationStatus(data.qcStation);
                mergeServerRows(data.rows || []);
                if (updateWarning) updateWarning.hidden = true;
            } catch (error) {
                updateStationStatus({ state: "Error", message: `QC Station status check failed (${error.message}). Check dashboard connectivity, then retry.` });
            }
        }

        function updateStationStatus(station) {
            const state = document.querySelector("[data-qc-station-state]");
            if (state) state.dataset.qcStationState = station?.state || "Error";
            document.querySelector("[data-qc-station-label]")?.replaceChildren(document.createTextNode(station?.state || "Error"));
            document.querySelector("[data-qc-station-message]")?.replaceChildren(document.createTextNode(station?.message || "QC Station status could not be checked."));
        }

        function mergeServerRows(rows) {
            rows.forEach(saved => {
                const row = document.querySelector(`.fruit-row[data-row-number='${saved.rowNumber}']`);
                if (!row) return;
                for (const field of ["Pressure1Lbs", "Pressure2Lbs", "WeightGrams", "StarchScaleValueId", "GradeId", "DefectsInspected"]) {
                    if (pending.rows[saved.rowNumber]?.changes?.[field]) continue;
                    const control = fieldControl("row", saved.rowNumber, field);
                    const key = field.charAt(0).toLowerCase() + field.slice(1);
                    if (control) {
                        setControlValue(control, saved[key]);
                        control.dataset.persistedValue = normalize(saved[key]) ?? "";
                    }
                }
                if (!pending.rows[saved.rowNumber]?.changes?.DefectTypeIds) applyDefectIds(row, (saved.defectTypeIds || []).join(","));
                if (!pending.rows[saved.rowNumber]?.changes?.WeightGrams) row.querySelector("[data-size-category]").textContent = saved.sizeCategory ?? "Not calculated";
                row.querySelector("[data-pressure-average]").textContent = saved.pressureAverageLbs ?? "";
                row.dataset.fieldVersion = String(saved.fieldVersion || 0);
            });
        }

        document.querySelector("[data-refresh-field-sample]")?.addEventListener("click", refreshFieldSample);
        window.addEventListener("online", () => flush());
        window.addEventListener("offline", () => setStatus("Offline — changes waiting", "Entered values are queued on this browser"));
        window.addEventListener("beforeunload", event => {
            persistPending();
            if (hasPending() && !storageSafe) { event.preventDefault(); event.returnValue = ""; }
        });

        window.fieldSampleAutosave = { flush, hasPending };
        initializePersistedValues(document);
        loadPending();
        restorePending();
        updateTerminology();
        document.querySelectorAll(".fruit-row").forEach(updateDefectUi);
        setInterval(refreshFieldSample, 3000);
        setInterval(() => { if (hasPending()) flush(); }, 5000);
    };
})();
