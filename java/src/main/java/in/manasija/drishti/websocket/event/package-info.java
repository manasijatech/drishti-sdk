/**
 * Typed events delivered by {@link in.manasija.drishti.websocket.DrishtiWebSocketSession}.
 *
 * <p>Every server message becomes exactly one
 * {@link in.manasija.drishti.websocket.event.WebSocketEvent}: a subscribe acknowledgement, a data
 * delivery, an error, a heartbeat, or a raw message the SDK does not recognise.
 *
 * @since 1.0.0
 */
package in.manasija.drishti.websocket.event;
