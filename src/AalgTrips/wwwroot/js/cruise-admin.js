(() => {

    // Dynamic itinerary editor for the cruise create / edit forms. The enforced CSP
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
            const lat = row.querySelector("[data-lat]");
            const long = row.querySelector("[data-long]");

            if (atSea) {
                const sync = () => {
                    [lat, long].forEach((field) => {
                        if (!field) {
                            return;
                        }

                        field.disabled = atSea.checked;

                        if (atSea.checked) {
                            field.value = "";
                        }
                    });
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

    // Confirm before deleting a cruise, mirroring the album delete guard.
    const deleteCruise = document.querySelector("#deletecruise");

    if (deleteCruise) {
        deleteCruise.addEventListener("click", (e) => {
            if (!confirm("Are you sure you want to delete this cruise?")) {
                e.preventDefault();
            }
        }, false);
    }
})();