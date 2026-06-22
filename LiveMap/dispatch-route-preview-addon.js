/*
OverWatch ELD Dispatch Route Preview Add-on
Safe overlay layer for LiveMap/index.html.
*/

(function () {
    const styleId = "oweld-route-preview-style";
    let routeLayers = [];

    function ensureStyle() {
        if (document.getElementById(styleId)) return;

        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = `
            .oweld-route-panel {
                position: absolute;
                left: 18px;
                bottom: 18px;
                width: 340px;
                max-width: calc(100vw - 36px);
                max-height: 44vh;
                overflow: auto;
                background: #0D1A2B;
                border: 1px solid #263E5C;
                border-radius: 14px;
                color: #EAF2FF;
                box-shadow: 0 18px 44px rgba(0,0,0,.38);
                z-index: 9998;
                font-family: Segoe UI, Arial, sans-serif;
            }

            .oweld-route-panel-header {
                position: sticky;
                top: 0;
                background: #07111F;
                border-bottom: 1px solid #263E5C;
                padding: 10px 12px;
                font-weight: 800;
            }

            .oweld-route-item {
                padding: 10px 12px;
                border-bottom: 1px solid rgba(38,62,92,.65);
                cursor: pointer;
            }

            .oweld-route-item:hover {
                background: #132238;
            }

            .oweld-route-load {
                font-weight: 800;
            }

            .oweld-route-meta {
                color: #9FB3CC;
                font-size: 12px;
                margin-top: 3px;
            }

            .oweld-route-pill {
                display: inline-block;
                background: #163B65;
                border: 1px solid #4A91D0;
                color: white;
                border-radius: 999px;
                padding: 2px 7px;
                font-size: 11px;
                font-weight: 700;
                margin-top: 5px;
            }
        `;
        document.head.appendChild(style);
    }

    function firstNonEmpty() {
        for (let i = 0; i < arguments.length; i++) {
            const value = arguments[i];
            if (value !== null && value !== undefined && String(value).trim() !== "") {
                return String(value).trim();
            }
        }
        return "";
    }

    function enc(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function getLatLng(route, prefix) {
        const lat = route[prefix + "Latitude"] ?? route[prefix.toLowerCase() + "Latitude"];
        const lng = route[prefix + "Longitude"] ?? route[prefix.toLowerCase() + "Longitude"];

        if (lat === null || lat === undefined || lng === null || lng === undefined) return null;

        const nLat = Number(lat);
        const nLng = Number(lng);

        if (!Number.isFinite(nLat) || !Number.isFinite(nLng)) return null;

        return [nLat, nLng];
    }

    function clearRoutes(map) {
        routeLayers.forEach(layer => {
            try {
                if (map && typeof map.removeLayer === "function") map.removeLayer(layer);
                else if (layer && typeof layer.remove === "function") layer.remove();
            } catch { }
        });

        routeLayers = [];
    }

    function drawRoute(map, route) {
        if (!map || typeof L === "undefined") return;

        clearRoutes(map);

        const origin = getLatLng(route, "origin");
        const destination = getLatLng(route, "destination");

        if (!origin || !destination) return;

        const line = L.polyline([origin, destination], {
            color: "#4A91D0",
            weight: 4,
            opacity: 0.92,
            dashArray: "8 8"
        }).addTo(map);

        const start = L.circleMarker(origin, {
            radius: 7,
            color: "#35B474",
            weight: 3,
            fillColor: "#1F7A4D",
            fillOpacity: 0.95
        }).addTo(map);

        const end = L.circleMarker(destination, {
            radius: 7,
            color: "#FFB454",
            weight: 3,
            fillColor: "#B7791F",
            fillOpacity: 0.95
        }).addTo(map);

        start.bindPopup(`<b>Pickup</b><br>${enc(firstNonEmpty(route.originCity, route.OriginCity))}`);
        end.bindPopup(`<b>Destination</b><br>${enc(firstNonEmpty(route.destinationCity, route.DestinationCity))}`);

        routeLayers.push(line, start, end);

        try {
            map.fitBounds(line.getBounds(), { padding: [44, 44] });
        } catch { }
    }

    window.overwatchEldRenderDispatchRoutes = function (map, routes) {
        ensureStyle();

        const old = document.getElementById("oweld-route-panel");
        if (old) old.remove();

        routes = Array.isArray(routes) ? routes : [];

        if (routes.length === 0) return;

        const panel = document.createElement("div");
        panel.id = "oweld-route-panel";
        panel.className = "oweld-route-panel";

        panel.innerHTML = `
            <div class="oweld-route-panel-header">Dispatch Route Preview</div>
            ${routes.map((r, index) => {
                const load = firstNonEmpty(r.loadNumber, r.LoadNumber, "Load");
                const driver = firstNonEmpty(r.driverName, r.DriverName, "Unassigned");
                const origin = firstNonEmpty(r.originCity, r.OriginCity, "--");
                const dest = firstNonEmpty(r.destinationCity, r.DestinationCity, "--");
                const cargo = firstNonEmpty(r.cargo, r.Cargo);
                const status = firstNonEmpty(r.status, r.Status);

                return `
                    <div class="oweld-route-item" data-index="${index}">
                        <div class="oweld-route-load">${enc(load)} • ${enc(driver)}</div>
                        <div class="oweld-route-meta">${enc(origin)} → ${enc(dest)}</div>
                        <div class="oweld-route-meta">${enc(cargo || "No cargo listed")}</div>
                        <span class="oweld-route-pill">${enc(status || "Active")}</span>
                    </div>
                `;
            }).join("")}
        `;

        panel.querySelectorAll(".oweld-route-item").forEach(el => {
            el.addEventListener("click", () => {
                const index = Number(el.getAttribute("data-index"));
                const route = routes[index];
                drawRoute(map, route);
            });
        });

        document.body.appendChild(panel);
    };
})();
