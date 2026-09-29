import { $, sheet, line, hint, track, untrack, dodge, miss, pin } from './overlay.js';
import { before, heard, skip, speak, stop } from './voice.js';

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
// The slider runs along the beats, each as long as its sentence; wherever it is let go the lesson
// goes on from the start of that beat, with the program as it stood there.
export function player(lesson, running) {
  const beats = lesson.beats;
  const chapters = [...new Set(beats.map(b => b.chapter))];
  const kicker = `${lesson.course} · Übung ${lesson.number}`;
  const length = beats.map(b => Math.max(b.say.length, 40));
  const starts = length.map((_, i) => length.slice(0, i).reduce((a, b) => a + b, 0));
  const slider = $('slider');
  let coach, index = 0, run = 0, misses = 0, solve = null, target = null, up = false, booted = null, dragging = false;
  // The beats the learner acts in that were marked, oldest first, and whether each was done.
  const marked = [];
  running.then(() => { up = true; });

  document.title = `${lesson.title} · ${lesson.course}`;
  sheet(beats[0]?.card || { kicker, title: lesson.title, text: lesson.summary }, true);
  slider.max = starts.at(-1) + length.at(-1);
  $('ticks').replaceChildren(...chapters.slice(1).map(c => {
    const tick = document.createElement('i');
    tick.style.left = `${starts[beats.findIndex(b => b.chapter === c)] / slider.max * 100}%`;
    return tick;
  }));

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
  function boot() {
    return booted ??= (async () => {
      $('card').hidden = true;
      $('stage').classList.remove('gone');
      $('actions').hidden = $('load').hidden = false;
      await running;
      $('actions').hidden = $('load').hidden = true;
      $('card').hidden = false;
    })();
  }

  const chapter = i => `${chapters.indexOf(beats[i].chapter) + 1}. ${beats[i].chapter}`;
  const at = value => starts.findLastIndex(s => s <= value);

  function play() {
    $('start').hidden = true;
    $('card').hidden = false;
    follow();
    go(0);
  }

  async function go(from) {
    const g = ++run;
    for (index = from; index < beats.length; index++) {
      const beat = beats[index];
      if (!up && live(beat)) await boot();
      if (g !== run) return;
      $('chapter').textContent = chapter(index);
      hint();
      if (beat.card) sheet(beat.card, false);
      $('stage').classList.toggle('gone', !beat.card);
      pin(!!beat.show?.page);
      dodge(null);
      await (beat.done ? task(beat, g) : tell(beat, g));
      if (g !== run) return;
    }
    finish();
  }

  // `page` names a control of the lesson itself, such as the pause button, rather than one of the program.
  const own = t => {
    const r = t?.page && $(t.page)?.getBoundingClientRect();
    return r ? [r.x, r.y, r.width, r.height] : null;
  };

  async function tell(beat, g) {
    line(beat.note, 'note');
    $('card').classList.toggle('act', !!beat.show?.act);
    if (beat.show?.page) track('spot', 'on own', () => union(own(beat.show), own(beat.through)), false);
    else if (beat.show) track('spot', 'on', () => union(box(beat.show, false), span(beat.through)));
    await speak(beat);
    if (g !== run) return;
    await sleep(PAUSE + (beat.hold || 0) * 1000);
    if (g !== run) return;
    untrack();
    $('card').classList.remove('act');
  }

  // The sentence asks for it, and a few seconds before it ends the ring shows where; a miss says the hint, a second one
  // rings at once. Once the program shows what the beat leads to, the voice finishes its
  // sentence and the lesson goes on.
  async function task(beat, g) {
    misses = 0;
    target = beat.allow[0] || beat.done.at;
    line(beat.task || beat.say);
    $('card').classList.add('act');
    await coach.Mark(JSON.stringify(beat.undo));
    if (g !== run) return;
    const mark = { at: index, done: false };
    marked.push(mark);
    const solved = new Promise(r => { solve = r; });
    coach.Step(JSON.stringify({ allow: beat.allow, done: beat.done }));
    const spoken = speak(beat);
    const soon = await Promise.race([before(LEAD).then(() => true), solved.then(() => false)]);
    if (g !== run) return;
    if (soon) ring('on');
    await solved;
    if (g !== run) return;
    mark.done = true;
    solve = null;
    $('card').classList.remove('act');
    ring('good');
    hint();
    await spoken;
    if (g !== run) return;
    await sleep(PAUSE + 300);
    if (g !== run) return;
    untrack();
  }

  function finish() {
    index = beats.length;
    coach?.Step(null);
    untrack();
    stop();
    line();
    sheet({ kicker, title: lesson.done?.title || 'Gut gemacht!', text: lesson.done?.text }, true);
    $('start').hidden = false;
    $('start').textContent = 'Noch einmal von vorn';
    $('start').onclick = () => location.reload();
    $('all').hidden = false;
    if (lesson.next) {
      $('next').href = lesson.next.href;
      $('next').textContent = `Weiter: ${lesson.next.title}  ▶`;
      $('next').hidden = false;
      $('start').className = 'quiet';
    }
    $('stage').classList.remove('gone');
  }

  // Back over a beat the learner acted in, the program is put back as it was marked; forwards
  // past one, the coach does what the learner would have. The lesson waits while it does.
  async function move(to) {
    ++run;
    solve?.();
    solve = null;
    coach?.Step(null);
    skip();
    stop();
    untrack();
    hint();
    $('card').classList.remove('act');
    $('time').classList.add('busy');
    if (!up && beats.slice(0, to).some(b => b.done)) await boot();
    try {
      while (marked.length && marked.at(-1).at >= to) await coach.Undo(marked.pop().done);
      for (let j = 0; j < to; j++) {
        if (!beats[j].done) continue;
        let mark = marked.find(m => m.at === j);
        if (!mark) {
          await coach.Mark(JSON.stringify(beats[j].undo));
          marked.push(mark = { at: j, done: false });
        }
        if (mark.done) continue;
        await coach.Do(JSON.stringify(beats[j].do), JSON.stringify(beats[j].done));
        mark.done = true;
      }
    } catch (e) {
      console.error('[lesson]', e);
    }
    $('time').classList.remove('busy');
  }

  // Only the last place the slider was let go counts; moves in between are not made.
  let wanted = null, moving = null;
  function seek(to) {
    wanted = to;
    moving ??= (async () => {
      let to;
      while (wanted !== null) {
        to = wanted;
        wanted = null;
        await move(to);
      }
      moving = null;
      go(to);
    })();
  }

  function follow() {
    if (!dragging && !moving) slider.value = index < beats.length ? starts[index] + length[index] * Math.min(heard(), .999) : slider.max;
    const i = Math.min(at(+slider.value), beats.length - 1);
    slider.setAttribute('aria-valuetext', `${Math.min(index, beats.length - 1) + 1} von ${beats.length}: ${chapter(i)}`);
    requestAnimationFrame(follow);
  }

  slider.addEventListener('input', () => {
    dragging = true;
    $('chapter').textContent = chapter(Math.min(at(+slider.value), beats.length - 1));
  });
  slider.addEventListener('change', () => {
    dragging = false;
    seek(+slider.value >= +slider.max ? beats.length : at(+slider.value));
  });
  slider.addEventListener('keydown', e => {
    const by = { ArrowLeft: -1, ArrowDown: -1, ArrowRight: 1, ArrowUp: 1, Home: -Infinity, End: Infinity }[e.key];
    if (!by) return;
    e.preventDefault();
    seek(Math.max(0, Math.min(beats.length, (wanted ?? index) + by)));
  });

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
