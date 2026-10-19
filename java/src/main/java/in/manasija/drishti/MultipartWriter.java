package in.manasija.drishti;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;

/** Builds a {@code multipart/form-data} body (RFC 7578) in memory. */
final class MultipartWriter {

    private static final String CRLF = "\r\n";

    private final String boundary;
    private final ByteArrayOutputStream out = new ByteArrayOutputStream();

    MultipartWriter(String boundary) {
        this.boundary = boundary;
    }

    MultipartWriter field(String name, String value) {
        writePartHeader("Content-Disposition: form-data; name=\"" + quote(name) + "\"" + CRLF);
        write(value.getBytes(StandardCharsets.UTF_8));
        write(CRLF);
        return this;
    }

    MultipartWriter file(String name, String fileName, String contentType, byte[] content) {
        writePartHeader("Content-Disposition: form-data; name=\"" + quote(name) + "\"; filename=\"" + quote(fileName) + "\""
                + CRLF + "Content-Type: " + contentType + CRLF);
        write(content);
        write(CRLF);
        return this;
    }

    byte[] finish() {
        write("--" + boundary + "--" + CRLF);
        return out.toByteArray();
    }

    private void writePartHeader(String headers) {
        write("--" + boundary + CRLF + headers + CRLF);
    }

    private void write(String text) {
        write(text.getBytes(StandardCharsets.UTF_8));
    }

    private void write(byte[] bytes) {
        out.write(bytes, 0, bytes.length);
    }

    /** Escapes a header parameter value: quotes and line breaks would end the parameter early. */
    private static String quote(String value) {
        return value.replace("\"", "%22").replace("\r", "%0D").replace("\n", "%0A");
    }
}
