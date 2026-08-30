// Castle Bingo filters. Client-side filtering of the castle grid by country and
// access, plus "unvisited only" and a "show ruins & fragments" toggle that reveals
// the many bare earthworks hidden by default. Keeps the visited/total scoreboard,
// the count and a "show more" pager in step with what is visible. Progressive
// enhancement: the filter bar and the pager stay hidden until this script reveals
// them, and with no JS every castle simply shows.
(() => {
    const filters = document.getElementById("castleFilters");
    const grid = document.getElementById("castleGrid");

    if (!filters || !grid) {
        return;
    }

    filters.hidden = false;

    const cards = Array.from(grid.querySelectorAll(".castle-card"));
    const nationBoxes = Array.from(filters.querySelectorAll(".filter-nation"));
    const accessBoxes = Array.from(filters.querySelectorAll(".filter-access"));
    const unvisitedBox = filters.querySelector(".filter-unvisited");
    const ruinsBox = filters.querySelector(".filter-ruins");

    const scoreDone = document.getElementById("scoreDone");
    const scoreTotal = document.getElementById("scoreTotal");
    const scoreFill = document.getElementById("scoreFill");
    const scoreNote = document.getElementById("scoreNote");
    const countEl = document.getElementById("castleCount");
    const emptyEl = document.getElementById("castleEmpty");
    const moreWrap = document.getElementById("castleMore");
    const moreBtn = document.getElementById("castleMoreBtn");

    // The full grid is thousands of cards, so only this many of the matching set are
    // shown at once; "show more" reveals another page.
    const PAGE_SIZE = 96;
    let cap = PAGE_SIZE;

    function checkedValues(boxes) {
        return new Set(boxes.filter((b) => b.checked).map((b) => b.value));
    }

    function matches(card, nations, tiers, unvisitedOnly, showRuins) {
        const nation = card.dataset.nation || "";

        // A castle with no resolved nation has no country chip to sit under, so it is
        // always shown rather than being permanently filtered out.
        if (nation !== "" && !nations.has(nation)) {
            return false;
        }

        if (!tiers.has(card.dataset.access)) {
            return false;
        }

        if (!showRuins && card.dataset.visitable !== "true") {
            return false;
        }

        if (unvisitedOnly && card.dataset.visited === "true") {
            return false;
        }

        return true;
    }

    function render() {
        const nations = checkedValues(nationBoxes);
        const tiers = checkedValues(accessBoxes);
        const unvisitedOnly = !!(unvisitedBox && unvisitedBox.checked);
        const showRuins = !!(ruinsBox && ruinsBox.checked);

        let matched = 0;
        let visited = 0;

        cards.forEach((card) => {
            if (!matches(card, nations, tiers, unvisitedOnly, showRuins)) {
                card.hidden = true;
                return;
            }

            matched += 1;

            if (card.dataset.visited === "true") {
                visited += 1;
            }

            // Page the matching set: show the first `cap`, keep the rest in the DOM
            // but hidden until "show more".
            card.hidden = matched > cap;
        });

        const pct = matched > 0 ? Math.round((100 * visited) / matched) : 0;

        if (scoreDone) {
            scoreDone.textContent = visited;
        }

        if (scoreTotal) {
            scoreTotal.textContent = matched;
        }

        if (scoreFill) {
            scoreFill.style.width = pct + "%";
        }

        if (scoreNote) {
            scoreNote.textContent = (matched - visited) + " still to visit";
        }

        if (countEl) {
            countEl.textContent = matched + (matched === 1 ? " castle" : " castles");
        }

        if (emptyEl) {
            emptyEl.hidden = matched !== 0;
        }

        if (moreWrap) {
            moreWrap.hidden = matched <= cap;
        }
    }

    filters.addEventListener("change", () => {
        cap = PAGE_SIZE;
        render();
    });

    if (moreBtn) {
        moreBtn.addEventListener("click", () => {
            cap += PAGE_SIZE;
            render();
        });
    }

    render();
})();
