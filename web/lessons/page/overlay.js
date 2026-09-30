export const $ = id => document.getElementById(id);
const MARGIN = 20, SUBTITLES = 112;

const remembered = key => { try { return localStorage.getItem(key); } catch { return null; } };
const remember = (key, value) => { try { localStorage.setItem(key, value); } catch {} };
let subtitles = remembered('untertitel') === 'an';
let aim = null, follow = 0, shown = '', always = false, pinned = false;

export function sheet(c, actions) {
  $('kicker').textContent = c.kicker || '';
  $('title').textContent = c.title || '';
  $('text').textContent = c.text || '';
  $('list').replaceChildren(...(c.list || []).map(item => Object.assign(document.createElement('li'), { textContent: item })));
  $('pad').hidden = !c.pad;
  $('actions').hidden = !actions;
}

export function line(text = '', kind = '') {
  $('line').textContent = text;
  $('line').className = kind;
  dodge();
}

export function hint(text = '') {
  $('hint').textContent = text;
  dodge();
}

// A caption shows while subtitles are on, or `always` where there is no voice to hear.
export function caption(text = '', force = false) {
  shown = text;
  always = force;
  const on = !!text && (subtitles || always);
  $('caption').textContent = on ? text : '';
  $('caption').hidden = !on;
  dodge();
}

$('subtitles').setAttribute('aria-pressed', subtitles);
$('subtitles').onclick = () => {
  subtitles = !subtitles;
  remember('untertitel', subtitles ? 'an' : 'aus');
  $('subtitles').setAttribute('aria-pressed', subtitles);
  caption(shown, always);
};

// A spotlight or ring follows its control while the program lays out or scrolls.
export function track(id, look, where, avoid = true) {
  untrack();
  const el = $(id);
  const tick = () => {
    const r = where();
    if (!r) return;
    Object.assign(el.style, { left: `${r[0] - 8}px`, top: `${r[1] - 8}px`, width: `${r[2] + 16}px`, height: `${r[3] + 16}px` });
    el.className = look;
    if (avoid) dodge(r);
  };
  tick();
  follow = setInterval(tick, 250);
}

export function untrack() {
  clearInterval(follow);
  $('spot').className = $('ring').className = '';
}

export function miss(x, y) {
  const d = Object.assign(document.createElement('div'), { className: 'miss' });
  Object.assign(d.style, { left: `${x}px`, top: `${y}px` });
  document.body.append(d);
  setTimeout(() => d.remove(), 1300);
}

const apart = (a, b) => !a || !b || a[0] > b[0] + b[2] + 24 || a[0] + a[2] < b[0] - 24 || a[1] > b[1] + b[3] + 24 || a[1] + a[3] < b[1] - 24;

// With subtitles on, a band is kept for them between the bottom corners, or across the
// bottom where the window is too narrow, and at the top while what is pointed at is below.
// The card takes the first corner that leaves what it points at and the band in view.
// While the lesson points at its own controls, the card stays where they are, only kept in view.
export function pin(on) { pinned = on; }

export function dodge(r = aim) {
  aim = r;
  const card = $('card'), w = card.offsetWidth, W = innerWidth, H = innerHeight;
  if (pinned) {
    card.style.top = `${Math.min(parseFloat(card.style.top) || 0, H - card.offsetHeight - MARGIN)}px`;
    return;
  }
  const rise = card.getBoundingClientRect().top - $('dachs').getBoundingClientRect().top, h = card.offsetHeight + rise;
  const between = Math.min(760, W - 2 * (w + 3 * MARGIN));
  const width = between < 420 ? W - 2 * MARGIN : between;
  const low = [(W - width) / 2, H - MARGIN - SUBTITLES, width, SUBTITLES];
  const band = !subtitles ? null : apart(low, r) ? low : [low[0], MARGIN, width, SUBTITLES];
  $('caption').classList.toggle('top', band !== low);
  $('caption').style.maxWidth = `${width}px`;
  const corners = [[W - w - MARGIN, H - h - MARGIN], [MARGIN, H - h - MARGIN], [W - w - MARGIN, MARGIN + 60], [MARGIN, MARGIN + 60]];
  const clear = ([x, y]) => apart([x, y, w, h], r) && apart([x, y, w, h], band);
  const [x, y] = corners.find(clear) || corners.find(([x, y]) => apart([x, y, w, h], r)) || corners[0];
  Object.assign(card.style, { left: `${x}px`, top: `${y + rise}px` });
}

new ResizeObserver(() => dodge()).observe($('card'));
addEventListener('resize', () => dodge());
