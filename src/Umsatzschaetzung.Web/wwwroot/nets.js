// The page's side of weights.worker.js: inputs are staged here while .NET hands them over,
// then moved to the worker with the run; outputs and files move back and wait here until
// .NET copies them out.
const worker = new Worker(new URL('weights.worker.js', import.meta.url));
const waiting = new Map();
let calls = 0;

worker.onmessage = ({ data: { id, value, error } }) => {
    const w = waiting.get(id);
    waiting.delete(id);
    if (error !== undefined) w.reject(new Error(error));
    else w.resolve(value);
};

function call(name, args = [], transfer = []) {
    const id = ++calls;
    return new Promise((resolve, reject) => {
        waiting.set(id, { resolve, reject });
        worker.postMessage({ call: name, id, args }, transfer);
    });
}

const held = new Map();
const staged = new Map();
let handles = 0;

const types = { float32: Float32Array, int64: BigInt64Array };

export function start() {
    navigator.storage?.persist?.().catch(() => false);
    return call('start');
}

export async function read(name) {
    const handle = ++handles;
    held.set(handle, await call('read', [name]));
    return handle;
}

export function length(handle) {
    return held.get(handle).byteLength;
}

export function copy(handle, into) {
    into.set(held.get(handle));
    held.delete(handle);
}

export async function open(name, accelerated) {
    return JSON.stringify(await call('open', [name, accelerated]));
}

export function close(id) {
    staged.delete(id);
    call('close', [id]);
}

export function stage(id, name, type, shape, data) {
    const bytes = data.slice();
    if (!staged.has(id)) staged.set(id, []);
    staged.get(id).push({ name, type, shape: Array.from(shape), data: new types[type](bytes.buffer) });
}

export async function run(id) {
    const feeds = staged.get(id) ?? [];
    staged.delete(id);
    const outputs = await call('run', [id, feeds], feeds.map(f => f.data.buffer));
    const handle = ++handles;
    held.set(handle, outputs.map(o => o.data));
    return JSON.stringify({ handle, outputs: outputs.map(o => ({ name: o.name, type: o.type, shape: o.shape })) });
}

export function take(handle, index, into) {
    const data = held.get(handle)[index];
    into.set(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
}

export function drop(handle) {
    held.delete(handle);
}
