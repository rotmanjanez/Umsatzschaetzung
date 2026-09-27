import { dotnet } from './_framework/dotnet.js';
import { alone } from './_content/Umsatzschaetzung.Browser/store.js';

if (!await alone()) {
    document.getElementById('out').textContent = 'Die Umsatzschätzung ist in diesem Browser schon in einem anderen Tab geöffnet.';
} else {
    navigator.storage?.persist?.().catch(() => false);
    const service = await import('./_content/Umsatzschaetzung.Browser/service.js');
    service.serve();
    const runtime = await dotnet.create();
    runtime.setModuleImports('service', service);
    runtime.setModuleImports('page', await import('./_content/Umsatzschaetzung.Browser/page.js'));
    await runtime.runMain(runtime.getConfig().mainAssemblyName, ['out']);
}
