(() => {

    // Dynamic itinerary editor for the journey create / edit forms. The enforced CSP
    // is script-src 'self' (no inline scripts), so this lives in its own file. Each
    // [data-itinerary] block holds the current stop rows, a hidden <template> row,
    // and an "Add stop" button. Rows can be added and removed freely; their form
    // field names (stops[i].*) are renumbered to a contiguous 0..n-1 range whenever
    // the set changes and once more on submit, so ASP.NET Core's collection binder
    // never stops at a gap left by a removed middle row.
    document.querySelectorAll("[data-itinerary]").forEach((editor) => {
        const rows = editor.querySelector("[data-itinerary-rows]");
        const template = editor.querySelector("[data-stop-template]");
        const addButton = editor.querySelector("[data-add-stop]");

        if (!rows) {
            return;
        }

        // Rewrite every stops[...] field name to the row's current DOM position.
        const renumber = () => {
            rows.querySelectorAll("[data-stop-row]").forEach((row, index) => {
                row.querySelectorAll("[name]").forEach((field) => {
                    field.name = field.name.replace(/stops\[[^\]]*\]/, `stops[${index}]`);
                });
            });
        };

        // A day at sea has no coordinates, so disable (and clear) the lat/long inputs
        // while it is ticked — the server also clears them, this is just the cue.
        const wireRow = (row) => {
            const remove = row.querySelector("[data-remove-stop]");

            if (remove) {
                remove.addEventListener("click", () => {
                    row.remove();
                    renumber();
                });
            }

            const atSea = row.querySelector("[data-atsea]");
            const seaDisabled = [
                row.querySelector("[data-lat]"),
                row.querySelector("[data-long]"),
                row.querySelector("[data-trips]"),
            ];

            if (atSea) {
                // A day at sea has no port, so it has no coordinates and no trips.
                // Disable those inputs while it is ticked but keep their values, so
                // unticking restores whatever was there rather than losing it. A
                // disabled input is not submitted, and the server clears an at-sea
                // stop's coordinates anyway, so nothing stale is saved.
                const sync = () => {
                    seaDisabled.forEach((field) => {
                        if (field) {
                            field.disabled = atSea.checked;
                        }
                    });

                    row.classList.toggle("stop-row--sea", atSea.checked);
                };

                atSea.addEventListener("change", sync);
                sync();
            }
        };

        rows.querySelectorAll("[data-stop-row]").forEach(wireRow);

        if (addButton && template) {
            addButton.addEventListener("click", () => {
                const clone = template.content.firstElementChild.cloneNode(true);
                rows.appendChild(clone);
                wireRow(clone);
                renumber();
            });
        }

        // Close any gaps one final time just before the form is serialized.
        const form = editor.closest("form");

        if (form) {
            form.addEventListener("submit", renumber);
        }
    });

    // Per-stop photo upload. One shared modal is reused for every stop: clicking a
    // stop's "Add photos" button stamps that stop's key into the form's hidden field
    // and opens the dialog, so the upload posts against the right day.
    const uploadDialog = document.querySelector("#uploadStopDialog");
    const uploadKeyField = document.querySelector("#uploadStopKey");

    if (uploadDialog && uploadKeyField) {
        document.querySelectorAll("[data-upload-stop]").forEach((trigger) => {
            trigger.addEventListener("click", () => {
                uploadKeyField.value = trigger.getAttribute("data-upload-stop");

                if (typeof uploadDialog.showModal === "function") {
                    uploadDialog.showModal();
                }
            });
        });
    }

    // Confirm before deleting a journey, mirroring the album delete guard.
    const deleteJourney = document.querySelector("#deletejourney");

    if (deleteJourney) {
        deleteJourney.addEventListener("click", (e) => {
            if (!confirm("Are you sure you want to delete this journey?")) {
                e.preventDefault();
            }
        }, false);
    }
})();