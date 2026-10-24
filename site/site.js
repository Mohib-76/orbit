// Orbit website. Plain script, no dependencies. Every animation is skipped under reduced motion; the film plays
// only while on screen and the route diagram runs one finite pass at a time.
(function () {
  var REPO = 'Mohib-76/orbit';
  var root = document.documentElement;
  var reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  var hasIO = 'IntersectionObserver' in window;
  var $ = function (s, c) { return (c || document).querySelector(s); };
  var $$ = function (s, c) { return [].slice.call((c || document).querySelectorAll(s)); };
  var onMedia = function (q, fn) { var m = matchMedia(q); if (m.addEventListener) m.addEventListener('change', fn); else if (m.addListener) m.addListener(fn); };

  // Theme toggle (the saved choice is applied in <head>).
  var themeBtn = $('#theme');
  var isDark = function () { return (root.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')) === 'dark'; };
  var syncThemeColor = function () {
    $$('meta[name="theme-color"]').forEach(function (m) { m.setAttribute('content', isDark() ? '#07070C' : '#FAFAFC'); m.removeAttribute('media'); });
  };
  if (root.hasAttribute('data-theme')) syncThemeColor();
  if (themeBtn) themeBtn.addEventListener('click', function () {
    var next = isDark() ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('orbit-theme', next); } catch (e) {}
    syncThemeColor();
  });

  // Nav: glass background once scrolled; section in view marked.
  var nav = $('#nav'), top = $('#top');
  if (hasIO && nav && top) new IntersectionObserver(function (e) { nav.classList.toggle('scrolled', !e[0].isIntersecting); }).observe(top);
  else if (nav) nav.classList.add('scrolled');
  var links = {};
  $$('.nav-links a[href^="#"]').forEach(function (a) { links[a.getAttribute('href').slice(1)] = a; });
  if (hasIO) {
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        var a = links[e.target.id];
        if (!a) return;
        if (e.isIntersecting) {
          for (var k in links) links[k].removeAttribute('aria-current');
          a.setAttribute('aria-current', 'location');
        } else a.removeAttribute('aria-current');
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    Object.keys(links).forEach(function (id) { var s = document.getElementById(id); if (s) spy.observe(s); });
  }

  // In-page links: CSS scrolls smoothly; move focus to the target too so keyboard and screen-reader users follow.
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[href^="#"]');
    if (!a || e.defaultPrevented) return;
    var id = a.getAttribute('href').slice(1), el = id && id !== 'top' ? document.getElementById(id) : null;
    if (!el) return;
    if (!el.hasAttribute('tabindex')) el.setAttribute('tabindex', '-1');
    setTimeout(function () { el.focus({ preventScroll: true }); }, 0);
  });

  // Phone menu.
  var menuBtn = $('#menu-btn'), menu = $('#menu');
  if (menuBtn && menu) {
    var setMenu = function (open) {
      menu.hidden = !open;
      menuBtn.setAttribute('aria-expanded', String(open));
      menuBtn.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
      nav.classList.toggle('scrolled', open || scrollY > 4);
    };
    menuBtn.addEventListener('click', function () { setMenu(menu.hidden); });
    menu.addEventListener('click', function (e) { if (e.target.closest('a')) setMenu(false); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !menu.hidden) { setMenu(false); menuBtn.focus(); } });
    onMedia('(min-width: 961px)', function (m) { if (m.matches) setMenu(false); });
  }

  // Platform first; direct download links from the latest release when GitHub answers.
  var ua = navigator.userAgent || '';
  var platform = /Android/i.test(ua) ? 'android' : /Windows/i.test(ua) ? 'windows' : /iPhone|iPad|Macintosh|Linux/i.test(ua) ? 'other' : 'unknown';
  var mark = function (id) { var c = document.getElementById(id); if (!c) return; c.classList.add('mine'); $('.this', c).hidden = false; };
  var note = $('#platform-note');
  // The Android download is hidden for now (the Windows app installs it over USB), so phones get a note.
  if (platform === 'android') { mark('dl-and'); if (note) note.textContent = 'Open this page on your Windows PC. Orbit for Windows installs the phone app over USB.'; }
  else if (platform === 'windows') mark('dl-win');
  else if (platform === 'other' && note) note.textContent = 'Orbit runs on Windows and Android. Open this page on your Windows PC to install it.';
  if (window.fetch) fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(function (r) { if (!r.ok) throw 0; return r.json(); })
    .then(function (rel) {
      var win = null, apk = null;
      (rel.assets || []).forEach(function (a) {
        if (/^OrbitSetup.*\.exe$/i.test(a.name)) win = a;
        if (/-android\.apk$/i.test(a.name)) apk = a;
      });
      if (rel.tag_name) $$('.rel-version').forEach(function (el) { el.textContent = rel.tag_name; el.setAttribute('translate', 'no'); });
      if (win) { $$('.dl-primary').forEach(function (a) { a.href = win.browser_download_url; }); var w = $('#win-link'); if (w) w.href = win.browser_download_url; }
      var andLink = $('#and-link');
      if (apk && andLink) andLink.href = apk.browser_download_url;
    })
    .catch(function () { /* keep the Releases page links */ });

  // Scroll reveal.
  var reveal = $$('[data-reveal]');
  if (hasIO && !reduce) {
    var rio = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); rio.unobserve(e.target); } });
    }, { rootMargin: '0px 0px -10% 0px', threshold: 0.05 });
    reveal.forEach(function (el) { rio.observe(el); });
  } else reveal.forEach(function (el) { el.classList.add('in'); });

  // Hero: as it scrolls away the rings turn and the product frame straightens (transform only, rAF-batched).
  var hero = $('#hero'), stage = $('#stage3d');
  if (hero && stage && !reduce) {
    var heroOn = true, queued = false;
    var paint = function () {
      queued = false;
      var vh = innerHeight, r = stage.getBoundingClientRect();
      var p = Math.min(1, Math.max(0, scrollY / (vh * 0.9)));
      var p2 = Math.min(1, Math.max(0, (vh - r.top) / (vh * 0.75)));
      hero.style.setProperty('--p', p.toFixed(4));
      stage.style.setProperty('--p2', p2.toFixed(4));
    };
    var queue = function () { if (!queued && heroOn) { queued = true; requestAnimationFrame(paint); } };
    if (hasIO) new IntersectionObserver(function (e) { heroOn = e[0].isIntersecting; queue(); }).observe(hero);
    addEventListener('scroll', queue, { passive: true });
    addEventListener('resize', queue, { passive: true });
    paint();
  } else if (stage) stage.style.setProperty('--p2', 1);

  // Spotlight on tiles, following the pointer (fine pointers only).
  if (matchMedia('(pointer: fine)').matches) $$('.tile').forEach(function (t) {
    var x = 0, y = 0, pending = false;
    t.addEventListener('pointermove', function (e) {
      x = e.clientX; y = e.clientY;
      if (pending) return; pending = true;
      requestAnimationFrame(function () {
        pending = false;
        var r = t.getBoundingClientRect();
        t.style.setProperty('--mx', (x - r.left) + 'px');
        t.style.setProperty('--my', (y - r.top) + 'px');
      });
    });
  });

  // Film: plays only while on screen and in the current theme; never loads before it's near. The Pause button
  // stops it for good (WCAG 2.2.2); under reduced motion it starts paused on its poster.
  var film = $('.film');
  if (film) {
    var vids = $$('video', film), btn = $('.film-btn', film);
    var label = $('.film-btn-l', btn), stopped = reduce, near = false, seen = false;
    var current = function () { for (var i = 0; i < vids.length; i++) if (vids[i].offsetParent) return vids[i]; return vids[0]; };
    var sync = function () {
      var v = current();
      vids.forEach(function (o) { if (o !== v && !o.paused) o.pause(); });
      btn.setAttribute('data-stopped', String(stopped));
      label.textContent = stopped ? 'Play' : 'Pause';
      btn.setAttribute('aria-label', stopped ? 'Play the video' : 'Pause the video');
      if (!near) return;
      if (v.preload !== 'auto') v.preload = 'auto';
      if (!stopped && seen) { var p = v.play(); if (p && p.catch) p.catch(function () {}); } else if (!v.paused) v.pause();
    };
    btn.addEventListener('click', function () { stopped = !stopped; near = true; seen = true; sync(); });
    if (hasIO) {
      new IntersectionObserver(function (e) { near = near || e[0].isIntersecting; sync(); }, { rootMargin: '400px 0px' }).observe(film);
      new IntersectionObserver(function (e) { seen = e[0].isIntersecting; sync(); }, { threshold: 0.35 }).observe(film);
    } else { near = seen = true; }
    if (themeBtn) themeBtn.addEventListener('click', function () { setTimeout(sync, 0); });
    onMedia('(prefers-color-scheme: dark)', sync);
    sync();
  }

  // Route diagram. The PC dials every lane at once; the chosen one answers and locks, the rest drop, then a few
  // packets travel the locked lane once (under 5 s). The HTML already paints the finished, locked state.
  var race = $('#race');
  if (!race) return;
  var COPY = [
    ['Connected over the USB cable', 'Orbit connects through the cable with adb, even with no network at all. Requires USB debugging on the phone.'],
    ['Connected over your phone’s hotspot', 'With USB tethering or a hotspot, your phone is the PC’s gateway, so Orbit reaches it directly, typically in well under a second.'],
    ['Connected over your Wi-Fi', 'On a shared network, the PC sends a local broadcast that only your paired phone answers.'],
    ['Connected over your manual address', 'On a VPN or a guest network that isolates devices, enter your phone’s address once in Orbit’s settings.']
  ];
  var lanes = $$('.lane', race).map(function (g) {
    var probe = $('.lane-probe', g);
    return { g: g, probe: probe, lock: $('.lane-lock', g), len: probe.getTotalLength() };
  });
  var labels = $$('.lane-label', race);
  var pkts = $$('.pkt', race);
  var title = $('.st-title', race), text = $('.st-text', race);
  var buttons = $$('.route');
  var replay = $('#replay');

  var WIN = 650, CUT = 760;
  var DUR = [1500, 1250, 1400, 1650], REACH = [0.78, 0.7, 0.74, 0.66];
  var TRIP = 1300, GAP = 1100;
  var winner = +race.getAttribute('data-winner') || 0;
  var t0 = 0, running = false, ticking = false, announced = true, played = false;

  var out4 = function (x) { return 1 - Math.pow(1 - x, 4); };
  var inOut = function (x) { return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; };
  var clamp = function (x) { return x < 0 ? 0 : x > 1 ? 1 : x; };
  function draw(path, len, part) { path.style.strokeDasharray = len + ' ' + len; path.style.strokeDashoffset = len * (1 - part); }
  function say(state, a, b) { race.setAttribute('data-state', state); title.textContent = a; text.textContent = b; }

  function locked(i) {
    lanes.forEach(function (L, j) {
      L.g.classList.toggle('won', j === i); L.probe.style.opacity = 0;
      L.lock.style.opacity = j === i ? 1 : 0; L.lock.style.strokeWidth = '';
      labels[j].classList.toggle('won', j === i);
    });
    pkts.forEach(function (p) { p.style.opacity = 0; });
  }

  function start(i) {
    winner = i; played = true;
    buttons.forEach(function (b) { b.setAttribute('aria-pressed', String(+b.getAttribute('data-lane') === i)); });
    if (reduce) { locked(i); say('done', COPY[i][0], COPY[i][1]); return; }
    lanes.forEach(function (L, j) { L.g.classList.remove('won'); L.lock.style.opacity = 0; labels[j].classList.remove('won'); });
    pkts.forEach(function (p) { p.style.opacity = 0; });
    say('trying', 'Trying 4 routes at once', 'The PC dials every route in parallel and keeps the first one that answers.');
    t0 = performance.now(); running = true; announced = false;
    kick();
  }

  function frame(now) {
    ticking = false;
    var t = now - t0;
    lanes.forEach(function (L, i) {
      if (i === winner) {
        draw(L.probe, L.len, out4(clamp(t / WIN)));
        L.probe.style.opacity = t < WIN ? 1 : 1 - clamp((t - WIN) / 240);
        if (t >= WIN) {
          var k = clamp((t - WIN) / 260);
          L.g.classList.add('won'); labels[i].classList.add('won');
          L.lock.style.opacity = k; L.lock.style.strokeWidth = 3.5 + (1 - out4(k)) * 3;
        }
        return;
      }
      var part = t < CUT ? out4(clamp(t / DUR[i])) * REACH[i]
        : out4(clamp(CUT / DUR[i])) * REACH[i] * (1 - inOut(clamp((t - CUT) / 420)));
      draw(L.probe, L.len, part);
      L.probe.style.opacity = part > 0.003 ? (t < CUT ? 1 : 0.55) : 0;
    });
    if (!announced && t >= WIN) { announced = true; say('done', COPY[winner][0], COPY[winner][1]); }
    var pt = t - WIN - 300;
    if (t >= WIN) packets(pt);
    if (pt > GAP * (pkts.length - 1) + TRIP) { running = false; pkts.forEach(function (p) { p.style.opacity = 0; }); return; }
    kick();
  }

  // One pass: clipboard and file out to the phone, a notification back, along the locked lane.
  function packets(t) {
    var L = lanes[winner];
    pkts.forEach(function (p, j) {
      var c = t - j * GAP;
      if (c < 0 || c > TRIP) { p.style.opacity = 0; return; }
      var f = inOut(c / TRIP), at = p.getAttribute('data-way') === 'in' ? 1 - f : f;
      var pt = L.lock.getPointAtLength(L.len * at);
      p.setAttribute('transform', 'translate(' + pt.x.toFixed(1) + ' ' + pt.y.toFixed(1) + ')');
      p.style.opacity = Math.min(1, c / 160, (TRIP - c) / 160);
    });
  }

  function kick() { if (!ticking && running && !reduce) { ticking = true; requestAnimationFrame(frame); } }

  // A route picked while the diagram is below the fold (one column on phones) brings it into view.
  buttons.forEach(function (b) { b.addEventListener('click', function () {
    var r = race.getBoundingClientRect();
    if (r.top > innerHeight - r.height * 0.5) race.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'nearest' });
    start(+b.getAttribute('data-lane'));
  }); });
  if (replay) replay.addEventListener('click', function () { start(winner); });
  if (hasIO && !reduce) {
    // First time the diagram comes into view, play the race once, after the card has faded in.
    var seenRace = new IntersectionObserver(function (e) {
      if (!e[0].isIntersecting) return;
      seenRace.disconnect();
      setTimeout(function () { if (!played) start(winner); }, 600);
    }, { threshold: 0.4 });
    seenRace.observe(race);
  }
  else if (reduce && replay) replay.hidden = true;
})();
