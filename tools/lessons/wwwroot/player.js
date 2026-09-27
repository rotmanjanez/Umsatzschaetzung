import { $, sheet, line, hint, track, untrack, dodge, miss, pin } from './overlay.js';
import { before, speak, stop } from './voice.js';

const PAUSE = 400;
const LEAD = 3;
const sleep = ms => new Promise(r => setTimeout(r, ms));

function union(a, b) {
  if (!a || !b) return a || b;
  const x = Math.min(a[0], b[0]), y = Math.min(a[1], b[1]);
  return [x, y, Math.max(a[0] + a[2], b[0] + b[2]) - x, Math.max(a[1] + a[3], b[1] + b[3]) - y];
}

// lesson.json as beats. A beat speaks its clip, may cover the program with a card or put a
// spotlight on a control, and a beat with `done` waits for the learner: only what `allow`
// names takes their clicks and keys, and it ends once the program shows `done`.
export function player(lesson, running) {
  const beats = lesson.beats;
  const chapters = [...new Set(beats.map(b => b.chapter))];
  const kicker = `${lesson.course} · Übung ${lesson.number}`;
  let coach, index = -1, misses = 0, solve = null, target = null, up = false;
  running.then(() => { up = true; });

  document.title = `${lesson.title} · ${lesson.course}`;
  sheet(beats[0]?.card || { kicker, title: lesson.title, text: lesson.summary }, true);

  const box = (t, show) => {
    const found = t && coach.Box(JSON.stringify(t), show);
    return found ? JSON.parse(found) : null;
  };
  const span = t => {
    const found = t && coach.Span(JSON.stringify(t));
    return found ? JSON.parse(found) : null;
  };
  const ring = look => track('ring', look, () => box(target, true));

  // The opening cards need no program, so they play while it still starts; the first beat
  // that shows or asks for it waits for it on the last card, with the bar.
  const live = b => !b.card || b.done || (b.show && !b.show.page);
  async function boot() {
    $('card').hidden = true;
    $('stage').classList.remove('gone');
    $('actions').hidden = $('load').hidden = false;
    await running;
    $('actions').hidden = $('load').hidden = true;
    $('card').hidden = false;
  }

  async function play() {
    $('start').hidden = true;
    $('card').hidden = false;
    for (index = 0; index < beats.length; index++) {
      const beat = beats[index];
      if (!up && live(beat)) await boot();
      $('chapter').textContent = `${chapters.indexOf(beat.chapter) + 1}. ${beat.chapter}`;
      $('count').textContent = `${index + 1}/${beats.length}`;
      hint();
      if (beat.card) sheet(beat.card, false);
      $('stage').classList.toggle('gone', !beat.card);
      pin(!!beat.show?.page);
      dodge(null);
      await (beat.done ? task(beat) : tell(beat));
    }
    finish();
  }

  // `page` names a control of the lesson itself, such as the pause button, rather than one of the program.
  const own = t => {
    const r = t?.page && $(t.page)?.getBoundingClientRect();
    return r ? [r.x, r.y, r.width, r.height] : null;
  };

  async function tell(beat) {
    line(beat.note, 'note');
    $('card').classList.toggle('act', !!beat.show?.act);
    if (beat.show?.page) track('spot', 'on own', () => union(own(beat.show), own(beat.through)), false);
    else if (beat.show) track('spot', 'on', () => union(box(beat.show, false), span(beat.through)));
    await speak(beat);
    await sleep(PAUSE + (beat.hold || 0) * 1000);
    untrack();
    $('card').classList.remove('act');
  }

  // The sentence asks for it, and a few seconds before it ends the ring shows where; a miss says the hint, a second one
  // rings at once. Once the program shows what the beat leads to, the voice finishes its
  // sentence and the lesson goes on.
  async function task(beat) {
    misses = 0;
    target = beat.allow[0] || beat.done.at;
    line(beat.task || beat.say);
    $('card').classList.add('act');
    const solved = new Promise(r => { solve = r; });
    coach.Step(JSON.stringify({ allow: beat.allow, done: beat.done }));
    const spoken = speak(beat);
    if (await Promise.race([before(LEAD).then(() => true), solved.then(() => false)])) ring('on');
    await solved;
    solve = null;
    $('card').classList.remove('act');
    ring('good');
    hint();
    await spoken;
    await sleep(PAUSE + 300);
    untrack();
  }

  function finish() {
    coach.Step(null);
    untrack();
    stop();
    sheet({ kicker, title: lesson.done?.title || 'Gut gemacht!', text: lesson.done?.text }, true);
    $('start').hidden = false;
    $('start').textContent = 'Noch einmal von vorn';
    $('start').onclick = () => location.reload();
    $('card').hidden = true;
    $('stage').classList.remove('gone');
  }

  const events = {
    missed(x, y) {
      miss(x, y);
      if (!solve) return;
      hint(beats[index].hint || 'Das war noch nicht die richtige Stelle.');
      if (++misses >= 2) ring('on');
    },
    solved() {
      solve?.();
    },
  };

  return { play, events, attach: c => { coach = c; } };
}
