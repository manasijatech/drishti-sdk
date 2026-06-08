#!/usr/bin/env python3
"""Listen to every Drishti WebSocket channel using the SDK."""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import json
import os
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from drishti_sdk import DrishtiClient, SubscribeOptions
from drishti_sdk.websocket import (
    DRISHTI_WS_PRODUCTS,
    DataEvent,
    DrishtiWebSocketProduct,
    ErrorEvent,
    SubscribedEvent,
)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Subscribe to all Drishti WebSocket channels and print events.",
    )
    parser.add_argument(
        "--api-key",
        default=os.getenv("DRISHTI_API_KEY", "").strip(),
        help="Drishti API key (default: DRISHTI_API_KEY env var).",
    )
    parser.add_argument(
        "--base-url",
        default=os.getenv("DRISHTI_BASE_URL", "https://developers.manasija.in"),
        help="API base URL.",
    )
    parser.add_argument(
        "--symbols",
        default=os.getenv("DRISHTI_WS_SYMBOLS", "RELIANCE,TCS"),
        help="Comma-separated symbols for symbol-scoped subscriptions.",
    )
    parser.add_argument(
        "--detailed",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Request detailed payloads (default: true).",
    )
    return parser


def _parse_symbols(raw: str) -> list[str]:
    seen: set[str] = set()
    symbols: list[str] = []
    for part in raw.split(","):
        token = part.strip().upper()
        if not token or token in seen:
            continue
        seen.add(token)
        symbols.append(token)
    return symbols


def _preview(data: dict[str, object]) -> str:
    symbol = data.get("symbol")
    headline = data.get("headline") or data.get("title") or data.get("reason")
    parts = [str(symbol)] if symbol else []
    if headline:
        parts.append(str(headline)[:120])
    return " | ".join(parts) if parts else json.dumps(data, default=str)[:200]


async def _run() -> int:
    args = _build_parser().parse_args()
    if not args.api_key:
        print("Missing API key. Pass --api-key or set DRISHTI_API_KEY.", file=sys.stderr)
        return 1

    symbols = _parse_symbols(args.symbols)
    client = DrishtiClient(api_key=args.api_key, base_url=args.base_url)

    def on_channel(channel: DrishtiWebSocketProduct):
        def handler(data: dict[str, object]) -> None:
            print(f"[{channel}] {_preview(data)}")

        return handler

    ws = client.websocket(
        on_open=lambda _: print("[lifecycle] connected"),
        on_close=lambda reason: print(f"[lifecycle] closed: {reason}"),
        on_reconnect_attempt=lambda attempt, delay, reason: print(
            f"[lifecycle] reconnect attempt={attempt} delay={delay:.1f}s reason={reason}"
        ),
        on_reconnect_warning=lambda attempt, reason: print(
            f"[lifecycle] reconnect warning after {attempt} attempts ({reason})"
        ),
        on_news=on_channel("news"),
        on_announcements=on_channel("announcements"),
        on_earnings=on_channel("earnings"),
        on_concalls=on_channel("concalls"),
        on_alerts=on_channel("alerts"),
        on_error=lambda event: print(
            f"[error] {event.message}" + (f" ({event.code})" if event.code else "")
        ),
    )

    async with ws:
        for product in DRISHTI_WS_PRODUCTS:
            await ws.subscribe(
                SubscribeOptions(
                    product=product,
                    symbols=symbols,
                    detailed=args.detailed,
                )
            )
            print(f"[subscribe] queued {product} symbols={symbols or '[]'}")

        print("Listening on all channels. Press Ctrl+C to stop.")
        try:
            async for event in ws.events():
                if isinstance(event, SubscribedEvent):
                    print(
                        f"[subscribed] product={event.product} tier={event.tier} "
                        f"full_feed={event.full_feed} symbols={event.symbols}"
                    )
                elif isinstance(event, DataEvent):
                    pass
                elif isinstance(event, ErrorEvent):
                    pass
                else:
                    print(f"[raw] {event}")
        except asyncio.CancelledError:
            pass

    print("Stopped.")
    return 0


def main() -> None:
    try:
        raise SystemExit(asyncio.run(_run()))
    except KeyboardInterrupt:
        with contextlib.suppress(asyncio.CancelledError):
            print("\nStopped.")


if __name__ == "__main__":
    main()
