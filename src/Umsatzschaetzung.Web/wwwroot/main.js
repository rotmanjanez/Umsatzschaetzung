import { dotnet } from './_framework/dotnet.js';
import { alone } from './_content/Umsatzschaetzung.Browser/store.js';

const boot = document.getElementById('boot');
const status = boot.querySelector('p');
const bar = boot.querySelector('.bar');
let shown = 0;
const show = part => { shown = Math.max(shown, part); bar.firstChild.style.width = `${100 * shown}%`; };

if (!await alone()) {
    status.textContent = 'Die Umsatzschätzung ist in diesem Browser schon in einem anderen Tab geöffnet.';
    bar.remove();
} else {
    navigator.storage?.persist?.().catch(() => false);
    const out = document.getElementById('out');
    new MutationObserver((_, watch) => { boot.remove(); watch.disconnect(); }).observe(out, { childList: true });
    const service = await import('./_content/Umsatzschaetzung.Browser/service.js');
    service.serve();
    const runtime = await dotnet
        .withModuleConfig({ onDownloadResourceProgress: (loaded, started) => show(loaded / started) })
        .create()
        .catch(error => {
            status.textContent = 'Die Umsatzschätzung konnte nicht geladen werden. Bitte die Seite neu laden.';
            bar.remove();
            throw error;
        });
    show(1);
    status.textContent = 'Umsatzschätzung wird gestartet …';
    runtime.setModuleImports('service', service);
    runtime.setModuleImports('page', await import('./_content/Umsatzschaetzung.Browser/page.js'));
    await runtime.runMain(runtime.getConfig().mainAssemblyName, ['out']);
}
