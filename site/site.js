// Orbit website. One requestAnimationFrame clock drives the route race; it runs only while the race is on
// screen and never under reduced motion (the HTML already paints the finished, locked state).
(function () {
  var REPO = 'Mohib-76/orbit';
  var root = document.documentElement;
  var reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  var hasIO = 'IntersectionObserver' in window;

  // Theme toggle (the saved choice is applied in <head>).
  var themeBtn = document.getElementById('theme');
  if (themeBtn) themeBtn.addEventListener('click', function () {
    var cur = root.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    var next = cur === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('orbit-theme', next); } catch (e) {}
  });

  // Nav: a border once the page has scrolled, and the section in view marked.
  var nav = document.getElementById('nav'), top = document.getElementById('top');
  if (hasIO && nav && top) new IntersectionObserver(function (e) { nav.classList.toggle('scrolled', !e[0].isIntersecting); }).observe(top);
  // The header takes the band's dark colors while it sits over the privacy band.
  var band = document.querySelector('.band');
  // Phones: the section row steps aside while scrolling down and returns on the way up.
  var lastY = 0, ticking2 = false;
  addEventListener('scroll', function () {
    if (ticking2) return; ticking2 = true;
    requestAnimationFrame(function () {
      ticking2 = false; var y = scrollY;
      if (Math.abs(y - lastY) > 6) { root.classList.toggle('nav-away', y > 240 && y > lastY); lastY = y; }
      if (band && nav) { var r = band.getBoundingClientRect(), edge = nav.offsetHeight; nav.classList.toggle('on-band', r.top < edge && r.bottom > edge); }
    });
  }, { passive: true });
  // Anchors land below the header, whose height changes on phones.
  if (nav) { var navh = function () { if (!root.classList.contains('nav-away')) root.style.setProperty('--nav0', nav.offsetHeight + 'px'); root.style.setProperty('--navh', nav.offsetHeight + 12 + 'px'); }; navh(); addEventListener('resize', navh, { passive: true }); }
  var links = {};
  document.querySelectorAll('.nav-links a[href^="#"]').forEach(function (a) { links[a.getAttribute('href').slice(1)] = a; });
  if (hasIO) {
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        var a = links[e.target.id];
        if (!a) return;
        if (e.isIntersecting) {
          for (var k in links) links[k].removeAttribute('aria-current');
          a.setAttribute('aria-current', 'location');
        } else if (a.hasAttribute('aria-current')) a.removeAttribute('aria-current');
      });
    }, { rootMargin: '-40% 0px -55% 0px' });
    Object.keys(links).forEach(function (id) { var s = document.getElementById(id); if (s) spy.observe(s); });
  }

  // In-page links glide (CSS smooth scrolling would also slow every scripted scroll); instant under reduced motion.
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[href^="#"]');
    if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey) return;
    var id = a.getAttribute('href').slice(1), el = id ? document.getElementById(id) : null;
    if (!el) return;
    e.preventDefault();
    el.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
    if (history.pushState) history.pushState(null, '', '#' + id);
    if (id === 'top') return;
    el.setAttribute('tabindex', '-1'); el.focus({ preventScroll: true });
  });

  // Your platform first; direct download links from the latest release when GitHub answers.
  var primary = document.querySelector('.dl-primary');
  if (primary) {
    var ua = navigator.userAgent || '';
    var platform = /Android/i.test(ua) ? 'android' : /Windows/i.test(ua) ? 'windows' : /iPhone|iPad|Macintosh/i.test(ua) ? 'apple' : 'other';
    var secondary = document.querySelector('.dl-secondary');
    var mark = function (id) { var c = document.getElementById(id); if (!c) return; c.classList.add('mine'); c.querySelector('.this').hidden = false; };
    if (platform === 'android') {
      document.querySelector('.ico-win').hidden = true; document.querySelector('.ico-and').hidden = false;
      document.querySelector('.dl-primary-label').textContent = 'Download for Android';
      document.querySelector('.dl-primary-sub').textContent = 'Android 10 or newer, APK';
      document.querySelector('.dl-secondary-label').textContent = 'Get the Windows app';
      mark('dl-and');
    } else if (platform === 'windows') {
      mark('dl-win');
    } else if (platform === 'apple') {
      document.getElementById('platform-note').textContent = 'Orbit runs on Windows and Android. Open this page on those devices to install it.';
    }
    fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { if (!r.ok) throw 0; return r.json(); })
      .then(function (rel) {
        var win = null, apk = null;
        (rel.assets || []).forEach(function (a) {
          if (/^OrbitSetup.*\.exe$/i.test(a.name)) win = a;
          if (/-android\.apk$/i.test(a.name)) apk = a;
        });
        if (rel.tag_name) document.querySelectorAll('.rel-version').forEach(function (el) { el.textContent = rel.tag_name; });
        if (win) document.getElementById('win-link').href = win.browser_download_url;
        if (apk) document.getElementById('and-link').href = apk.browser_download_url;
        var p = platform === 'android' ? apk : win, s = platform === 'android' ? win : apk;
        if (p) primary.href = p.browser_download_url;
        if (s && secondary) secondary.href = s.browser_download_url;
      })
      .catch(function () { /* keep the Releases page links */ });
  }

  // Film: plays only while on screen and in the current theme; never loads before it's near. A pause button
  // stops it for good (WCAG 2.2.2); under reduced motion it starts paused on its poster.
  var film = document.querySelector('.film');
  if (film) {
    var vids = [].slice.call(film.querySelectorAll('video')), btn = film.querySelector('.film-btn');
    var label = btn.querySelector('.film-btn-l'), stopped = reduce, near = false, seen = false;
    var current = function () { for (var i = 0; i < vids.length; i++) if (vids[i].offsetParent) return vids[i]; return vids[0]; };
    var sync = function () {
      var v = current();
      vids.forEach(function (o) { if (o !== v && !o.paused) o.pause(); });
      btn.setAttribute('aria-pressed', String(stopped));
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
    matchMedia('(prefers-color-scheme: dark)').addEventListener('change', sync);
    sync();
  }

  // Feature tiles slide up as they arrive (tiles already on screen stay put).
  if (hasIO && !reduce) {
    var tio = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.remove('wait'); tio.unobserve(e.target); } });
    }, { rootMargin: '0px 0px -8% 0px' });
    document.querySelectorAll('.tile').forEach(function (t) {
      if (t.getBoundingClientRect().top > innerHeight) { t.classList.add('wait'); tio.observe(t); }
    });
  }

  // The route race.
  var race = document.getElementById('race');
  if (!race) return;
  var fail = race.getAttribute('data-mode') === 'fail';
  var COPY = [
    ['Connected over the USB cable', 'Through the cable with adb, even with no network at all. It needs USB debugging on the phone.'],
    ['Connected over your phone’s hotspot', 'With USB tethering or the phone’s hotspot, the phone is the PC’s gateway, so the PC dials it directly. That takes well under a second.'],
    ['Connected over your Wi-Fi', 'On the same Wi-Fi, the PC sends a broadcast that only your paired phone answers.'],
    ['Connected over the address you typed', 'On a VPN, or a guest Wi-Fi that keeps devices apart, type the phone’s address once in Orbit’s settings.']
  ];
  var lanes = [].map.call(race.querySelectorAll('.lane'), function (g) {
    var probe = g.querySelector('.lane-probe');
    return { g: g, probe: probe, lock: g.querySelector('.lane-lock'), len: probe.getTotalLength() };
  });
  var labels = race.querySelectorAll('.lane-label');
  var pkts = [].slice.call(race.querySelectorAll('.pkt'));
  var title = race.querySelector('.st-title'), text = race.querySelector('.st-text');
  var buttons = [].slice.call(race.querySelectorAll('.setup'));

  var WIN = 650;                       // the answering lane reaches the phone
  var CUT = 760;                       // the others stop dialling
  var DUR = [1500, 1250, 1400, 1650];  // how far the others would have taken
  var REACH = [0.78, 0.7, 0.74, 0.66]; // how far they get before they drop
  var RETRY = 3000;                    // 404: dial again (the real app retries every 1.5 s)
  var winner = +race.getAttribute('data-winner') || 0;
  var t0 = 0, settled = !fail, onScreen = true, ticking = false, announced = true;

  var out4 = function (x) { return 1 - Math.pow(1 - x, 4); };
  var inOut = function (x) { return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; };
  var clamp = function (x) { return x < 0 ? 0 : x > 1 ? 1 : x; };
  function draw(path, len, part) { path.style.strokeDasharray = len + ' ' + len; path.style.strokeDashoffset = len * (1 - part); }
  function say(state, a, b) { race.setAttribute('data-state', state); title.textContent = a; if (b != null) text.textContent = b; }

  function locked(i) {
    lanes.forEach(function (L, j) {
      L.g.classList.toggle('won', j === i); L.probe.style.opacity = 0;
      L.lock.style.opacity = j === i ? 1 : 0; L.lock.style.strokeDasharray = ''; L.lock.style.strokeWidth = '';
      labels[j].classList.toggle('won', j === i);
    });
    pkts.forEach(function (p) { p.style.opacity = 0; });
  }

  function start(i) {
    winner = i;
    buttons.forEach(function (b) { b.setAttribute('aria-pressed', String(+b.getAttribute('data-lane') === i)); });
    if (reduce) { locked(i); say('done', COPY[i][0], COPY[i][1]); return; }
    lanes.forEach(function (L, j) { L.g.classList.remove('won'); L.lock.style.opacity = 0; labels[j].classList.remove('won'); });
    pkts.forEach(function (p) { p.style.opacity = 0; });
    if (!fail) say('trying', 'Trying 4 routes at once', 'The PC dials every route together and keeps the first one that answers.');
    t0 = performance.now(); settled = false; announced = false;
    kick();
  }

  function frame(now) {
    ticking = false;
    var t = now - t0;
    if (fail && t > RETRY) { t0 = now; t = 0; }
    lanes.forEach(function (L, i) {
      if (!fail && i === winner) {
        draw(L.probe, L.len, out4(clamp(t / WIN)));
        L.probe.style.opacity = t < WIN ? 1 : 1 - clamp((t - WIN) / 240);
        if (t >= WIN) {
          var k = clamp((t - WIN) / 260);
          L.g.classList.add('won'); labels[i].classList.add('won');
          L.lock.style.opacity = k; L.lock.style.strokeWidth = 3.5 + (1 - out4(k)) * 3;
        }
        return;
      }
      var cut = fail ? 1150 : CUT, part;
      if (t < cut) part = out4(clamp(t / DUR[i])) * REACH[i];
      else part = out4(clamp(cut / DUR[i])) * REACH[i] * (1 - inOut(clamp((t - cut) / 420)));
      draw(L.probe, L.len, part);
      L.probe.style.opacity = part > 0.003 ? (t < cut ? 1 : 0.55) : 0;
    });
    if (!fail && !announced && t >= WIN) { announced = true; settled = true; say('done', COPY[winner][0], COPY[winner][1]); }
    if (!fail && t >= WIN) packets(t - WIN - 300);
    kick();
  }

  // Idle: clipboard and file go out to the phone, a notification comes back, along the locked lane.
  function packets(t) {
    var L = lanes[winner], TRIP = 1500, GAP = 1700, LOOP = GAP * pkts.length;
    pkts.forEach(function (p, j) {
      var c = t - j * GAP;
      if (c < 0) { p.style.opacity = 0; return; }
      c = c % LOOP;
      if (c > TRIP) { p.style.opacity = 0; return; }
      var f = inOut(c / TRIP), at = p.getAttribute('data-way') === 'in' ? 1 - f : f;
      var pt = L.lock.getPointAtLength(L.len * at);
      p.setAttribute('transform', 'translate(' + pt.x.toFixed(1) + ' ' + pt.y.toFixed(1) + ')');
      p.style.opacity = Math.min(1, c / 180, (TRIP - c) / 180);
    });
  }

  function kick() { if (!ticking && onScreen && !reduce) { ticking = true; requestAnimationFrame(frame); } }

  buttons.forEach(function (b) { b.addEventListener('click', function () { start(+b.getAttribute('data-lane')); }); });
  if (hasIO) new IntersectionObserver(function (e) { onScreen = e[0].isIntersecting; kick(); }).observe(race);

  if (fail) { if (reduce) { lanes.forEach(function (L) { L.probe.style.opacity = 0; }); } else { t0 = performance.now(); kick(); } }
  else start(winner);
})();
