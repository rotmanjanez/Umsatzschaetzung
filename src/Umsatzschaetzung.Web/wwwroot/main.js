import { dotnet } from './_framework/dotnet.js';
import { alone } from './store.js';

if (!await alone()) {
    document.getElementById('out').textContent = 'Die Umsatzschätzung ist in diesem Browser schon in einem anderen Tab geöffnet.';
} else {
    navigator.storage?.persist?.().catch(() => false);
    const runtime = await dotnet.create();
    runtime.setModuleImports('service', await import('./service.js'));
    runtime.setModuleImports('page', await import('./page.js'));
    await runtime.runMain(runtime.getConfig().mainAssemblyName, ['out']);
}
