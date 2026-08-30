// Castle Bingo admin actions (admin only; this script is rendered only for an
// administrator). Ticking a castle off / removing the tick posts to the page's
// Mark / Unmark handlers and reloads; the "+ Album" button opens the shared
// create-album dialog pre-filled with the castle's name and coordinates so a new
// album lands on the castle with the castle flag already set.
(() => {
    const grid = document.getElementById("castleGrid");

    if (!grid) {
        return;
    }

    const tokenInput = document.querySelector("input[name=\"__RequestVerificationToken\"]");
    const token = tokenInput ? tokenInput.value : "";

    async function postAction(handler, castleId) {
        const body = new URLSearchParams();
        body.set("__RequestVerificationToken", token);
        body.set("castleId", castleId);

        const response = await fetch("/castles?handler=" + handler, {
            method: "POST",
            headers: { "RequestVerificationToken": token },
            body: body,
        });

        return response.ok;
    }

    const dialog = document.getElementById("castleAlbumDialog");

    function openCreate(card) {
        if (!dialog) {
            return;
        }

        const name = card.dataset.name || "";
        document.getElementById("castleAlbumName").value = name;
        document.getElementById("castleAlbumLat").value = card.dataset.lat || "";
        document.getElementById("castleAlbumLong").value = card.dataset.long || "";

        const title = document.getElementById("castleAlbumTitle");
        if (title) {
            title.textContent = "New album — " + name;
        }

        if (typeof dialog.showModal === "function") {
            dialog.showModal();
        } else {
            dialog.setAttribute("open", "");
        }
    }

    grid.addEventListener("click", async (event) => {
        const actionButton = event.target.closest("[data-castle-action]");

        if (actionButton) {
            const handler = actionButton.dataset.castleAction === "unmark" ? "Unmark" : "Mark";
            actionButton.disabled = true;

            if (await postAction(handler, actionButton.dataset.castleId)) {
                window.location.reload();
            } else {
                actionButton.disabled = false;
            }

            return;
        }

        const createButton = event.target.closest("[data-castle-create]");
        if (createButton) {
            const card = createButton.closest(".castle-card");
            if (card) {
                openCreate(card);
            }
        }
    });

    if (dialog) {
        dialog.addEventListener("click", (event) => {
            if (event.target.closest("[data-close-dialog]")) {
                dialog.close();
            }
        });
    }
})();
