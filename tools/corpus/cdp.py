import atexit
import base64
import json
import os
import shutil
import socket
import struct
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


class WebSocket:
    def __init__(self, host, port, path):
        self.sock = socket.create_connection((host, port), timeout=120)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        key = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall(
            f"GET {path} HTTP/1.1\r\nHost: {host}:{port}\r\nUpgrade: websocket\r\n"
            f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n"
            f"Sec-WebSocket-Version: 13\r\n\r\n".encode())
        self.stream = self.sock.makefile("rb")
        while True:
            line = self.stream.readline()
            if line in (b"\r\n", b"\n", b""):
                break

    def send(self, text):
        payload = text.encode()
        mask = os.urandom(4)
        n = len(payload)
        header = bytearray([0x81])
        if n < 126:
            header.append(0x80 | n)
        elif n < 65536:
            header.append(0x80 | 126)
            header += struct.pack(">H", n)
        else:
            header.append(0x80 | 127)
            header += struct.pack(">Q", n)
        header += mask
        masked = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        self.sock.sendall(bytes(header) + masked)

    def _read(self, n):
        data = self.stream.read(n)
        if data is None or len(data) < n:
            raise ConnectionError("websocket closed")
        return data

    def recv(self):
        chunks = []
        while True:
            first, second = self._read(2)
            fin = first & 0x80
            opcode = first & 0x0F
            length = second & 0x7F
            if length == 126:
                length = struct.unpack(">H", self._read(2))[0]
            elif length == 127:
                length = struct.unpack(">Q", self._read(8))[0]
            payload = self._read(length) if length else b""
            if opcode == 0x9:
                self.sock.sendall(b"\x8a\x80" + os.urandom(4))
                continue
            if opcode == 0x8:
                raise ConnectionError("websocket closed by peer")
            if opcode == 0xA:
                continue
            chunks.append(payload)
            if fin:
                return b"".join(chunks).decode("utf-8", "replace")

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


class Page:
    def __init__(self, ws):
        self.ws = ws
        self.counter = 0

    def send(self, method, **params):
        self.counter += 1
        ident = self.counter
        self.ws.send(json.dumps({"id": ident, "method": method, "params": params}))
        while True:
            message = json.loads(self.ws.recv())
            if message.get("id") != ident:
                continue
            if "error" in message:
                raise RuntimeError(f"{method}: {message['error']}")
            return message.get("result", {})

    def wait(self, event, timeout=60):
        deadline = time.time() + timeout
        while time.time() < deadline:
            message = json.loads(self.ws.recv())
            if message.get("method") == event:
                return message.get("params", {})
        raise TimeoutError(event)

    def metrics(self, width, height, scale=1):
        self.send("Emulation.setDeviceMetricsOverride", width=width, height=height,
                  deviceScaleFactor=scale, mobile=False)

    def navigate(self, url):
        self.send("Page.navigate", url=url)
        self.wait("Page.loadEventFired")

    def evaluate(self, expression):
        result = self.send("Runtime.evaluate", expression=expression, returnByValue=True,
                           awaitPromise=True)
        if result.get("exceptionDetails"):
            raise RuntimeError(result["exceptionDetails"].get("text", "evaluate failed"))
        return result["result"].get("value")

    def screenshot(self, clip=None, fmt="png"):
        params = {"format": fmt, "captureBeyondViewport": True}
        if clip:
            params["clip"] = clip
        return base64.b64decode(self.send("Page.captureScreenshot", **params)["data"])

    def close(self):
        self.ws.close()


class Browser:
    def __init__(self, chrome, flags=()):
        self.dir = tempfile.mkdtemp(prefix="cdp-")
        self.port = free_port()
        self.process = subprocess.Popen(
            [chrome, *flags, "--remote-debugging-port=%d" % self.port,
             "--user-data-dir=" + self.dir, "about:blank"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.wait_ready()
        atexit.register(self.close)

    def wait_ready(self, timeout=40):
        deadline = time.time() + timeout
        while time.time() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError("chrome exited during startup")
            try:
                with urllib.request.urlopen(
                        f"http://127.0.0.1:{self.port}/json/version", timeout=1) as r:
                    json.load(r)
                    return
            except (urllib.error.URLError, OSError, ValueError):
                time.sleep(0.05)
        raise TimeoutError("chrome devtools did not come up")

    def page(self):
        target = self.request("/json/new?about:blank", "PUT")
        ws = target["webSocketDebuggerUrl"].split("/devtools/")[1]
        page = Page(WebSocket("127.0.0.1", self.port, "/devtools/" + ws))
        page.send("Page.enable")
        page.send("Runtime.enable")
        page.target = target["id"]
        return page

    def request(self, path, method="GET"):
        url = f"http://127.0.0.1:{self.port}{path}"
        try:
            req = urllib.request.Request(url, method=method)
            with urllib.request.urlopen(req, timeout=10) as r:
                return json.load(r)
        except urllib.error.HTTPError:
            with urllib.request.urlopen(url, timeout=10) as r:
                return json.load(r)

    def close(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
        shutil.rmtree(self.dir, ignore_errors=True)


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]
