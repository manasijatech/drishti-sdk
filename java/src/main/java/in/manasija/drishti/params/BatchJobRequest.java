package in.manasija.drishti.params;

import in.manasija.drishti.ContentRetention;
import java.nio.charset.StandardCharsets;
import java.util.Objects;

/**
 * A batch job submission for {@code POST /v1/batch/jobs}: a JSONL input file plus optional
 * settings.
 *
 * <p>Each line of the file is one {@code POST /v1/daily-summary/} body with a {@code custom_id}.
 * Build the file with {@link in.manasija.drishti.Params#buildBatchInputJsonl(java.util.List)}:
 *
 * <pre>{@code
 * String jsonl = Params.buildBatchInputJsonl(lines);
 * JsonNode job = client.postBatchJobsFile(new BatchJobRequest("watchlist.jsonl", jsonl)
 *         .displayName("Nightly watchlist")
 *         .contentRetention(ContentRetention.NONE));
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#postBatchJobsFile(BatchJobRequest)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/create-batch-job">API reference</a>
 * @since 1.0.0
 */
public final class BatchJobRequest {

    private final String fileName;
    private final byte[] fileContent;
    private String displayName;
    private String metadata;
    private ContentRetention contentRetention;

    /**
     * Creates a submission from raw file bytes.
     *
     * @param fileName the file name sent with the upload, for example {@code input.jsonl}
     * @param fileContent the JSONL file content
     * @throws NullPointerException when either argument is {@code null}
     */
    public BatchJobRequest(String fileName, byte[] fileContent) {
        this.fileName = Objects.requireNonNull(fileName, "fileName");
        this.fileContent = Objects.requireNonNull(fileContent, "fileContent").clone();
    }

    /**
     * Creates a submission from JSONL text, encoded as UTF-8.
     *
     * @param fileName the file name sent with the upload, for example {@code input.jsonl}
     * @param fileContent the JSONL file content
     * @throws NullPointerException when either argument is {@code null}
     */
    public BatchJobRequest(String fileName, String fileContent) {
        this(fileName, Objects.requireNonNull(fileContent, "fileContent").getBytes(StandardCharsets.UTF_8));
    }

    /**
     * Sets a readable name for the job, shown in job listings.
     *
     * @param value the display name, or {@code null} to omit it
     * @return this object, for chaining
     */
    public BatchJobRequest displayName(String value) {
        this.displayName = value;
        return this;
    }

    /**
     * Attaches metadata to the job, returned unchanged when the job is read back.
     *
     * @param value the metadata as a JSON object string, for example {@code {"team":"ops"}}, or
     *     {@code null} to omit it
     * @return this object, for chaining
     */
    public BatchJobRequest metadata(String value) {
        this.metadata = value;
        return this;
    }

    /**
     * Sets the content retention preference for the job's output.
     *
     * @param value the retention preference, or {@code null} for the account default
     * @return this object, for chaining
     */
    public BatchJobRequest contentRetention(ContentRetention value) {
        this.contentRetention = value;
        return this;
    }

    /**
     * Returns the upload file name.
     *
     * @return the file name
     */
    public String fileName() {
        return fileName;
    }

    /**
     * Returns a copy of the file content.
     *
     * @return the JSONL bytes
     */
    public byte[] fileContent() {
        return fileContent.clone();
    }

    /**
     * Returns the display name.
     *
     * @return the display name, or {@code null} when unset
     */
    public String displayName() {
        return displayName;
    }

    /**
     * Returns the metadata JSON string.
     *
     * @return the metadata, or {@code null} when unset
     */
    public String metadata() {
        return metadata;
    }

    /**
     * Returns the content retention preference.
     *
     * @return the preference, or {@code null} when unset
     */
    public ContentRetention contentRetention() {
        return contentRetention;
    }
}
