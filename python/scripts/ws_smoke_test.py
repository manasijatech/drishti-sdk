#!/usr/bin/env python3
from __future__ import annotations

import argparse
import asyncio
import contextlib
import os
import sys
from pathlib import Path
from typing import NoReturn, cast

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from drishti_sdk import DrishtiWebSocketSession, SubscribeOptions
from drishti_sdk.websocket import (
    DRISHTI_WS_PRODUCTS,
    DrishtiWebSocketProduct,
    DataEvent,
    ErrorEvent,
    SubscribedEvent,
)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Smoke test for Drishti SDK WebSocket setup.",
    )
    parser.add_argument(
        "--api-key",
        default=os.getenv("DRISHTI_API_KEY", "").strip(),
        help="Drishti API key. Defaults to env DRISHTI_API_KEY.",
    )
    parser.add_argument(
        "--base-url",
        default=os.getenv("DRISHTI_BASE_URL", "https://developers.manasija.in"),
        help="API base URL.",
    )
    parser.add_argument(
        "--product",
        choices=list(DRISHTI_WS_PRODUCTS),
        default="news",
        help="WebSocket product to subscribe to.",
    )
    parser.add_argument(
        "--symbols",
        default="RELIANCE",
        help="Comma-separated symbols (e.g. RELIANCE,TCS).",
    )
    parser.add_argument(
        "--detailed",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Request detailed payloads (default: true).",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=20.0,
        help="Seconds to wait for subscribe ack and (optional) data.",
    )
    parser.add_argument(
        "--expect-data",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Fail if no data message is received before timeout (default: true).",
    )
    parser.add_argument(
        "--live",
        action=argparse.BooleanOptionalAction,
        default=False,
        help="Interactive mode: stay connected and stream events until you quit.",
    )
    return parser


def _exit_failure(message: str) -> NoReturn:
    print(f"FAIL: {message}")
    raise SystemExit(1)


def _parse_symbols(raw: str) -> list[str]:
    out: list[str] = []
    seen: set[str] = set()
    for part in raw.split(","):
        token = part.strip().upper()
        if not token or token in seen:
            continue
        seen.add(token)
        out.append(token)
    return out


async def _run() -> int:
    args = _build_parser().parse_args()
    if not args.api_key:
        _exit_failure("Missing API key. Pass --api-key or set MARKET_STACK_API_KEY.")

    symbols = _parse_symbols(args.symbols)

    print("Starting WebSocket smoke test...")
    print(f"base_url={args.base_url}")
    print(f"product={args.product}")
    if symbols:
        print(f"symbols={symbols}")
    else:
        print("symbols=[] (requests full-feed only if your tier allows it)")
    print(f"detailed={args.detailed}")
    print(f"expect_data={args.expect_data}")
    print(f"timeout={args.timeout}s")

    got_subscribed = False
    got_data = False

    try:
        async with DrishtiWebSocketSession(api_key=args.api_key, base_url=args.base_url) as ws:
            await ws.subscribe(
                SubscribeOptions(
                    product=args.product,
                    symbols=symbols,
                    detailed=args.detailed,
                )
            )
            print("Connected and subscribe sent, waiting for events...")
            events = ws.events()

            if args.live:
                print("LIVE MODE: WebSocket is active.")
                print("Commands: `s` subscribe, `q` quit, `h` help")

                async def event_reader() -> None:
                    while True:
                        event = await anext(events)
                        if isinstance(event, SubscribedEvent):
                            print(
                                "SUBSCRIBED:",
                                {
                                    "product": event.product,
                                    "tier": event.tier,
                                    "full_feed": event.full_feed,
                                    "symbols": event.symbols,
                                    "detailed": event.detailed,
                                },
                            )
                        elif isinstance(event, DataEvent):
                            keys = sorted(event.data.keys()) if isinstance(event.data, dict) else []
                            symbol = event.data.get("symbol") if isinstance(event.data, dict) else None
                            print(f"DATA: channel={event.channel} symbol={symbol} keys={keys}")
                        elif isinstance(event, ErrorEvent):
                            print(f"ERROR: code={event.code} message={event.message}")
                        else:
                            print(f"RAW: {event}")

                async def command_loop() -> None:
                    while True:
                        cmd = (
                            await asyncio.to_thread(
                                input,
                                "\n[live] Enter command (`s` subscribe, `q` quit, `h` help): ",
                            )
                        ).strip().lower()
                        if cmd == "q":
                            return
                        if cmd == "h":
                            print("`s` => subscribe to another feed")
                            print("`q` => quit")
                            continue
                        if cmd != "s":
                            print("Unknown command. Type `h` for help.")
                            continue

                        product = (
                            await asyncio.to_thread(
                                input,
                                f"Product {list(DRISHTI_WS_PRODUCTS)}: ",
                            )
                        ).strip()
                        if product not in DRISHTI_WS_PRODUCTS:
                            print(f"Invalid product: {product}")
                            continue

                        raw_symbols = await asyncio.to_thread(
                            input,
                            "Symbols (comma separated, empty for full-feed if entitled): ",
                        )
                        parsed_symbols = _parse_symbols(raw_symbols)
                        if not parsed_symbols:
                            print("No symbols entered; sending empty list.")

                        detailed_raw = (
                            await asyncio.to_thread(input, "Detailed payload? [Y/n]: ")
                        ).strip().lower()
                        detailed = detailed_raw not in {"n", "no", "false", "0"}

                        await ws.subscribe(
                            SubscribeOptions(
                                product=cast(DrishtiWebSocketProduct, product),
                                symbols=parsed_symbols,
                                detailed=detailed,
                            )
                        )
                        print(
                            f"SUBSCRIBE SENT: product={product} symbols={parsed_symbols} detailed={detailed}"
                        )

                reader_task = asyncio.create_task(event_reader())
                try:
                    await command_loop()
                finally:
                    reader_task.cancel()
                    with contextlib.suppress(asyncio.CancelledError):
                        await reader_task
                print("Closed live session.")
                return 0

            deadline = asyncio.get_running_loop().time() + args.timeout
            while asyncio.get_running_loop().time() < deadline:
                remaining = max(0.1, deadline - asyncio.get_running_loop().time())
                try:
                    event = await asyncio.wait_for(anext(events), timeout=remaining)
                except TimeoutError:
                    break

                if isinstance(event, SubscribedEvent):
                    got_subscribed = True
                    print(
                        "SUBSCRIBED:",
                        {
                            "product": event.product,
                            "tier": event.tier,
                            "full_feed": event.full_feed,
                            "symbols": event.symbols,
                            "detailed": event.detailed,
                        },
                    )
                    if not args.expect_data:
                        break
                elif isinstance(event, DataEvent):
                    got_data = True
                    keys = sorted(event.data.keys()) if isinstance(event.data, dict) else []
                    print(f"DATA: channel={event.channel} keys={keys}")
                    break
                elif isinstance(event, ErrorEvent):
                    _exit_failure(f"Server error: code={event.code} message={event.message}")
                else:
                    print(f"RAW: {event}")
    except Exception as exc:  # noqa: BLE001
        _exit_failure(f"{type(exc).__name__}: {exc}")

    if not got_subscribed:
        _exit_failure("No subscription acknowledgment received before timeout.")
    if args.expect_data and not got_data:
        _exit_failure("Subscribed successfully but no data event received before timeout.")

    print("PASS: WebSocket is set up correctly for this key/product.")
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(_run()))
