import { $, caption } from './overlay.js';

const CHARS_PER_SECOND = 14;
const LINE = 84;
const voice = $('voice');
let hush = null, go = null, clip = null, held = false, cues = [], progress = () => 0, left = () => NaN, force = false, showing = null, soon = null;
let live = () => {}, rate = 1;
const dachs = $('dachs');
const changed = () => {
  live(held || !!go || !voice.paused);
  dachs.classList.toggle('talk', !held && !!hush);
};
voice.onplay = voice.onpause = changed;

// Subtitles come a sentence at a time, a long one cut at a comma or between words near its
// middle, and each part is shown for its share of the take.
function cut(text) {
  if (text.length <= LINE) return [text];
  const mid = text.length / 2;
  const near = re => [...text.matchAll(re)].map(m => m.index + m[0].length)
    .filter(at => at > text.length * .2 && at < text.length * .8)
    .sort((a, b) => Math.abs(a - mid) - Math.abs(b - mid))[0];
  const at = near(/[,;:–]\s+/g) ?? near(/\s+/g);
  return at ? [...cut(text.slice(0, at).trim()), ...cut(text.slice(at).trim())] : [text];
}

function subtitles(say) {
  const parts = say.split(/(?<=[.!?…])\s+/).filter(Boolean).flatMap(cut);
  let seen = 0;
  return parts.map(text => ({ text, at: (seen += text.length) - text.length })).map(c => ({ ...c, at: c.at / seen }));
}

function follow() {
  if (!hush) return;
  const p = progress();
  const now = cues.findLast(c => c.at <= p)?.text ?? '';
  if (now !== showing) caption(showing = now, force);
  if (soon && left() <= soon.lead) soon.go();
  requestAnimationFrame(follow);
}

// The beat's clip; without one, or where the browser will not play it, the sentence is
// shown and given the time it takes to read. Held, neither starts nor goes on.
export function speak({ say = '', audio = null }) {
  clip = audio;
  cues = subtitles(say);
  force = false;
  showing = null;
  return new Promise(resolve => {
    let timer = 0;
    const end = () => {
      if (hush !== end) return;
      hush = go = null;
      clearTimeout(timer);
      voice.onended = null;
      caption(showing = null);
      soon?.go();
      changed();
      resolve();
    };
    hush = end;
    const silent = () => {
      if (!say) return end();
      const length = say.length / (CHARS_PER_SECOND * rate) * 1000;
      let spent = 0, since = 0;
      force = true;
      showing = null;
      progress = () => (spent + (since && Date.now() - since)) / length;
      left = () => (length - spent - (since && Date.now() - since)) / 1000;
      go = on => {
        if (on) {
          since = Date.now();
          timer = setTimeout(end, length - spent);
        } else if (since) {
          clearTimeout(timer);
          spent += Date.now() - since;
          since = 0;
        }
      };
      if (!held) go(true);
      changed();
    };
    follow();
    if (!audio) return silent();
    voice.src = audio;
    voice.onended = end;
    progress = () => voice.duration ? voice.currentTime / voice.duration : 0;
    left = () => (voice.duration - voice.currentTime) / voice.playbackRate;
    go = on => on ? voice.play().catch(silent) : voice.pause();
    if (!held) go(true);
    changed();
  });
}

// Resolves once the clip playing has no more than `lead` seconds to go, or has ended.
export function before(lead) {
  if (!hush) return Promise.resolve();
  return new Promise(go => {
    soon = { lead, go: () => { soon = null; go(); } };
  });
}

export function hold(on) {
  held = on;
  if (go) go(!on);
  else if (on) voice.pause();
  else if (clip && voice.currentTime && !voice.ended) voice.play().catch(() => {});
  changed();
}

export function pace(r) {
  rate = voice.defaultPlaybackRate = voice.playbackRate = r;
}

export const onlive = f => { live = f; };

export const holding = () => held;

export const skip = () => hush?.();

export const stop = () => voice.pause();

export function replay() {
  if (!clip) return;
  voice.currentTime = 0;
  voice.play();
}
