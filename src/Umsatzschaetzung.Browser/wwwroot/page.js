// The Bericht fills a page of its own, whose policy applies its styles and loads nothing;
// in a frame of srcdoc it would run under the app's policy, which admits no inline style.
const shell = new URL('bericht.html', import.meta.url).href;

function frame(sandbox) {
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', sandbox);
    frame.src = shell;
    return frame;
}

function fill(frame) {
    const doc = frame.contentDocument;
    if (doc?.URL !== shell || frame.bericht === undefined) return;
    const bericht = new frame.contentWindow.DOMParser().parseFromString(frame.bericht, 'text/html');
    doc.replaceChild(doc.importNode(bericht.documentElement, true), doc.documentElement);
}

export function preview() {
    const preview = frame('allow-same-origin');
    preview.addEventListener('load', () => fill(preview));
    return preview;
}

export function show(preview, html) {
    preview.bericht = html;
    fill(preview);
}

// The Bericht in a hidden frame, printed through the browser's dialog, where it can be saved as PDF.
export function print(html) {
    const printed = frame('allow-same-origin allow-modals');
    printed.style.cssText = 'position:fixed;width:0;height:0;border:0;visibility:hidden';
    printed.bericht = html;
    printed.onload = async () => {
        fill(printed);
        const doc = printed.contentDocument;
        await Promise.all([doc.fonts.ready, ...[...doc.images].map(i => i.decode().catch(() => {}))]);
        printed.contentWindow.addEventListener('afterprint', () => printed.remove());
        printed.contentWindow.print();
    };
    document.body.appendChild(printed);
}
