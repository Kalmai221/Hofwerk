// Small touches only: the site works without this. It points the big button at the right file for the visitor's system,
// and lets people choose whether the newest early version is shown next to the newest final one.
(function () {
  'use strict';

  var ua = navigator.userAgent || '';
  var os = /Android|iPhone|iPad|iPod/.test(ua) ? '' : /Windows/.test(ua) ? 'windows' : /Linux|X11|CrOS/.test(ua) ? 'linux' : '';
  if (os) document.documentElement.setAttribute('data-os', os);

  document.querySelectorAll('[data-download-cta]').forEach(function (link) {
    var direct = os === 'windows' ? link.getAttribute('data-windows') : os === 'linux' ? link.getAttribute('data-linux') : '';
    if (direct) {
      link.setAttribute('href', direct);
      link.setAttribute('download', '');
    }
  });

  var toggle = document.getElementById('show-early');
  if (toggle) {
    var early = document.querySelectorAll('.release[data-channel="early"]');
    var apply = function () { early.forEach(function (r) { r.hidden = !toggle.checked; }); };
    try { toggle.checked = localStorage.getItem('hofwerk-show-early') === '1'; } catch (e) { /* private window: fine */ }
    toggle.addEventListener('change', function () {
      apply();
      try { localStorage.setItem('hofwerk-show-early', toggle.checked ? '1' : '0'); } catch (e) { /* ignore */ }
    });
    apply();
  }
})();
