package in.manasija.drishti.websocket;

import java.time.Duration;
import java.util.concurrent.TimeUnit;

/**
 * Time source and sleeper for {@link DrishtiWebSocketSession}.
 *
 * <p>The session reads time and sleeps only through this interface. Tests can therefore inject a
 * fake clock and check reconnect backoff without real waiting. Applications should use
 * {@link #system()}.
 *
 * @since 1.0.0
 */
public interface SessionClock {

    /**
     * Returns a monotonic time reading, as {@link System#nanoTime()} does.
     *
     * @return the current reading in nanoseconds; only differences between readings matter
     */
    long nanoTime();

    /**
     * Sleeps for a duration.
     *
     * @param duration the time to sleep; zero or negative returns at once
     * @throws InterruptedException when the sleeping thread is interrupted
     */
    void sleep(Duration duration) throws InterruptedException;

    /**
     * Returns the real system clock.
     *
     * @return a clock backed by {@link System#nanoTime()} and {@link TimeUnit#sleep(long)}
     */
    static SessionClock system() {
        return SystemClock.INSTANCE;
    }
}

/** The real clock. */
enum SystemClock implements SessionClock {
    INSTANCE;

    @Override
    public long nanoTime() {
        return System.nanoTime();
    }

    @Override
    public void sleep(Duration duration) throws InterruptedException {
        if (!duration.isNegative() && !duration.isZero()) {
            TimeUnit.NANOSECONDS.sleep(duration.toNanos());
        }
    }
}
