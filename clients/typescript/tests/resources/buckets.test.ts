import { describe, it, expect } from "vitest";
import { HttpTransport } from "../../src/transport.js";
import { createBucketOperations } from "../../src/resources/buckets.js";
import { mockFetch } from "../helpers.js";

describe("BucketOperations", () => {
  it("create() POSTs to /api/buckets", async () => {
    const bucket = { id: "abc123", name: "test", owner: "key1", created_at: "2026-01-01T00:00:00Z", file_count: 0, total_size: 0 };
    const fetch = mockFetch(200, bucket);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const result = await buckets.create({ name: "test" });

    expect(result.id).toBe("abc123");
    expect(result.name).toBe("test");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("POST");
    expect(new URL(req.url).pathname).toBe("/api/buckets");
    const body = await req.json();
    expect(body).toEqual({ name: "test" });
  });

  it("list() GETs paginated results with query params", async () => {
    const response = { items: [], total: 0, limit: 10, offset: 5 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    await buckets.list({ limit: 10, offset: 5, sort: "name", order: "asc", includeExpired: true });

    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/buckets");
    expect(url.searchParams.get("limit")).toBe("10");
    expect(url.searchParams.get("offset")).toBe("5");
    expect(url.searchParams.get("sort")).toBe("name");
    expect(url.searchParams.get("order")).toBe("asc");
    expect(url.searchParams.get("include_expired")).toBe("true");
  });

  it("list() omits query string when no params", async () => {
    const response = { items: [], total: 0, limit: 20, offset: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    await buckets.list();

    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.search).toBe("");
  });

  it('buckets["test-id"].get() fetches BucketDetailResponse', async () => {
    const detail = {
      id: "test-id", name: "My Bucket", owner: "key1", created_at: "2026-01-01T00:00:00Z",
      file_count: 2, total_size: 1024, unique_content_count: 1, unique_content_size: 512,
      files: [{ path: "a.txt", name: "a.txt", size: 512, mime_type: "text/plain", created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" }],
      has_more_files: false,
    };
    const fetch = mockFetch(200, detail);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const result = await buckets["test-id"].get();

    expect(result.id).toBe("test-id");
    expect(result.files).toHaveLength(1);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/test-id");
  });

  it('buckets["test-id"].update() PATCHes bucket', async () => {
    const bucket = { id: "test-id", name: "Updated", owner: "key1", created_at: "2026-01-01T00:00:00Z", file_count: 0, total_size: 0 };
    const fetch = mockFetch(200, bucket);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const result = await buckets["test-id"].update({ name: "Updated" });

    expect(result.name).toBe("Updated");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("PATCH");
    const body = await req.json();
    expect(body).toEqual({ name: "Updated" });
  });

  it('buckets["test-id"].delete() DELETEs bucket', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    await buckets["test-id"].delete();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("DELETE");
    expect(new URL(req.url).pathname).toBe("/api/buckets/test-id");
  });

  it('buckets["test-id"].getSummary() returns plain text', async () => {
    const fetch = mockFetch(200, "Bucket summary text", "text/plain");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const result = await buckets["test-id"].getSummary();

    expect(result).toBe("Bucket summary text");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/test-id/summary");
  });

  it('buckets["test-id"].downloadZip() returns Response', async () => {
    const fetch = mockFetch(200, "zipdata", "application/zip");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const result = await buckets["test-id"].downloadZip();

    expect(result).toBeInstanceOf(Response);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/test-id/zip");
  });

  it('buckets["test-id"].files is accessible', () => {
    const fetch = mockFetch(200, {});
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const resource = buckets["test-id"];
    expect(resource.files).toBeDefined();
    expect(typeof resource.files.list).toBe("function");
  });

  it('buckets["test-id"].tokens is accessible', () => {
    const fetch = mockFetch(200, {});
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    const resource = buckets["test-id"];
    expect(resource.tokens).toBeDefined();
    expect(typeof resource.tokens.create).toBe("function");
  });

  it('buckets["test/id"].get() escapes bucket ID', async () => {
    const detail = {
      id: "test/id", name: "B", owner: "key1", created_at: "2026-01-01T00:00:00Z",
      file_count: 0, total_size: 0, unique_content_count: 0, unique_content_size: 0,
      files: [], has_more_files: false,
    };
    const fetch = mockFetch(200, detail);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const buckets = createBucketOperations(transport);

    await buckets["test/id"].get();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/test%2Fid");
  });
});
