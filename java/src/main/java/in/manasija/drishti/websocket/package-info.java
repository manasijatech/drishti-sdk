/**
 * Websocket streaming: near-live announcements, earnings, conference calls, news, block deals and
 * alerts, pushed as they are published.
 *
 * <p>Start with {@link in.manasija.drishti.websocket.DrishtiWebSocketSession}, created from
 * {@link in.manasija.drishti.DrishtiClient#websocket()}. It manages connecting, subscribing,
 * heartbeats and reconnect backoff. {@link in.manasija.drishti.websocket.WebSocketMessages} exposes
 * the raw wire format for applications that manage their own sockets.
 *
 * @see <a href="https://drishti.manasija.in/docs/guides/websockets">Websocket streams reference</a>
 * @since 1.0.0
 */
package in.manasija.drishti.websocket;
