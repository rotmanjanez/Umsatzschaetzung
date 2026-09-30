"""Starts the published web app in headless Chrome, and a lesson on its program, served with
the headers and the brotli files the server hands out, and fails unless both come up.

    python3 tools/boot/boot.py publish/wwwroot
"""

import json
import re
import shutil
import sys
import threading
import time
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote
from urllib.request import urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "corpus"))
from cdp import Browser, Page, WebSocket  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
PAGE = ROOT / "web" / "lessons" / "page"
HTACCESS = ROOT / "src" / "Umsatzschaetzung.Web" / "htaccess"
TYPES = {".wasm": "application/wasm", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
         ".html": "text/html", ".css": "text/css", ".svg": "image/svg+xml", ".dat": "application/octet-stream"}

# A lesson with nothing to say, on an empty program: what the lesson's page needs to start it.
LESSON = {
    "lektionen/boot/index.html": lambda: (PAGE / "index.html").read_bytes().replace(b"<head>", b'<head>\n<base href="../">', 1),
    "lektionen/boot/lesson.json": lambda: json.dumps({"course": "", "number": 0, "title": "", "summary": "",
                                                      "done": {"title": "", "text": ""}, "beats": []}).encode(),
    "lektionen/boot/zustand/zustand.json": lambda: b'{"case":null,"files":[]}',
}

BOOTED = {
    "": "!document.getElementById('boot') && !!document.querySelector('#out canvas')",
    "lektionen/boot/": "document.getElementById('load').getAttribute('aria-valuenow') === '100'",
}
# Up once it shows so and nothing breaks in the seconds after; a runner without a GPU is slow.
SETTLE = 3
TIMEOUT = 90
BROKEN = re.compile(r"Failed to load AOT module|aot-runtime|MONO_WASM|Assertion at|Uncaught")


# The headers every answer of the server carries, as the app's .htaccess sets them.
def headers() -> list[tuple[str, str]]:
    found = re.findall(r'^  Header set (\S+) "([^"]*)"$', HTACCESS.read_text(), re.M)
    missing = {"Content-Security-Policy", "Cross-Origin-Opener-Policy", "Cross-Origin-Embedder-Policy"} - {name for name, _ in found}
    if missing:
        raise SystemExit(f"{HTACCESS} sets no {', '.join(sorted(missing))} any more")
    return found


class Server(ThreadingHTTPServer):
    request_queue_size = 256


class Served(SimpleHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def __init__(self, *args, fixed, **kwargs):
        self.fixed = fixed
        super().__init__(*args, **kwargs)

    def log_message(self, *args):
        pass

    def end_headers(self):
        for name, value in self.fixed:
            self.send_header(name, value)
        super().end_headers()

    def do_GET(self):
        path = unquote(self.path.split("?")[0]).lstrip("/")
        if path == "" or path.endswith("/"):
            path += "index.html"
        if path in LESSON:
            return self.answer(LESSON[path](), path)
        if path.startswith("lektionen/") and (PAGE / path.removeprefix("lektionen/")).is_file():
            return self.answer((PAGE / path.removeprefix("lektionen/")).read_bytes(), path)
        file = Path(self.directory) / path
        if not file.is_file():
            return self.send_error(404)
        compressed = file.with_name(file.name + ".br")
        if "br" in self.headers.get("Accept-Encoding", "") and compressed.is_file():
            return self.answer(compressed.read_bytes(), path, "br")
        self.answer(file.read_bytes(), path)

    def answer(self, body: bytes, path: str, encoding: str | None = None):
        self.send_response(200)
        self.send_header("Content-Type", TYPES.get(Path(path).suffix, "application/octet-stream"))
        if encoding:
            self.send_header("Content-Encoding", encoding)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def chrome() -> str:
    for name in ("google-chrome", "chromium", "chrome", "chromium-browser"):
        if found := shutil.which(name):
            return found
    raise SystemExit("no Chrome on the path")


def boots(browser: Browser, url: str, booted: str) -> tuple[bool, list[str]]:
    target = browser.request("/json/new?about:blank", "PUT")
    page = Page(WebSocket("127.0.0.1", browser.port, "/devtools/" + target["webSocketDebuggerUrl"].split("/devtools/")[1]))
    said = []

    def call(method, **params):
        page.counter += 1
        page.ws.send(json.dumps({"id": page.counter, "method": method, "params": params}))
        while True:
            message = json.loads(page.ws.recv())
            if message.get("id") == page.counter:
                return message.get("result", {})
            params = message.get("params", {})
            if message.get("method") == "Runtime.consoleAPICalled" and params["type"] in ("error", "assert"):
                said.append(" ".join(str(a.get("value", a.get("description", ""))) for a in params["args"]))
            elif message.get("method") == "Runtime.exceptionThrown":
                details = params["exceptionDetails"]
                said.append("Uncaught " + (details.get("exception", {}).get("description") or details.get("text", "")))
            elif message.get("method") == "Log.entryAdded" and params["entry"]["level"] == "error":
                said.append(f'{params["entry"]["text"]} {params["entry"].get("url", "")}'.strip())

    call("Runtime.enable")
    call("Log.enable")
    call("Page.enable")
    call("Page.navigate", url=url)
    deadline = time.time() + TIMEOUT
    up = None
    try:
        while time.time() < deadline:
            shown = call("Runtime.evaluate", expression=booted, returnByValue=True)["result"].get("value") is True
            if any(BROKEN.search(s) for s in said):
                return False, said
            up = (up or time.time()) if shown else None
            if up and time.time() - up > SETTLE:
                return True, said
            time.sleep(0.25)
        return False, said + [f"not up after {TIMEOUT} s"]
    finally:
        page.close()
        urlopen(f"http://127.0.0.1:{browser.port}/json/close/{target['id']}").close()


def main():
    served = Path(sys.argv[1]).resolve()
    server = Server(("127.0.0.1", 0), partial(Served, directory=str(served), fixed=headers()))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    browser = Browser(chrome(), ["--headless=new", "--no-sandbox", "--no-first-run", "--mute-audio"])
    failed = False
    for path, booted in BOOTED.items():
        url = f"http://127.0.0.1:{server.server_port}/{path}"
        start = time.time()
        ok, said = boots(browser, url, booted)
        print(f"{'up' if ok else 'DOWN'} after {time.time() - start:.1f} s: {url}", flush=True)
        for line in said:
            print("   ", line[:400], flush=True)
        failed |= not ok
    browser.close()
    server.shutdown()
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
