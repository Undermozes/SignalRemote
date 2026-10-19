// Shows <time data-local-time> elements in the viewer's own time zone.  Blazor owns the
// element's text (UTC), so this only sets an attribute; app.css shows it instead.
// data-local-time="time" shows only the time; any other value shows date and time.
export function localizeTimes() {
    for (const el of document.querySelectorAll("time[data-local-time]")) {
        const date = new Date(el.getAttribute("datetime"));
        if (isNaN(date)) {
            continue;
        }
        el.dataset.localized = el.dataset.localTime === "time"
            ? date.toLocaleTimeString()
            : date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
        el.title = date.toString();
    }
}
