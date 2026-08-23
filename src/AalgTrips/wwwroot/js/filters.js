// Home-page trip filters. Filters the rendered trip cards by "castle" and by the
// people on the trip, updates the trip count, and broadcasts the active filter as
// a "trips:filter" event so map.js can re-plot the matching pins. Progressive
// enhancement: the filter bar is hidden until this script reveals it, so a no-JS
// visitor never sees controls that would not work.
(() => {
    const filters = document.getElementById("filters");

    if (!filters) {
        return;
    }

    filters.hidden = false;

    // Scope to the Trips section only: journey cards reuse .trip-card for styling but
    // live in their own per-kind sections and must not be hidden or counted by the
    // trip filters — each kind has its own toggle below.
    const cards = Array.from(document.querySelectorAll(".trips .trip-card"));
    const countEl = document.querySelector(".trips .section-head__count");
    const emptyEl = document.getElementById("tripsEmpty");
    const castleBox = filters.querySelector(".filter-castle");
    const personBoxes = Array.from(filters.querySelectorAll(".filter-person"));
    const exactBox = filters.querySelector(".filter-exact");
    const clearBtn = filters.querySelector(".filters__clear");

    function currentFilter() {
        return {
            castleOnly: !!(castleBox && castleBox.checked),
            people: personBoxes.filter((b) => b.checked).map((b) => b.value),
            exact: !!(exactBox && exactBox.checked),
        };
    }

    function cardMatches(card, filter) {
        if (filter.castleOnly && card.dataset.castle !== "true") {
            return false;
        }

        if (filter.people.length > 0) {
            const on = (card.dataset.people || "").split("|").filter(Boolean);

            // Every selected person must have been on the trip (AND): selecting
            // more people narrows the results rather than broadening them.
            if (!filter.people.every((p) => on.includes(p))) {
                return false;
            }

            // "Exact match" additionally requires no other people on the trip, so
            // the trip's people are exactly the selected set (e.g. "trips only I
            // was on").
            if (filter.exact && on.length !== filter.people.length) {
                return false;
            }
        }

        return true;
    }

    function apply() {
        const filter = currentFilter();
        let shown = 0;

        cards.forEach((card) => {
            const show = cardMatches(card, filter);
            card.hidden = !show;

            if (show) {
                shown += 1;
            }
        });

        if (countEl) {
            countEl.textContent = shown + (shown === 1 ? " trip" : " trips");
        }

        if (emptyEl) {
            emptyEl.hidden = shown !== 0;
        }

        const active = filter.castleOnly || filter.people.length > 0;

        if (clearBtn) {
            clearBtn.hidden = !active;
        }

        // A null detail means "no filter" — map.js then shows every pin.
        document.dispatchEvent(new CustomEvent("trips:filter", { detail: active ? filter : null }));
    }

    // Journey toggles: independent of the trip filters. Each toggle shows or hides one
    // kind's card section and — via the journeys:toggle event map.js listens for — that
    // kind's routes on the map, without touching the trip cards or their count.
    const journeyBoxes = Array.from(filters.querySelectorAll(".filter-journeys"));

    journeyBoxes.forEach((box) => {
        const kind = box.dataset.kind;
        const section = document.querySelector(".journeys[data-journey-kind=\"" + kind + "\"]");

        const applyJourneys = () => {
            const show = box.checked;

            if (section) {
                section.hidden = !show;
            }

            document.dispatchEvent(new CustomEvent("journeys:toggle", { detail: { kind: kind, show: show } }));
        };

        box.addEventListener("change", applyJourneys);
        applyJourneys();
    });

    filters.addEventListener("change", apply);

    if (clearBtn) {
        clearBtn.addEventListener("click", () => {
            if (castleBox) {
                castleBox.checked = false;
            }

            if (exactBox) {
                exactBox.checked = false;
            }

            personBoxes.forEach((b) => {
                b.checked = false;
            });

            apply();
        });
    }

    apply();
})();