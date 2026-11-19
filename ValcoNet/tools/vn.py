#!/usr/bin/env python3
"""Minimal Marionette client for ValcoNet dev: run chrome JS, take screenshots.
  vn.py js 'return document.title'      (chrome context, async allowed via 'await')
  vn.py shot out.png                     (X root screenshot)
"""
import json, socket, sys, subprocess

class M:
    def __init__(s, port=2828):
        s.sock = socket.create_connection(("127.0.0.1", port), timeout=60)
        s.buf = b""; s.id = 0; s.read()
        s.cmd("WebDriver:NewSession", {"capabilities": {}})
        s.cmd("Marionette:SetContext", {"value": "chrome"})
    def read(s):
        while b":" not in s.buf: s.buf += s.sock.recv(65536)
        n, rest = s.buf.split(b":", 1); n = int(n)
        while len(rest) < n: rest += s.sock.recv(65536)
        s.buf = rest[n:]; return json.loads(rest[:n])
    def cmd(s, name, params):
        s.id += 1
        data = json.dumps([0, s.id, name, params]).encode()
        s.sock.sendall(str(len(data)).encode() + b":" + data)
        r = s.read()
        if r[2]: raise RuntimeError(r[2])
        return r[3]
    def js(s, src):
        body = "const done = arguments[arguments.length-1]; (async () => {" + src + "})().then(done, e => done('ERR: ' + e + '\\n' + e.stack));"
        return s.cmd("WebDriver:ExecuteAsyncScript", {"script": body, "args": [], "sandbox": None})["value"]

if __name__ == "__main__":
    if sys.argv[1] == "shot":
        subprocess.run(["import", "-display", ":99", "-window", "root", sys.argv[2]], check=True)
    elif sys.argv[1] == "js":
        src = sys.argv[2] if sys.argv[2] != "-" else sys.stdin.read()
        print(json.dumps(M().js(src), indent=1) if True else "")
