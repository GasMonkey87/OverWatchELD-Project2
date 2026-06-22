/*
OverWatch ELD Illinois Cities + Roads Live Map Overlay
MapLibre-safe add-on.
*/

(function () {
    const styleId = "oweld-illinois-map-style";

    function ensureStyle() {
        if (document.getElementById(styleId)) return;

        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = `
            .oweld-map-toggle-panel label.oweld-illinois-sub {
                margin-left: 10px;
                font-size: 12px;
            }
        `;
        document.head.appendChild(style);
    }

    function asFeatureCollection(features) {
        return {
            type: "FeatureCollection",
            features: Array.isArray(features) ? features : []
        };
    }

    function cityToFeature(city) {
        const lat = Number(city.latitude ?? city.Latitude);
        const lon = Number(city.longitude ?? city.Longitude);

        if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;

        return {
            type: "Feature",
            properties: {
                name: city.name || city.Name || "Illinois City",
                state: city.state || city.State || "IL",
                kind: city.kind || city.Kind || "City"
            },
            geometry: {
                type: "Point",
                coordinates: [lon, lat]
            }
        };
    }

    function roadToFeature(road) {
        const points = road.points || road.Points || [];
        const coords = points
            .map(p => [
                Number(p.longitude ?? p.Longitude),
                Number(p.latitude ?? p.Latitude)
            ])
            .filter(p => Number.isFinite(p[0]) && Number.isFinite(p[1]));

        if (coords.length < 2) return null;

        return {
            type: "Feature",
            properties: {
                name: road.name || road.Name || "Illinois Road",
                kind: road.kind || road.Kind || "Road",
                color: road.color || road.Color || "#4A91D0"
            },
            geometry: {
                type: "LineString",
                coordinates: coords
            }
        };
    }

    function setVisible(map, layerIds, visible) {
        layerIds.forEach(id => {
            try {
                if (map.getLayer(id)) {
                    map.setLayoutProperty(id, "visibility", visible ? "visible" : "none");
                }
            } catch { }
        });
    }

    window.overwatchEldRenderIllinoisCitiesRoads = function (map, payload, showCities, showRoads) {
        if (!map) return;

        payload = payload || {};

        const citySource = "oweld-illinois-cities";
        const cityCircle = "oweld-illinois-cities-circle";
        const cityLabel = "oweld-illinois-cities-label";

        const roadSource = "oweld-illinois-roads";
        const roadLine = "oweld-illinois-roads-line";
        const roadLabel = "oweld-illinois-roads-label";

        const cities = Array.isArray(payload.cities || payload.Cities)
            ? (payload.cities || payload.Cities)
            : [];

        const roads = Array.isArray(payload.roads || payload.Roads)
            ? (payload.roads || payload.Roads)
            : [];

        const cityFeatures = cities.map(cityToFeature).filter(Boolean);
        const roadFeatures = roads.map(roadToFeature).filter(Boolean);

        if (!map.getSource(citySource)) {
            map.addSource(citySource, {
                type: "geojson",
                data: asFeatureCollection(cityFeatures)
            });

            map.addLayer({
                id: cityCircle,
                type: "circle",
                source: citySource,
                paint: {
                    "circle-radius": [
                        "case",
                        ["==", ["get", "kind"], "Major City"], 7,
                        ["==", ["get", "kind"], "Capital"], 6,
                        5
                    ],
                    "circle-color": "#FFB454",
                    "circle-stroke-color": "#07111F",
                    "circle-stroke-width": 2,
                    "circle-opacity": 0.96
                },
                layout: {
                    "visibility": showCities ? "visible" : "none"
                }
            });

            map.addLayer({
                id: cityLabel,
                type: "symbol",
                source: citySource,
                layout: {
                    "text-field": ["get", "name"],
                    "text-size": [
                        "case",
                        ["==", ["get", "kind"], "Major City"], 13,
                        11
                    ],
                    "text-offset": [0, 1.35],
                    "text-anchor": "top",
                    "visibility": showCities ? "visible" : "none"
                },
                paint: {
                    "text-color": "#EAF2FF",
                    "text-halo-color": "#07111F",
                    "text-halo-width": 2
                }
            });
        } else {
            map.getSource(citySource).setData(asFeatureCollection(cityFeatures));
        }

        if (!map.getSource(roadSource)) {
            map.addSource(roadSource, {
                type: "geojson",
                data: asFeatureCollection(roadFeatures)
            });

            map.addLayer({
                id: roadLine,
                type: "line",
                source: roadSource,
                layout: {
                    "line-cap": "round",
                    "line-join": "round",
                    "visibility": showRoads ? "visible" : "none"
                },
                paint: {
                    "line-color": ["get", "color"],
                    "line-width": [
                        "case",
                        ["==", ["get", "kind"], "Interstate"], 4,
                        2.5
                    ],
                    "line-opacity": 0.82
                }
            });

            map.addLayer({
                id: roadLabel,
                type: "symbol",
                source: roadSource,
                layout: {
                    "symbol-placement": "line",
                    "text-field": ["get", "name"],
                    "text-size": 10,
                    "visibility": showRoads ? "visible" : "none"
                },
                paint: {
                    "text-color": "#EAF2FF",
                    "text-halo-color": "#07111F",
                    "text-halo-width": 2
                }
            });
        } else {
            map.getSource(roadSource).setData(asFeatureCollection(roadFeatures));
        }

        setVisible(map, [cityCircle, cityLabel], showCities);
        setVisible(map, [roadLine, roadLabel], showRoads);
    };

    window.overwatchEldInstallIllinoisMapToggles = function (map, illinoisPayload) {
        ensureStyle();

        const panel = document.getElementById("oweld-map-toggle-panel");

        if (!panel) {
            const div = document.createElement("div");
            div.id = "oweld-map-toggle-panel";
            div.className = "oweld-map-toggle-panel";
            div.innerHTML = `<div class="oweld-map-toggle-title">Map Layers</div>`;
            document.body.appendChild(div);
        }

        const targetPanel = document.getElementById("oweld-map-toggle-panel");

        if (!document.getElementById("oweld-toggle-illinois-cities")) {
            const cityLabel = document.createElement("label");
            cityLabel.className = "oweld-illinois-sub";
            cityLabel.innerHTML = `<input type="checkbox" id="oweld-toggle-illinois-cities" checked> Illinois cities`;
            targetPanel.appendChild(cityLabel);
        }

        if (!document.getElementById("oweld-toggle-illinois-roads")) {
            const roadLabel = document.createElement("label");
            roadLabel.className = "oweld-illinois-sub";
            roadLabel.innerHTML = `<input type="checkbox" id="oweld-toggle-illinois-roads" checked> Illinois roads`;
            targetPanel.appendChild(roadLabel);
        }

        const cityBox = document.getElementById("oweld-toggle-illinois-cities");
        const roadBox = document.getElementById("oweld-toggle-illinois-roads");

        const refresh = () => {
            window.overwatchEldRenderIllinoisCitiesRoads(
                map,
                illinoisPayload || {},
                cityBox.checked,
                roadBox.checked);
        };

        cityBox.addEventListener("change", refresh);
        roadBox.addEventListener("change", refresh);

        if (map.loaded && map.loaded()) {
            refresh();
        } else {
            map.once("load", refresh);
        }
    };
})();
