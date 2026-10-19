package in.manasija.drishti.support;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.Closeable;
import java.io.DataInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * Minimal RFC 6455 server for exercising the real {@link java.net.http.WebSocket} client: one
 * connection at a time, text frames only, no extensions. Good enough for loopback tests.
 */
public final class MiniWebSocketServer implements Closeable {

    /** Reacts to each text frame from the client; reply with {@link Peer#send(String)}. */
    @FunctionalInterface
    public interface Handler {
        void onText(Peer peer, String text) throws IOException;
    }

    /** The connected client, as seen by the server. */
    public static final class Peer {
        private final OutputStream out;
        public final Map<String, String> requestHeaders;

        Peer(OutputStream out, Map<String, String> requestHeaders) {
            this.out = out;
            this.requestHeaders = requestHeaders;
        }

        public synchronized void send(String text) throws IOException {
            writeFrame(out, 0x1, text.getBytes(StandardCharsets.UTF_8));
        }

        public synchronized void close(int code) throws IOException {
            byte[] payload = {(byte) (code >> 8), (byte) code};
            writeFrame(out, 0x8, payload);
        }
    }

    private final ServerSocket serverSocket;
    private final Thread acceptThread;
    private final Integer rejectStatus;
    private final Handler handler;
    public final List<Map<String, String>> handshakes = new CopyOnWriteArrayList<>();
    public final List<String> received = new CopyOnWriteArrayList<>();
    private volatile boolean closed;

    /**
     * @param rejectStatus when non-null, answer every handshake with this HTTP status instead of 101
     * @param handler reacts to client text frames
     */
    public MiniWebSocketServer(Integer rejectStatus, Handler handler) throws IOException {
        this.serverSocket = new ServerSocket(0, 50, InetAddress.getLoopbackAddress());
        this.rejectStatus = rejectStatus;
        this.handler = handler;
        this.acceptThread = new Thread(this::acceptLoop, "mini-ws-server");
        acceptThread.setDaemon(true);
        acceptThread.start();
    }

    public String baseUrl() {
        return "http://127.0.0.1:" + serverSocket.getLocalPort();
    }

    private void acceptLoop() {
        while (!closed) {
            try (Socket socket = serverSocket.accept()) {
                serve(socket);
            } catch (IOException e) {
                if (closed) {
                    return;
                }
            }
        }
    }

    private void serve(Socket socket) throws IOException {
        InputStream in = socket.getInputStream();
        OutputStream out = socket.getOutputStream();
        Map<String, String> headers = readHandshake(in);
        handshakes.add(headers);
        if (rejectStatus != null) {
            out.write(("HTTP/1.1 " + rejectStatus + " Rejected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
                    .getBytes(StandardCharsets.US_ASCII));
            out.flush();
            return;
        }
        String accept = acceptKey(headers.get("sec-websocket-key"));
        out.write(("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                + "Sec-WebSocket-Accept: " + accept + "\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
        out.flush();
        Peer peer = new Peer(out, headers);
        DataInputStream data = new DataInputStream(in);
        while (!closed) {
            int b0 = data.read();
            if (b0 < 0) {
                return;
            }
            int opcode = b0 & 0x0F;
            int b1 = data.readUnsignedByte();
            long length = b1 & 0x7F;
            if (length == 126) {
                length = data.readUnsignedShort();
            } else if (length == 127) {
                length = data.readLong();
            }
            byte[] mask = new byte[4];
            if ((b1 & 0x80) != 0) {
                data.readFully(mask);
            }
            byte[] payload = new byte[(int) length];
            data.readFully(payload);
            for (int i = 0; i < payload.length; i++) {
                payload[i] ^= mask[i % 4];
            }
            if (opcode == 0x8) {
                peer.close(1000);
                return;
            }
            if (opcode == 0x9) {
                writeFrame(out, 0xA, payload);
                continue;
            }
            if (opcode == 0x1) {
                String text = new String(payload, StandardCharsets.UTF_8);
                received.add(text);
                handler.onText(peer, text);
            }
        }
    }

    private static Map<String, String> readHandshake(InputStream in) throws IOException {
        // Read byte-by-byte so no websocket frame bytes are swallowed by a buffer.
        ByteArrayOutputStream head = new ByteArrayOutputStream();
        int matched = 0;
        byte[] end = {'\r', '\n', '\r', '\n'};
        while (matched < 4) {
            int b = in.read();
            if (b < 0) {
                throw new IOException("handshake ended early");
            }
            head.write(b);
            matched = b == end[matched] ? matched + 1 : (b == '\r' ? 1 : 0);
        }
        Map<String, String> headers = new LinkedHashMap<>();
        BufferedReader reader = new BufferedReader(new InputStreamReader(
                new java.io.ByteArrayInputStream(head.toByteArray()), StandardCharsets.US_ASCII));
        headers.put(":request-line", reader.readLine());
        String line;
        while ((line = reader.readLine()) != null && !line.isEmpty()) {
            int colon = line.indexOf(':');
            headers.put(line.substring(0, colon).trim().toLowerCase(Locale.ROOT), line.substring(colon + 1).trim());
        }
        return headers;
    }

    private static String acceptKey(String key) {
        try {
            MessageDigest sha1 = MessageDigest.getInstance("SHA-1");
            byte[] digest = sha1.digest((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").getBytes(StandardCharsets.US_ASCII));
            return Base64.getEncoder().encodeToString(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private static void writeFrame(OutputStream out, int opcode, byte[] payload) throws IOException {
        out.write(0x80 | opcode);
        if (payload.length < 126) {
            out.write(payload.length);
        } else if (payload.length < 65536) {
            out.write(126);
            out.write(payload.length >> 8);
            out.write(payload.length);
        } else {
            out.write(127);
            for (int shift = 56; shift >= 0; shift -= 8) {
                out.write((int) ((long) payload.length >> shift));
            }
        }
        out.write(payload);
        out.flush();
    }

    @Override
    public void close() throws IOException {
        closed = true;
        serverSocket.close();
    }
}
