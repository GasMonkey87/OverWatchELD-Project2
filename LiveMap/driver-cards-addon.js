/*
OverWatch ELD Live Map Driver Cards Add-on
Drop this into LiveMap/index.html or include it after the existing map script.
It is safe if no drivers/card payload exists yet.
*/

(function () {
    const eldCardStyleId = "overwatch-eld-driver-card-style";

    function ensureDriverCardStyle() {
        if (document.getElementById(eldCardStyleId)) return;

        const style = document.createElement("style");
        style.id = eldCardStyleId;
        style.textContent = `
            .oweld-driver-card {
                position: absolute;
                right: 18px;
                top: 18px;
                width: 320px;
                max-width: calc(100vw - 36px);
                background: #0D1A2B;
                border: 1px solid #263E5C;
                border-radius: 14px;
                color: #EAF2FF;
                box-shadow: 0 18px 44px rgba(0,0,0,.38);
                z-index: 9999;
                overflow: hidden;
                font-family: Segoe UI, Arial, sans-serif;
            }

            .oweld-driver-card-header {
                display: flex;
                align-items: center;
                justify-content: space-between;
                background: #07111F;
                border-bottom: 1px solid #263E5C;
                padding: 12px 14px;
            }

            .oweld-driver-card-title {
                font-weight: 800;
                font-size: 15px;
                letter-spacing: .2px;
            }

            .oweld-driver-card-close {
                border: 1px solid #4E6077;
                background: #2A3444;
                color: white;
                border-radius: 8px;
                padding: 4px 8px;
                cursor: pointer;
            }

            .oweld-driver-card-body {
                padding: 14px;
            }

            .oweld-driver-card-row {
                display: grid;
                grid-template-columns: 96px 1fr;
                gap: 8px;
                padding: 6px 0;
                border-bottom: 1px solid rgba(38,62,92,.55);
                font-size: 13px;
            }

            .oweld-driver-card-row:last-child {
                border-bottom: none;
            }

            .oweld-driver-card-label {
                color: #9FB3CC;
                font-weight: 650;
            }

            .oweld-driver-card-value {
                color: #EAF2FF;
                overflow-wrap: anywhere;
            }

            .oweld-driver-card-pill {
                display: inline-block;
                background: #1F7A4D;
                color: white;
                padding: 3px 8px;
                border-radius: 999px;
                font-size: 12px;
                font-weight: 700;
            }

            .oweld-driver-card-muted {
                color: #9FB3CC;
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

    function htmlEncode(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function row(label, value, fallback) {
        const v = firstNonEmpty(value, fallback || "--");
        return `
            <div class="oweld-driver-card-row">
                <div class="oweld-driver-card-label">${htmlEncode(label)}</div>
                <div class="oweld-driver-card-value">${htmlEncode(v)}</div>
            </div>`;
    }

    window.overwatchEldShowDriverCard = function (driver) {
        ensureDriverCardStyle();

        const old = document.getElementById("overwatch-eld-driver-card");
        if (old) old.remove();

        const card = document.createElement("div");
        card.id = "overwatch-eld-driver-card";
        card.className = "oweld-driver-card";

        const driverName = firstNonEmpty(driver.driverName, driver.DriverName, driver.name, driver.Name, "Unknown Driver");
        const status = firstNonEmpty(driver.status, driver.Status, "Unknown");
        const city = firstNonEmpty(driver.currentCity, driver.CurrentCity, driver.resolvedCity, driver.ResolvedCity);
        const company = firstNonEmpty(driver.currentCompany, driver.CurrentCompany, driver.resolvedCompany, driver.ResolvedCompany);
        const source = firstNonEmpty(driver.mapSource, driver.MapSource, driver.resolvedSource, driver.ResolvedSource);

        card.innerHTML = `
            <div class="oweld-driver-card-header">
                <div>
                    <div class="oweld-driver-card-title">${htmlEncode(driverName)}</div>
                    <div class="oweld-driver-card-muted">${htmlEncode(firstNonEmpty(city, "Unknown Location"))}</div>
                </div>
                <button class="oweld-driver-card-close" type="button">X</button>
            </div>
            <div class="oweld-driver-card-body">
                <div style="margin-bottom:8px;"><span class="oweld-driver-card-pill">${htmlEncode(status)}</span></div>
                ${row("Truck", firstNonEmpty(driver.truckName, driver.TruckName, driver.truck, driver.Truck))}
                ${row("Company", company)}
                ${row("Map Source", source)}
                ${row("Load #", firstNonEmpty(driver.loadNumber, driver.LoadNumber))}
                ${row("Cargo", firstNonEmpty(driver.cargo, driver.Cargo))}
                ${row("Trailer", firstNonEmpty(driver.trailer, driver.Trailer))}
                ${row("World X/Z", [
                    firstNonEmpty(driver.worldX, driver.WorldX),
                    firstNonEmpty(driver.worldZ, driver.WorldZ)
                ].filter(Boolean).join(" / "))}
            </div>
        `;

        card.querySelector(".oweld-driver-card-close").addEventListener("click", function () {
            card.remove();
        });

        document.body.appendChild(card);
    };

    window.overwatchEldAttachDriverCards = function (map, markerLookup, driverLookup) {
        // markerLookup can be:
        // 1) array of markers
        // 2) object keyed by driver id/name
        // driverLookup can be:
        // 1) array of driver payload rows
        // 2) object keyed by driver id/name
        if (!markerLookup) return;

        const driversByKey = {};
        const drivers = Array.isArray(driverLookup) ? driverLookup : Object.values(driverLookup || {});
        drivers.forEach(d => {
            const keys = [
                d.driverName, d.DriverName, d.name, d.Name, d.id, d.Id, d.driverId, d.DriverId
            ].filter(Boolean).map(x => String(x).trim().toLowerCase());

            keys.forEach(k => driversByKey[k] = d);
        });

        const markers = Array.isArray(markerLookup) ? markerLookup : Object.values(markerLookup || {});
        markers.forEach(marker => {
            if (!marker || marker.__oweldCardAttached) return;

            const attachedDriver =
                marker.driver ||
                marker.data ||
                marker.options?.driver ||
                marker.options?.data ||
                null;

            const key = firstNonEmpty(
                attachedDriver?.driverName,
                attachedDriver?.DriverName,
                attachedDriver?.name,
                attachedDriver?.Name,
                attachedDriver?.id,
                attachedDriver?.Id,
                marker.driverName,
                marker.name
            ).toLowerCase();

            const driver = driversByKey[key] || attachedDriver;
            if (!driver) return;

            if (typeof marker.on === "function") {
                marker.on("click", function () {
                    window.overwatchEldShowDriverCard(driver);
                });
                marker.__oweldCardAttached = true;
            }
        });
    };
})();
