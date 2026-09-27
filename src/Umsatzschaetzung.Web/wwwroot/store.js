// /work lives in IndexedDB: read in before the services start, written back shortly after
// every change to a file under it; the flush it returns writes back at once.
export async function mount(FS, dir) {
    FS.mkdirTree(dir);
    FS.mount(FS.filesystems.IDBFS, { autoPersist: false }, dir);
    await sync(FS, true);

    let running = null, again = false, timer = 0;
    const flush = () => {
        clearTimeout(timer);
        if (running) { again = true; return; }
        running = sync(FS, false).catch(e => console.error('store', e)).finally(() => {
            running = null;
            if (again) { again = false; flush(); }
        });
    };
    const touched = path => { if (String(path).startsWith(dir)) { clearTimeout(timer); timer = setTimeout(flush, 250); } };

    const write = FS.write;
    FS.write = function (stream, ...rest) {
        const n = write.call(this, stream, ...rest);
        touched(stream.path);
        return n;
    };
    for (const name of ['unlink', 'rename', 'rmdir']) {
        const f = FS[name];
        FS[name] = function (path, ...rest) {
            const r = f.call(this, path, ...rest);
            touched(path);
            return r;
        };
    }
    return flush;
}

function sync(FS, populate) {
    return new Promise((resolve, reject) => FS.syncfs(populate, e => e ? reject(e) : resolve()));
}

// One tab at a time: two would each write back their own copy of the same stores.
export function alone() {
    if (!navigator.locks) return Promise.resolve(true);
    return new Promise(resolve => navigator.locks.request('umsatzschaetzung', { ifAvailable: true }, lock => {
        resolve(lock !== null);
        return lock && new Promise(() => { });
    }));
}
