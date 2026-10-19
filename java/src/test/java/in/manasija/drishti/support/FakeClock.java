package in.manasija.drishti.support;

import in.manasija.drishti.websocket.SessionClock;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;

/** Deterministic clock: time only moves when the code under test sleeps or waits on a socket. */
public final class FakeClock implements SessionClock {

    public long nanos = Duration.ofSeconds(1000).toNanos();
    public final List<Duration> sleeps = new ArrayList<>();

    @Override
    public long nanoTime() {
        return nanos;
    }

    @Override
    public void sleep(Duration duration) {
        sleeps.add(duration);
        advance(duration);
    }

    public void advance(Duration duration) {
        if (!duration.isNegative()) {
            nanos += duration.toNanos();
        }
    }

    /** Recorded sleeps in seconds, for compact assertions. */
    public List<Double> sleepSeconds() {
        List<Double> out = new ArrayList<>();
        sleeps.forEach(d -> out.add(d.toNanos() / 1e9));
        return out;
    }
}
