// Renders the album map with Leaflet + CARTO's Voyager basemap (OpenStreetMap
// data, English/Latin labels). Runs only on pages that
// contain the #map element (the home page); markers come from the generated
// albums/markers.json and link through to their album.
//
// Markers are grouped with Leaflet.markercluster so that several trips near (or
// on top of) each other collapse into a single count badge instead of stacking
// invisibly. Zooming in — or clicking the badge — separates them, and trips at
// the exact same coordinates fan out (spiderfy) so each one is individually
// hoverable and clickable. Hovering a pin shows a tooltip with the trip name,
// date and photo count so you can confirm before clicking through.

(() => {
    const mapElement = document.getElementById("map");

    if (!mapElement) {
        return;
    }

    // Serve Leaflet's default marker images from the locally-hosted copy so the
    // markers render without reaching out to a CDN.
    L.Icon.Default.imagePath = "/lib/leaflet/images/";

    // A castle trip's pin: a small round CSS marker (styled in site.css). Using a
    // divIcon keeps it a local asset with no extra image download.
    const castleIcon = L.divIcon({
        className: "castle-pin",
        html: "<span class=\"castle-pin__dot\"></span>",
        iconSize: [20, 20],
        iconAnchor: [10, 10],
        tooltipAnchor: [0, -10],
    });

    // The route colour a journey falls back to when it has not chosen one.
    const defaultRouteColor = "#0e6e78";

    // A journey waypoint's pin: a small numbered CSS marker (styled in site.css) that
    // reads as a numbered waypoint on the route rather than a trip. A local divIcon,
    // no CDN; the sequence number and the journey's route colour are stamped in per
    // stop so the visit order and which journey it belongs to are both clear.
    //
    // offsetX nudges the badge sideways (in pixels) from the exact point: two journeys
    // that call at the very same port would otherwise stack one pin over the other and
    // hide it, so a shared location fans its pins apart. It shifts the anchor rather
    // than the coordinate, so the spread is constant at every zoom.
    function waypointIconFor(sequence, color, offsetX) {
        const dx = offsetX || 0;

        return L.divIcon({
            className: "route-pin",
            html: "<span class=\"route-pin__num\" style=\"background:" + color + "\">" + sequence + "</span>",
            iconSize: [22, 22],
            iconAnchor: [11 - dx, 11],
            tooltipAnchor: [dx, -11],
        });
    }

    // Two waypoint pins at the same port sit this many pixels apart when fanned out.
    const sharedPortPinSpacing = 30;

    // Geometry endpoints nearer than this (in degrees, ~11m) to a terminal waypoint are
    // treated as already meeting it; farther apart, the route is tied back to the pin.
    const routeTieEpsilon = 1e-4;

    function samePoint(a, b) {
        return Math.abs(a[0] - b[0]) < routeTieEpsilon && Math.abs(a[1] - b[1]) < routeTieEpsilon;
    }

    const map = L.map(mapElement);

    // CARTO's Voyager basemap: OpenStreetMap data, but labelled in English/Latin
    // (endonym-only OSM standard tiles rendered "Danmark", "Deutschland", …). Still
    // keyless, so no secret to configure; the CSP allows the cartocdn tile host.
    L.tileLayer("https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png", {
        subdomains: "abcd",
        maxZoom: 20,
        detectRetina: true,
        attribution: "&copy; <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a> contributors &copy; <a href=\"https://carto.com/attributions\">CARTO</a>",
    }).addTo(map);

    // Trips near (or on top of) each other collapse into a single count badge.
    // The hover ring over a cluster's covered area is noisy for a photo map; the
    // count badge alone communicates "several trips here".
    const cluster = L.markerClusterGroup({ showCoverageOnHover: false });
    map.addLayer(cluster);

    // Journey routes (polylines, waypoint pins and the dotted connectors to their
    // trips) live in a layer per kind, below the clustered trip pins and independent
    // of the trip filter, so the home page can show or hide a whole kind at once.
    const journeyLayers = {};
    let journeyPoints = [];
    let allMarkers = [];
    let journeyRoutes = [];
    let activeFilter = null;

    // Per-kind visibility for the journey layers; a kind defaults to visible until its
    // toggle says otherwise.
    const journeyVisible = {};

    function layerForKind(kind) {
        if (!journeyLayers[kind]) {
            journeyLayers[kind] = L.layerGroup();

            if (journeyVisible[kind] !== false) {
                journeyLayers[kind].addTo(map);
            }
        }

        return journeyLayers[kind];
    }

    // The home-page filters (filters.js) broadcast the active filter; re-plot the
    // pins so the map always matches the filtered trip list. A null detail clears
    // the filter and shows every pin again.
    document.addEventListener("trips:filter", (event) => {
        activeFilter = event.detail;
        render();
    });

    // The home-page per-kind toggles (filters.js) show or hide a kind's routes
    // independently of the trip filter.
    document.addEventListener("journeys:toggle", (event) => {
        const detail = event.detail || {};
        journeyVisible[detail.kind] = detail.show !== false;
        applyJourneyVisibility();
    });

    // The marker file's URL is provided by the server (the photo store): a
    // root-relative /albums/markers.json for local disk, or a CDN/blob URL when
    // content is stored in Azure Blob. Fall back to the local path if absent.
    const markersUrl = mapElement.dataset.markersUrl || "/albums/markers.json";
    const journeysUrl = mapElement.dataset.journeysUrl || "/albums/journeys.json";

    fetch(markersUrl)
        .then((response) => response.json())
        .then((markers) => {
            allMarkers = Array.isArray(markers) ? markers : [];
            return fetchJourneys();
        })
        .then(() => {
            drawJourneys();
            render();
        })
        .catch(() => map.setView([20, 0], 2));

    // Journey routes are best-effort: a missing or malformed journeys file must never
    // stop the trip pins from rendering, so its failure resolves to no routes.
    function fetchJourneys() {
        return fetch(journeysUrl)
            .then((response) => response.json())
            .then((routes) => {
                journeyRoutes = Array.isArray(routes) ? routes : [];
            })
            .catch(() => {
                journeyRoutes = [];
            });
    }

    function render() {
        const shown = allMarkers.filter((marker) => matchesFilter(marker, activeFilter));

        cluster.clearLayers();

        const points = [];

        shown.forEach((marker) => {
            const position = [marker.Lat, marker.Long];

            // Castle trips get a distinct-colour pin (a locally-styled divIcon, so
            // no CDN asset); every other trip keeps Leaflet's default marker.
            const options = marker.Castle ? { icon: castleIcon } : undefined;

            L.marker(position, options)
                .bindTooltip(tooltipFor(marker), { direction: "top", offset: [0, -12] })
                .on("click", () => {
                    window.location.href = "album/" + marker.Slug;
                })
                .addTo(cluster);

            points.push(position);
        });

        // Keep the journey routes in view alongside the (filtered) trip pins, so a
        // route is never scrolled off when the trips it links are the only anchors.
        const boundsPoints = points.concat(journeyPoints);

        if (boundsPoints.length > 0) {
            map.fitBounds(boundsPoints, { padding: [20, 20], maxZoom: 12 });
        } else {
            // No matching trips: show the whole world rather than leaving Leaflet
            // without a view (which would throw on interaction).
            map.setView([20, 0], 2);
        }
    }

    // Draws each journey as a route line through its waypoints, a distinct pin per
    // waypoint, and a dotted connector from a waypoint to each trip done from it (the
    // trip's coordinates resolved by slug from the loaded markers, so a trip's
    // location has a single source of truth). Each journey is drawn into the layer for
    // its kind so a kind can be toggled as a whole. Runs once after both files load.
    function drawJourneys() {
        Object.keys(journeyLayers).forEach((kind) => journeyLayers[kind].clearLayers());
        journeyPoints = [];

        const bySlug = {};
        allMarkers.forEach((marker) => {
            bySlug[marker.Slug] = [marker.Lat, marker.Long];
        });

        // Waypoint pins are collected across every journey first and drawn once the
        // routes are in, so two journeys calling at the same port can be fanned apart
        // (a per-journey pass only sees its own stops and would let one pin hide another).
        const allPins = [];

        journeyRoutes.forEach((journey) => {
            const waypoints = Array.isArray(journey.Waypoints) ? journey.Waypoints : [];
            const color = journey.Color || defaultRouteColor;
            const targetLayer = layerForKind(journey.Kind);

            // A journey with an uploaded route is drawn along that geometry — one
            // polyline per segment, dashed for a travel hop (a flight/transit) and
            // solid for a covered track. Without geometry the line falls back to
            // straight hops between the waypoints.
            const hasGeometry = Array.isArray(journey.Geometry) && journey.Geometry.length > 0;
            const waypointPoints = waypoints.map((w) => [w.Lat, w.Long]);
            const segments = hasGeometry
                ? journey.Geometry
                : [{ Points: waypointPoints, Travel: false }];

            segments.forEach((segment, segmentIndex) => {
                let points = Array.isArray(segment.Points) ? segment.Points.slice() : [];

                // An uploaded sea track can begin and end a little off the port (the
                // ship's channel in and out), leaving the line hanging short of its
                // start/end pin — and a round trip's two open ends never close. Tie the
                // first and last segment back to their terminal waypoints, but only when
                // there is an actual gap, so a track that already meets its port is left
                // exactly as drawn.
                if (hasGeometry && waypointPoints.length > 0 && points.length > 0) {
                    if (segmentIndex === 0 && !samePoint(points[0], waypointPoints[0])) {
                        points.unshift(waypointPoints[0]);
                    }

                    if (segmentIndex === segments.length - 1) {
                        const lastWaypoint = waypointPoints[waypointPoints.length - 1];

                        if (!samePoint(points[points.length - 1], lastWaypoint)) {
                            points.push(lastWaypoint);
                        }
                    }
                }

                if (points.length >= 2) {
                    L.polyline(points, {
                        className: "journey-route",
                        color: color,
                        weight: 3,
                        opacity: 0.85,
                        // The route is a small, curated path — draw it faithfully
                        // rather than let Leaflet simplify vertices away.
                        smoothFactor: 0,
                        dashArray: segment.Travel ? "6 8" : null,
                    }).addTo(targetLayer);
                }
            });

            // Dotted connectors run per waypoint, so every stop's trips reach the map
            // even when two stops share a location.
            waypoints.forEach((waypoint) => {
                const position = [waypoint.Lat, waypoint.Long];
                journeyPoints.push(position);

                (waypoint.Trips || []).forEach((slug) => {
                    const tripPosition = bySlug[slug];

                    if (tripPosition) {
                        L.polyline([position, tripPosition], {
                            className: "journey-connector",
                            color: color,
                            weight: 1.5,
                            opacity: 0.6,
                            dashArray: "3 6",
                        }).addTo(targetLayer);
                    }
                });
            });

            // Waypoint pins. A round-trip journey can call at the same place twice
            // (start and return), which would stack the later pin exactly over the
            // first and hide it. Group the waypoints by coordinate so a shared
            // start/end reads as a single pin badged with both visit numbers ("1 · 6").
            const pinsByLocation = new Map();
            waypoints.forEach((waypoint, index) => {
                const key = waypoint.Lat + "," + waypoint.Long;
                let group = pinsByLocation.get(key);

                if (!group) {
                    group = { position: [waypoint.Lat, waypoint.Long], visits: [] };
                    pinsByLocation.set(key, group);
                }

                group.visits.push(Object.assign({ Seq: index + 1 }, waypoint));
            });

            pinsByLocation.forEach((group, key) => {
                allPins.push({
                    key: key,
                    position: group.position,
                    label: group.visits.map((visit) => visit.Seq).join(" · "),
                    color: color,
                    journey: journey,
                    visits: group.visits,
                    layer: targetLayer,
                });
            });
        });

        // Draw the collected pins. Pins that landed on the same coordinate belong to
        // different journeys (a port two cruises both call at); fan them out so every
        // journey's stop number stays visible instead of the last-drawn pin hiding the
        // rest. A lone pin sits dead on its point.
        const pinsByCoord = new Map();
        allPins.forEach((pin) => {
            let list = pinsByCoord.get(pin.key);

            if (!list) {
                list = [];
                pinsByCoord.set(pin.key, list);
            }

            list.push(pin);
        });

        pinsByCoord.forEach((pins) => {
            const count = pins.length;

            pins.forEach((pin, index) => {
                const offsetX = count > 1 ? (index - (count - 1) / 2) * sharedPortPinSpacing : 0;

                L.marker(pin.position, { icon: waypointIconFor(pin.label, pin.color, offsetX) })
                    .bindTooltip(waypointTooltip(pin.journey, pin.visits), { direction: "top", offset: [offsetX, -11] })
                    .on("click", () => {
                        window.location.href = "journey/" + pin.journey.Slug;
                    })
                    .addTo(pin.layer);
            });
        });

        // Honour any toggles that fired before the layers were populated.
        applyJourneyVisibility();
    }

    // Adds or removes each kind's layer to match its toggle.
    function applyJourneyVisibility() {
        Object.keys(journeyLayers).forEach((kind) => {
            const layer = journeyLayers[kind];

            if (journeyVisible[kind] !== false) {
                if (!map.hasLayer(layer)) {
                    map.addLayer(layer);
                }
            } else if (map.hasLayer(layer)) {
                map.removeLayer(layer);
            }
        });
    }

    // The waypoint hover tooltip: the stop name over a muted date · arrive–depart ·
    // journey-name line, built as DOM text so a name can never inject markup. A stop
    // visited more than once (a round-trip start/end) gets one line per visit,
    // prefixed with its stop number, then the journey name.
    function waypointTooltip(journey, visits) {
        const tip = document.createElement("div");
        tip.className = "map-tip";

        const name = document.createElement("span");
        name.className = "map-tip__name";
        name.textContent = visits[0].Name || journey.Name;
        tip.appendChild(name);

        if (visits.length === 1) {
            const waypoint = visits[0];
            const meta = [];

            if (waypoint.Date) {
                meta.push(waypoint.Date);
            }

            const times = [waypoint.Arrive, waypoint.Depart].filter(Boolean).join("–");
            if (times) {
                meta.push(times);
            }

            if (journey.Name) {
                meta.push(journey.Name);
            }

            if (meta.length > 0) {
                appendMeta(tip, meta.join(" · "));
            }
        } else {
            visits.forEach((waypoint) => {
                const meta = ["Stop " + waypoint.Seq];

                if (waypoint.Date) {
                    meta.push(waypoint.Date);
                }

                const times = [waypoint.Arrive, waypoint.Depart].filter(Boolean).join("–");
                if (times) {
                    meta.push(times);
                }

                appendMeta(tip, meta.join(" · "));
            });

            if (journey.Name) {
                appendMeta(tip, journey.Name);
            }
        }

        return tip;
    }

    // Appends one muted detail line to a map tooltip.
    function appendMeta(tip, text) {
        const detail = document.createElement("span");
        detail.className = "map-tip__meta";
        detail.textContent = text;
        tip.appendChild(detail);
    }

    function matchesFilter(marker, filter) {
        if (!filter) {
            return true;
        }

        if (filter.castleOnly && !marker.Castle) {
            return false;
        }

        if (filter.people && filter.people.length > 0) {
            const on = marker.People || [];

            // Match the card filter: every selected person must be on the trip (AND),
            // and "exact" additionally requires no other people (exact set).
            if (!filter.people.every((person) => on.includes(person))) {
                return false;
            }

            if (filter.exact && on.length !== filter.people.length) {
                return false;
            }
        }

        return true;
    }

    // Builds the hover tooltip as a DOM node (not an HTML string) so an album
    // name is inserted as text and can never inject markup into the page.
    function tooltipFor(marker) {
        const tip = document.createElement("div");
        tip.className = "map-tip";

        const name = document.createElement("span");
        name.className = "map-tip__name";
        name.textContent = marker.Name || marker.Slug;
        tip.appendChild(name);

        const meta = [];
        if (marker.Date) {
            meta.push(marker.Date);
        }
        if (typeof marker.Photos === "number") {
            meta.push(marker.Photos + (marker.Photos === 1 ? " photo" : " photos"));
        }

        if (meta.length > 0) {
            const detail = document.createElement("span");
            detail.className = "map-tip__meta";
            detail.textContent = meta.join(" · ");
            tip.appendChild(detail);
        }

        return tip;
    }
})();
