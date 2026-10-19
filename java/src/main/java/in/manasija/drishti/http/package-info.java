/**
 * The pluggable HTTP transport behind {@link in.manasija.drishti.DrishtiClient}.
 *
 * <p>Most applications never touch this package. Implement
 * {@link in.manasija.drishti.http.HttpTransport} only to replace the default
 * {@link in.manasija.drishti.http.JdkHttpTransport}, for example in tests.
 *
 * @since 1.0.0
 */
package in.manasija.drishti.http;
