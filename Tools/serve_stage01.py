#!/usr/bin/env python3
"""Serve the published Stage 01 WebGL directory without changing cwd."""

from __future__ import annotations

import argparse
import functools
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class StageOneRequestHandler(SimpleHTTPRequestHandler):
    extensions_map = {
        **SimpleHTTPRequestHandler.extensions_map,
        ".data": "application/octet-stream",
        ".js": "text/javascript",
        ".json": "application/json",
        ".wasm": "application/wasm",
    }

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store, max-age=0")
        self.send_header("Pragma", "no-cache")
        super().end_headers()


class StageOneServer(ThreadingHTTPServer):
    allow_reuse_address = True
    daemon_threads = True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8799)
    parser.add_argument("--directory", required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    build_directory = Path(args.directory).expanduser().resolve()
    handler = functools.partial(
        StageOneRequestHandler,
        directory=str(build_directory),
    )
    with StageOneServer((args.bind, args.port), handler) as server:
        print(
            f"Serving Stage 01 from {build_directory} "
            f"at http://{args.bind}:{args.port}",
            flush=True,
        )
        server.serve_forever()


if __name__ == "__main__":
    main()
