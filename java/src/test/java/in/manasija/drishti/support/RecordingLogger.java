package in.manasija.drishti.support;

import java.text.MessageFormat;
import java.util.List;
import java.util.ResourceBundle;
import java.util.concurrent.CopyOnWriteArrayList;

/** System.Logger that keeps every message, for asserting lifecycle log lines. */
public final class RecordingLogger implements System.Logger {

    public record Entry(Level level, String message) {
    }

    public final List<Entry> entries = new CopyOnWriteArrayList<>();

    public List<String> messages() {
        return entries.stream().map(Entry::message).toList();
    }

    @Override
    public String getName() {
        return "recording";
    }

    @Override
    public boolean isLoggable(Level level) {
        return true;
    }

    @Override
    public void log(Level level, ResourceBundle bundle, String msg, Throwable thrown) {
        entries.add(new Entry(level, msg));
    }

    @Override
    public void log(Level level, ResourceBundle bundle, String format, Object... params) {
        entries.add(new Entry(level, params == null || params.length == 0 ? format : MessageFormat.format(format, params)));
    }
}
