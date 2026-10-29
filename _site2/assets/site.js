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

// The destination sign on the home page: a dot-matrix board like the ones on OMSI's buses. It flips through a few destinations, one
// column at a time. The words are in the page for everyone else (the sign is only a picture), and with reduced motion it holds still.
(function () {
  'use strict';

  var box = document.querySelector('.sign');
  if (!box) return;
  var canvas = box.querySelector('canvas');
  var items;
  try { items = JSON.parse(box.getAttribute('data-signs')); } catch (e) { return; }
  if (!canvas || !items || !items.length || !canvas.getContext) return;

  // 5 x 7 letters, row by row ('#' is a lit dot)
  var FONT = {
    'A': '.###.#...##...#######...##...##...#',
    'B': '####.#...##...#####.#...##...#####.',
    'C': '.#####....#....#....#....#.....####',
    'D': '####.#...##...##...##...##...#####.',
    'E': '######....#....####.#....#....#####',
    'F': '######....#....####.#....#....#....',
    'G': '.#####....#....#.####...##...#.###.',
    'H': '#...##...##...#######...##...##...#',
    'I': '#####..#....#....#....#....#..#####',
    'J': '..###...#....#....#....#.#..#..##..',
    'K': '#...##..#.#.#..##...#.#..#..#.#...#',
    'L': '#....#....#....#....#....#....#####',
    'M': '#...###.###.#.##.#.##...##...##...#',
    'N': '#...###..##.#.##..###...##...##...#',
    'O': '.###.#...##...##...##...##...#.###.',
    'P': '####.#...##...#####.#....#....#....',
    'Q': '.###.#...##...##...##.#.##..#..##.#',
    'R': '####.#...##...#####.#.#..#..#.#...#',
    'S': '.#####....#.....###.....#....#####.',
    'T': '#####..#....#....#....#....#....#..',
    'U': '#...##...##...##...##...##...#.###.',
    'V': '#...##...##...##...##...#.#.#...#..',
    'W': '#...##...##...##.#.##.#.###.###...#',
    'X': '#...##...#.#.#...#...#.#.#...##...#',
    'Y': '#...##...#.#.#...#....#....#....#..',
    'Z': '#####....#...#...#...#...#....#####',
    '0': '.###.#...##..###.#.###..##...#.###.',
    '1': '..#...##....#....#....#....#...###.',
    '2': '.###.#...#....#...#...#...#...#####',
    '3': '####.....#....#.###.....#....#####.',
    '4': '...#...##..#.#.#..#.#####...#....#.',
    '5': '######....####.....#....##...#.###.',
    '6': '..##..#...#....####.#...##...#.###.',
    '7': '#####....#...#...#...#....#....#...',
    '8': '.###.#...##...#.###.#...##...#.###.',
    '9': '.###.#...##...#.####....#...#..##..',
    '-': '................###................',
    '.': '................................#..',
    '/': '....#....#...#...#...#...#....#....',
    ' ': ''
  };
  var COLS = 120, ROWS = 11, GAP = 1;
  var FIELD = 3;           // letters for the line number
  var TEXT = 16;           // letters for the destination

  function glyph(ch) {
    var g = FONT[ch];
    if (g === undefined) g = FONT[' '];
    if (g === '') return null;
    return g.length === 35 ? g : '#####'.repeat(7);
  }

  // the lit dots of one sign as an array of [col, row]
  function lit(item) {
    var dots = [];
    function put(text, left, width) {
      text = text.toUpperCase().slice(0, width);
      for (var i = 0; i < text.length; i++) {
        var g = glyph(text.charAt(i));
        if (!g) continue;
        for (var r = 0; r < 7; r++) for (var c = 0; c < 5; c++) if (g.charAt(r * 5 + c) === '#') dots.push([left + i * 6 + c, r + 2]);
      }
    }
    put(item[0], 2, FIELD);
    put(item[1], 2 + FIELD * 6 + 3, TEXT);
    return dots;
  }

  var ctx = canvas.getContext('2d');
  var on = '#ffb41f', off = 'rgba(255, 180, 31, .09)';
  var size, pitch;

  function resize() {
    var ratio = window.devicePixelRatio || 1;
    var width = canvas.clientWidth || 600;
    pitch = width / COLS;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(pitch * ROWS * ratio);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    size = pitch;
  }

  // 'frame' draws every dot: lit ones from the old sign left of the wipe column, from the new one right of it
  function draw(from, to, wipe) {
    ctx.clearRect(0, 0, COLS * size, ROWS * size);
    var radius = size * .36;
    var litNow = {}, key = function (c, r) { return c * 32 + r; };
    from.forEach(function (d) { if (d[0] >= wipe) litNow[key(d[0], d[1])] = 1; });
    to.forEach(function (d) { if (d[0] < wipe) litNow[key(d[0], d[1])] = 1; });
    for (var c = 0; c < COLS; c++) {
      for (var r = 0; r < ROWS; r++) {
        var isOn = litNow[key(c, r)];
        ctx.fillStyle = isOn ? on : off;
        if (isOn) { ctx.shadowColor = on; ctx.shadowBlur = size * .9; } else { ctx.shadowBlur = 0; }
        ctx.beginPath();
        ctx.arc((c + .5) * size, (r + .5) * size, radius, 0, 6.2832);
        ctx.fill();
      }
    }
    ctx.shadowBlur = 0;
  }

  var shown = items.map(lit);
  var index = 0;
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  resize();
  draw(shown[0], shown[0], 0);
  window.addEventListener('resize', function () { resize(); draw(shown[index], shown[index], 0); });
  if (reduce || shown.length < 2) return;

  var busy = false;
  function next() {
    if (busy || document.hidden) return;
    busy = true;
    var from = shown[index];
    index = (index + 1) % shown.length;
    var to = shown[index], start = null;
    function step(now) {
      if (start === null) start = now;
      var wipe = Math.round((now - start) / 700 * COLS);
      if (wipe >= COLS) { draw(to, to, 0); busy = false; return; }
      draw(from, to, wipe);
      requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }
  setInterval(next, 3200);
})();
