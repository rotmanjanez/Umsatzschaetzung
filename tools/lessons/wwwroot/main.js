import { dotnet } from './_framework/dotnet.js';
import { $ } from './overlay.js';
import { hold, holding, onlive, pace, replay, skip } from './voice.js';
import { player } from './player.js';

let up;
const lesson = player(await fetch('lesson.json', { cache: 'no-cache' }).then(r => r.json()), new Promise(r => { up = r; }));

const pause = $('pause');
const held = on => {
  hold(on);
  pause.setAttribute('aria-pressed', on);
  pause.title = on ? 'Weiter' : 'Pause';
  pause.setAttribute('aria-label', pause.title);
};
pause.onclick = () => held(!holding());
onlive(on => { pause.disabled = !on; });
$('replay').onclick = () => {
  held(false);
  replay();
};
const speed = $('speed');
try { speed.value = localStorage.getItem('speed') || '1'; } catch {}
if (!speed.value) speed.value = '1';
pace(+speed.value);
speed.onchange = () => {
  pace(+speed.value);
  try { localStorage.setItem('speed', speed.value); } catch {}
};
$('stage').onclick = e => {
  if (!e.target.closest('button, a')) skip();
};
$('start').onclick = () => lesson.play();

// The program reads the clipboard through the browser's permission prompt, which would stop a
// learner; a paste key hands it what the page's paste event brings instead.
let take = null;
const own = () => {
  delete navigator.clipboard.read;
  delete navigator.clipboard.readText;
};
addEventListener('keydown', e => {
  if (!(e.metaKey || e.ctrlKey) || e.code !== 'KeyV') return;
  e.preventDefault = () => {};
  const pasted = new Promise(r => { take = r; });
  Object.defineProperty(navigator.clipboard, 'read', { value: undefined, configurable: true });
  Object.defineProperty(navigator.clipboard, 'readText', { value: () => (own(), pasted), configurable: true });
}, true);
document.addEventListener('paste', e => take?.(e.clipboardData.getData('text/plain')), true);
addEventListener('pointerdown', own, true);

// The stores the lesson starts from, laid out in the services' memory, and the case it opens.
const zustand = new URL('zustand/zustand.json', location.href).href;
const { case: open } = await fetch(zustand, { cache: 'no-cache' }).then(r => r.json());
const service = await import('./_content/Umsatzschaetzung.Browser/service.js');
service.serve(zustand);

// The downloads are the first part of the bar; the rest is the program and its services
// starting, which is shown as it would go.
const loaded = share => {
  $('loaded').style.transform = `scaleX(${share})`;
  $('load').setAttribute('aria-valuenow', Math.round(share * 100));
};
const runtime = await dotnet
  .withModuleConfig({ onDownloadResourceProgress: (done, all) => loaded(done / all * .3) })
  .create();
$('load').classList.add('starting');
loaded(.95);
runtime.setModuleImports('service', service);
runtime.setModuleImports('page', await import('./_content/Umsatzschaetzung.Browser/page.js'));
runtime.setModuleImports('coach', {
  ...lesson.events,
  ready() {
    loaded(1);
    up();
  },
});
const name = runtime.getConfig().mainAssemblyName;
lesson.attach((await runtime.getAssemblyExports(name)).Umsatzschaetzung.Lessons.Coach);
await runtime.runMain(name, open ? ['out', open] : ['out']);
