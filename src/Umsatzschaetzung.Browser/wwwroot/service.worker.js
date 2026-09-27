// The services in a runtime of their own: the stores, the reading of documents and the matching
// never hold up the page. /work is laid out before they start, as the page's first message says.
import { dotnet } from '../../_framework/dotnet.js';
import { load, mount } from './store.js';

// What goes wrong here shows in the page's console, where it is looked for.
for (const level of ['error', 'warn']) {
    const write = console[level];
    console[level] = (...args) => {
        write(...args);
        postMessage({ log: level, text: args.map(String).join(' ') });
    };
}

const calls = new Map();
let exports, flush = () => { };

async function start({ from }) {
    const runtime = await dotnet.create();
    flush = from ? await load(runtime.Module.FS, '/work', from) : await mount(runtime.Module.FS, '/work');
    runtime.setModuleImports('nets', await import('./nets.js'));
    runtime.setModuleImports('service', {
        blob: (id, index, into) => into.set(calls.get(id).blobs[index]),
        length: (id, index) => calls.get(id).blobs[index].byteLength,
        attach: (id, data) => calls.get(id).answer.push(data.slice()),
    });
    await runtime.runMain(runtime.getConfig().mainAssemblyName, ['service']);
    exports = (await runtime.getAssemblyExports('Umsatzschaetzung.Browser')).Umsatzschaetzung.Browser.Serve;
}

let ready;

onmessage = async ({ data }) => {
    if (data.start) {
        ready = start(data.start).then(() => postMessage({ started: true }), e => postMessage({ started: false, error: String(e?.message ?? e) }));
        return;
    }
    await ready;
    if (data.flush) return flush();
    if (data.cancel !== undefined) return exports.Cancel(data.cancel);
    const { id, method, json, blobs } = data;
    calls.set(id, { blobs, answer: [] });
    try {
        const reply = JSON.parse(await exports.Handle(id, method, json, blobs.length));
        if (reply.canceled) return postMessage({ id, canceled: true });
        const answer = calls.get(id).answer;
        postMessage({ id, json: reply.json, fault: reply.fault, blobs: answer }, answer.map(b => b.buffer));
    } finally {
        calls.delete(id);
    }
};
