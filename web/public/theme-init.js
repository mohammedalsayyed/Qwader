// Applies the saved light/dark choice before the app draws, so a dark screen never flashes white (CSP allows only same-origin scripts).
(function () {
  try {
    var t = localStorage.getItem('qwader-theme');
    if (t === 'dark') {
      document.documentElement.dataset.theme = 'dark';
      var m = document.querySelector('meta[name="theme-color"]');
      if (m) m.setAttribute('content', '#0e1524');
    }
  } catch (e) { /* private mode: stays light */ }
})();
