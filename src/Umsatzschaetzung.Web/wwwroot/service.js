// The page's side of service.worker.js, where the services run: a call's attachments are
// staged here while .NET hands them over, the answer's wait here until .NET copies them out.
const worker = new Worker(new URL('service.worker.js', import.meta.url), { type: 'module' });
const waiting = new Map();
const answers = new Map();
let staged = [];

const started = new Promise((resolve, reject) => {
    worker.addEventListener('message', function ready({ data }) {
        if (data.started === undefined) return;
        worker.removeEventListener('message', ready);
        data.started ? resolve() : reject(new Error(data.error));
    });
});

worker.addEventListener('message', ({ data }) => {
    if (data.log) return console[data.log]('[service]', data.text);
    const w = waiting.get(data.id);
    if (!w) return;
    waiting.delete(data.id);
    if (data.canceled) w.reject(new DOMException('abgebrochen', 'AbortError'));
    else {
        answers.set(data.id, data);
        w.resolve(data.id);
    }
});

addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') worker.postMessage({ flush: true }); });

export function start() {
    return started;
}

export function attach(data) {
    staged.push(data.slice());
}

export function call(id, method, json) {
    const blobs = staged;
    staged = [];
    return new Promise((resolve, reject) => {
        waiting.set(id, { resolve, reject });
        worker.postMessage({ id, method, json, blobs }, blobs.map(b => b.buffer));
    });
}

export function cancel(id) {
    worker.postMessage({ cancel: id });
}

export function json(id) {
    return answers.get(id).json;
}

export function fault(id) {
    const f = answers.get(id).fault;
    return f ? JSON.stringify(f) : null;
}

export function count(id) {
    return answers.get(id).blobs.length;
}

export function length(id, index) {
    return answers.get(id).blobs[index].byteLength;
}

export function take(id, index, into) {
    into.set(answers.get(id).blobs[index]);
}

export function done(id) {
    answers.delete(id);
}
