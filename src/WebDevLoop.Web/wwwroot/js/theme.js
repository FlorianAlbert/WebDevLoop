// Loaded synchronously in <head> so the theme is applied before first paint.
(function () {
    var key = 'wdl-theme';
    var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function stored() {
        try {
            var v = localStorage.getItem(key);
            return v === 'light' || v === 'dark' ? v : 'system';
        } catch (e) { return 'system'; }
    }

    function apply(mode) {
        var effective = mode === 'system' ? (media && media.matches ? 'dark' : 'light') : mode;
        document.documentElement.setAttribute('data-bs-theme', effective);
    }

    apply(stored());
    if (media && media.addEventListener) {
        media.addEventListener('change', function () { apply(stored()); });
    }

    window.wdlTheme = {
        get: stored,
        set: function (mode) {
            try {
                if (mode === 'system') { localStorage.removeItem(key); } else { localStorage.setItem(key, mode); }
            } catch (e) { }
            apply(mode);
        }
    };
})();
