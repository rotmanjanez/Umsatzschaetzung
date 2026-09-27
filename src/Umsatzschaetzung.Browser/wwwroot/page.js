// The Bericht in a hidden frame, printed through the browser's dialog, where it can be saved as PDF.
export function print(html) {
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', 'allow-same-origin allow-modals');
    frame.style.cssText = 'position:fixed;width:0;height:0;border:0;visibility:hidden';
    frame.onload = () => {
        frame.contentWindow.addEventListener('afterprint', () => frame.remove());
        frame.contentWindow.print();
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
}
