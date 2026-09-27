// The runtime and the weights, off the page's thread. Weights and the runtime come from this
// origin under a path named for their release, so a fetched file never changes: it is checked
// against its hash once and kept in the cache for good.
const store = 'umsatz-weights';

// This script is served from _content/Umsatzschaetzung.Browser/; the weights sit at the app's root.
const root = new URL('../../', location.href);
const at = url => new URL(url, root).href;

const manifest = fetch(at('weights.json'), { cache: 'no-cache' }).then(r => {
    if (!r.ok) throw new Error(`weights.json: ${r.status}`);
    return r.json();
});

const kept = manifest.then(async m => {
    const cache = await caches.open(store);
    const wanted = new Set(Object.values(m.files).map(f => at(f.url)));
    for (const r of await cache.keys()) if (!wanted.has(r.url)) await cache.delete(r);
    return cache;
});

const loading = new Map();

async function bytes(name) {
    const f = (await manifest).files[name];
    if (!f) throw new Error(`Unbekannte Modelldatei: ${name}`);
    const cache = await kept;
    const url = at(f.url);
    const hit = await cache.match(url);
    if (hit) return new Uint8Array(await hit.arrayBuffer());
    const r = await fetch(url);
    if (!r.ok) throw new Error(`${f.url}: ${r.status}`);
    const data = await r.arrayBuffer();
    const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', data)), b => b.toString(16).padStart(2, '0')).join('');
    if (hash !== f.sha256) throw new Error(`${f.url}: Prüfsumme stimmt nicht`);
    await cache.put(url, new Response(data, { headers: { 'content-type': 'application/octet-stream' } }));
    return new Uint8Array(data);
}

function once(name) {
    if (!loading.has(name)) loading.set(name, bytes(name).finally(() => loading.delete(name)));
    return loading.get(name);
}

let adapter, runtime;

function ort() {
    return runtime ??= (async () => {
        const m = await manifest;
        const [lib, wasm] = await Promise.all([import(at(m.runtime.module)), once(m.runtime.wasm)]);
        lib.env.wasm.wasmBinary = wasm;
        lib.env.wasm.numThreads = crossOriginIsolated ? Math.min(4, navigator.hardwareConcurrency || 1) : 1;
        if (adapter) lib.env.webgpu.adapter = adapter;
        return lib;
    })();
}

const sessions = new Map();
let ids = 0, queue = Promise.resolve();

// Handed over, not copied: the page takes the buffers.
const moved = (value, buffers) => ({ moved: true, value, buffers });

// One call into the runtime at a time, whatever its session: a run on the GPU suspends the
// runtime's stack, and a call started meanwhile would overwrite it.
function serial(work) {
    const turn = queue.then(work);
    queue = turn.catch(() => { });
    return turn;
}

const port = p => ({ name: p.name, shape: (p.shape ?? []).map(d => typeof d === 'number' ? d : -1) });

const calls = {
    async start() {
        adapter = navigator.gpu ? await navigator.gpu.requestAdapter().catch(() => null) : null;
        return adapter !== null;
    },

    async read(name) {
        const data = await once(name);
        return moved(data, [data.buffer]);
    },

    async open(name, accelerated) {
        const [lib, model] = await Promise.all([ort(), once(name)]);
        const session = await serial(() => lib.InferenceSession.create(model, {
            executionProviders: accelerated && adapter ? ['webgpu', 'wasm'] : ['wasm'],
            graphOptimizationLevel: 'all',
        }));
        const id = ++ids;
        sessions.set(id, { session, lib });
        const meta = (names, metadata) => names.map((n, i) => port(metadata?.[i] ?? { name: n }));
        return { id, inputs: meta(session.inputNames, session.inputMetadata), outputs: meta(session.outputNames, session.outputMetadata) };
    },

    close(id) {
        const s = sessions.get(id);
        sessions.delete(id);
        if (s) serial(() => s.session.release());
    },

    async run(id, feeds) {
        const s = sessions.get(id);
        const inputs = {};
        for (const f of feeds) inputs[f.name] = new s.lib.Tensor(f.type, f.data, f.shape);
        const out = await serial(() => s.session.run(inputs));
        const outputs = s.session.outputNames.map(name => {
            const t = out[name];
            const own = t.data.byteOffset === 0 && t.data.byteLength === t.data.buffer.byteLength;
            const data = own ? t.data : t.data.slice();
            t.dispose?.();
            return { name, type: t.type, shape: t.dims, data };
        });
        return moved(outputs, outputs.map(o => o.data.buffer));
    },
};

onmessage = async ({ data: { call, id, args } }) => {
    try {
        const r = await calls[call](...args);
        if (r?.moved) postMessage({ id, value: r.value }, r.buffers);
        else postMessage({ id, value: r });
    } catch (e) {
        postMessage({ id, error: String(e?.message ?? e) });
    }
};
