/*
OverWatch ELD Live Map Add-ons #5-#7
#5 Expansion depot/company markers
#6 Driver route trails
#7 Region overlays including Illinois DLC

MapLibre-safe version.
*/

(function () {
    const togglePanelId = "oweld-map-toggle-panel";
    const styleId = "oweld-map-addons-567-style";

    function ensureStyle() {
        if (document.getElementById(styleId)) return;

        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = `
            .oweld-map-toggle-panel {
                position: absolute;
                right: 18px;
                bottom: 18px;
                z-index: 9997;
                background: #0D1A2B;
                color: #EAF2FF;
                border: 1px solid #263E5C;
                border-radius: 14px;
                padding: 10px;
                font-family: Segoe UI, Arial, sans-serif;
                box-shadow: 0 18px 44px rgba(0,0,0,.38);
                display: grid;
                gap: 8px;
                min-width: 210px;
            }

            .oweld-map-toggle-title {
                font-weight: 800;
                font-size: 13px;
                color: #EAF2FF;
                border-bottom: 1px solid #263E5C;
                padding-bottom: 7px;
            }

            .oweld-map-toggle-panel label {
                display: flex;
                align-items: center;
                gap: 8px;
                font-size: 13px;
                color: #9FB3CC;
                cursor: pointer;
            }
        `;
        document.head.appendChild(style);
    }

    function safeId(value) {
        return String(value || "")
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function asFeatureCollection(features) {
        return {
            type: "FeatureCollection",
            features: Array.isArray(features) ? features : []
        };
    }

    function regionToFeature(region) {
        const points = region.points || region.Points || [];
        const coords = points
            .map(p => [
                Number(p.longitude ?? p.Longitude),
                Number(p.latitude ?? p.Latitude)
            ])
            .filter(p => Number.isFinite(p[0]) && Number.isFinite(p[1]));

        if (coords.length < 3) return null;

        const first = coords[0];
        const last = coords[coords.length - 1];

        if (first[0] !== last[0] || first[1] !== last[1])
            coords.push(first);

        return {
            type: "Feature",
            properties: {
                name: region.name || region.Name || "Region",
                shortName: region.shortName || region.ShortName || "",
                stroke: region.stroke || region.Stroke || "#4A91D0",
                fill: region.fill || region.Fill || "#163B65",
                opacity: Number(region.opacity ?? region.Opacity ?? 0.08)
            },
            geometry: {
                type: "Polygon",
                coordinates: [coords]
            }
        };
    }

    function depotToFeature(depot) {
        const lat = Number(depot.latitude ?? depot.Latitude);
        const lon = Number(depot.longitude ?? depot.Longitude ?? depot.lng ?? depot.Lng);

        if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;

        return {
            type: "Feature",
            properties: {
                companyName: depot.companyName || depot.CompanyName || "Depot",
                cityName: depot.cityName || depot.CityName || "",
                source: depot.source || depot.Source || "",
                kind: depot.kind || depot.Kind || "Company"
            },
            geometry: {
                type: "Point",
                coordinates: [lon, lat]
            }
        };
    }

    function trailToFeatures(trails) {
        const features = [];

        (Array.isArray(trails) ? trails : []).forEach(t => {
            const points = t.points || t.Points || [];
            const coords = points
                .map(p => [
                    Number(p.longitude ?? p.Longitude),
                    Number(p.latitude ?? p.Latitude)
                ])
                .filter(p => Number.isFinite(p[0]) && Number.isFinite(p[1]));

            if (coords.length < 2) return;

            features.push({
                type: "Feature",
                properties: {
                    driverName: t.driverName || t.DriverName || "Driver Trail"
                },
                geometry: {
                    type: "LineString",
                    coordinates: coords
                }
            });
        });

        return features;
    }

    function removeLayerAndSource(map, layerId, sourceId) {
        try {
            if (map.getLayer(layerId)) map.removeLayer(layerId);
        } catch { }

        try {
            if (sourceId && map.getSource(sourceId)) map.removeSource(sourceId);
        } catch { }
    }

    function setLayerVisibility(map, layerIds, visible) {
        layerIds.forEach(id => {
            try {
                if (map.getLayer(id)) {
                    map.setLayoutProperty(id, "visibility", visible ? "visible" : "none");
                }
            } catch { }
        });
    }

    window.overwatchEldRenderRegionOverlays = function (map, regions, visible) {
        if (!map) return;

        const sourceId = "oweld-region-overlays";
        const fillLayer = "oweld-region-overlays-fill";
        const lineLayer = "oweld-region-overlays-line";
        const labelLayer = "oweld-region-overlays-label";

        const features = (Array.isArray(regions) ? regions : [])
            .map(regionToFeature)
            .filter(Boolean);

        const fc = asFeatureCollection(features);

        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: "geojson",
                data: fc
            });

            map.addLayer({
                id: fillLayer,
                type: "fill",
                source: sourceId,
                paint: {
                    "fill-color": ["get", "fill"],
                    "fill-opacity": ["get", "opacity"]
                }
            });

            map.addLayer({
                id: lineLayer,
                type: "line",
                source: sourceId,
                paint: {
                    "line-color": ["get", "stroke"],
                    "line-width": 2,
                    "line-opacity": 0.85
                }
            });

            map.addLayer({
                id: labelLayer,
                type: "symbol",
                source: sourceId,
                layout: {
                    "text-field": ["get", "shortName"],
                    "text-size": 13,
                    "text-font": ["Open Sans Bold", "Arial Unicode MS Bold"],
                    "visibility": visible ? "visible" : "none"
                },
                paint: {
                    "text-color": "#EAF2FF",
                    "text-halo-color": "#07111F",
                    "text-halo-width": 2
                }
            });
        } else {
            map.getSource(sourceId).setData(fc);
        }

        setLayerVisibility(map, [fillLayer, lineLayer, labelLayer], visible);
    };

    window.overwatchEldRenderDepotMarkers = function (map, depots, visible) {
        if (!map) return;

        const sourceId = "oweld-expansion-depots";
        const layerId = "oweld-expansion-depots-circle";
        const labelId = "oweld-expansion-depots-label";

        const features = (Array.isArray(depots) ? depots : [])
            .map(depotToFeature)
            .filter(Boolean);

        const fc = asFeatureCollection(features);

        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: "geojson",
                data: fc
            });

            map.addLayer({
                id: layerId,
                type: "circle",
                source: sourceId,
                paint: {
                    "circle-radius": 5,
                    "circle-color": "#163B65",
                    "circle-stroke-color": "#4A91D0",
                    "circle-stroke-width": 2,
                    "circle-opacity": 0.95
                },
                layout: {
                    "visibility": visible ? "visible" : "none"
                }
            });

            map.addLayer({
                id: labelId,
                type: "symbol",
                source: sourceId,
                layout: {
                    "text-field": ["get", "companyName"],
                    "text-size": 10,
                    "text-offset": [0, 1.2],
                    "visibility": visible ? "visible" : "none"
                },
                paint: {
                    "text-color": "#EAF2FF",
                    "text-halo-color": "#07111F",
                    "text-halo-width": 1.5
                }
            });
        } else {
            map.getSource(sourceId).setData(fc);
        }

        setLayerVisibility(map, [layerId, labelId], visible);
    };

    window.overwatchEldRenderDriverTrails = function (map, trails, visible) {
        if (!map) return;

        const sourceId = "oweld-driver-trails";
        const layerId = "oweld-driver-trails-line";

        const fc = asFeatureCollection(trailToFeatures(trails));

        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: "geojson",
                data: fc
            });

            map.addLayer({
                id: layerId,
                type: "line",
                source: sourceId,
                paint: {
                    "line-color": "#35B474",
                    "line-width": 3,
                    "line-opacity": 0.72
                },
                layout: {
                    "visibility": visible ? "visible" : "none"
                }
            });
        } else {
            map.getSource(sourceId).setData(fc);
        }

        setLayerVisibility(map, [layerId], visible);
    };

    window.overwatchEldInstallMapAddonToggles = function (map, payload) {
        ensureStyle();

        const old = document.getElementById(togglePanelId);
        if (old) old.remove();

        payload = payload || {};

        const panel = document.createElement("div");
        panel.id = togglePanelId;
        panel.className = "oweld-map-toggle-panel";
        panel.innerHTML = `
            <div class="oweld-map-toggle-title">Map Layers</div>
            <label><input type="checkbox" id="oweld-toggle-depots"> Expansion depots</label>
            <label><input type="checkbox" id="oweld-toggle-trails" checked> Driver trails</label>
            <label><input type="checkbox" id="oweld-toggle-regions"> Region overlay</label>
            <label><input type="checkbox" id="oweld-toggle-illinois" checked> Illinois DLC map</label>
        `;

        document.body.appendChild(panel);

        const depots = payload.depots || payload.Depots || [];
        const trails = payload.trails || payload.Trails || [];
        const allRegions = payload.regions || payload.Regions || [];

        const depotsBox = panel.querySelector("#oweld-toggle-depots");
        const trailsBox = panel.querySelector("#oweld-toggle-trails");
        const regionsBox = panel.querySelector("#oweld-toggle-regions");
        const illinoisBox = panel.querySelector("#oweld-toggle-illinois");

        const refresh = () => {
            const showGeneralRegions = regionsBox.checked;
            const showIllinois = illinoisBox.checked;

            const selectedRegions = (Array.isArray(allRegions) ? allRegions : []).filter(r => {
                const name = String(r.name || r.Name || "").toLowerCase();

                if (name.includes("illinois"))
                    return showIllinois;

                return showGeneralRegions;
            });

            window.overwatchEldRenderDepotMarkers(map, depots, depotsBox.checked);
            window.overwatchEldRenderDriverTrails(map, trails, trailsBox.checked);
            window.overwatchEldRenderRegionOverlays(map, selectedRegions, selectedRegions.length > 0);
        };

        depotsBox.addEventListener("change", refresh);
        trailsBox.addEventListener("change", refresh);
        regionsBox.addEventListener("change", refresh);
        illinoisBox.addEventListener("change", refresh);

        if (map.loaded && map.loaded()) {
            refresh();
        } else {
            map.once("load", refresh);
        }
    };
})();
