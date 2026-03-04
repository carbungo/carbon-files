import { describe, it, expect, vi } from "vitest";
import { HttpTransport } from "../../src/transport.js";
import { createFileOperations } from "../../src/resources/files.js";
import { CarbonFilesError } from "../../src/errors.js";
import { mockFetch } from "../helpers.js";

describe("FileOperations", () => {
  it("list() GETs paginated files", async () => {
    const response = { items: [], total: 0, limit: 10, offset: 5 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files.list({ limit: 10, offset: 5, sort: "name", order: "asc" });

    expect(result.items).toEqual([]);
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/buckets/bucket-1/files");
    expect(url.searchParams.get("limit")).toBe("10");
    expect(url.searchParams.get("offset")).toBe("5");
    expect(url.searchParams.get("sort")).toBe("name");
    expect(url.searchParams.get("order")).toBe("asc");
  });

  it("listDirectory() GETs directory listing with path", async () => {
    const response = { files: [], folders: [], total_files: 0, total_folders: 0, limit: 20, offset: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files.listDirectory("docs/images");

    expect(result.files).toEqual([]);
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/buckets/bucket-1/ls");
    expect(url.searchParams.get("path")).toBe("docs/images");
  });

  it("listTree() GETs tree response with delimiter and prefix", async () => {
    const response = { delimiter: "/", directories: [], files: [], total_files: 0, total_directories: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files.listTree({ delimiter: "/", prefix: "docs/" });

    expect(result.delimiter).toBe("/");
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/buckets/bucket-1/tree");
    expect(url.searchParams.get("delimiter")).toBe("/");
    expect(url.searchParams.get("prefix")).toBe("docs/");
  });

  it("listTree() with cursor passes cursor in query", async () => {
    const response = { delimiter: "/", directories: [], files: [], total_files: 0, total_directories: 0, cursor: "next-page" };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files.listTree({ cursor: "abc123", limit: 50 });

    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.searchParams.get("cursor")).toBe("abc123");
    expect(url.searchParams.get("limit")).toBe("50");
  });

  it("upload() sends PUT with filename query param", async () => {
    const uploadResponse = { uploaded: [{ path: "test.txt", name: "test.txt", size: 5, mime_type: "text/plain", deduplicated: false, created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" }] };
    const fetch = mockFetch(200, uploadResponse);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files.upload(new Uint8Array([72, 101, 108, 108, 111]), "test.txt");

    expect(result.uploaded).toHaveLength(1);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("PUT");
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/buckets/bucket-1/upload/stream");
    expect(url.searchParams.get("filename")).toBe("test.txt");
  });

  it("upload() with progress calls onProgress", async () => {
    const uploadResponse = { uploaded: [{ path: "test.txt", name: "test.txt", size: 5, mime_type: "text/plain", deduplicated: false, created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" }] };
    const fetch = mockFetch(200, uploadResponse);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");
    const onProgress = vi.fn();

    await files.upload(new Uint8Array([72, 101, 108, 108, 111]), "test.txt", { onProgress });

    expect(onProgress).toHaveBeenCalled();
    const lastCall = onProgress.mock.calls[onProgress.mock.calls.length - 1]![0];
    expect(lastCall.bytesSent).toBe(5);
    expect(lastCall.totalBytes).toBe(5);
    expect(lastCall.percentage).toBe(100);
  });

  it("upload() with uploadToken passes token in query", async () => {
    const uploadResponse = { uploaded: [] };
    const fetch = mockFetch(200, uploadResponse);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files.upload(new Uint8Array([1]), "f.txt", { uploadToken: "cfu_abc123" });

    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.searchParams.get("token")).toBe("cfu_abc123");
  });

  it("upload() with Uint8Array sends content", async () => {
    const uploadResponse = { uploaded: [] };
    const fetch = mockFetch(200, uploadResponse);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files.upload(new Uint8Array([1, 2, 3]), "data.bin");

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.body).toBeDefined();
  });

  it("upload() deserializes response with dedup fields", async () => {
    const uploadResponse = {
      uploaded: [{
        path: "test.txt", name: "test.txt", size: 5, mime_type: "text/plain",
        sha256: "abc123def456", deduplicated: true,
        created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z",
      }],
    };
    const fetch = mockFetch(200, uploadResponse);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files.upload(new Uint8Array([1]), "test.txt");

    expect(result.uploaded[0]!.sha256).toBe("abc123def456");
    expect(result.uploaded[0]!.deduplicated).toBe(true);
  });
});

describe("FileResource", () => {
  it('files["test.txt"].getMetadata() returns BucketFile', async () => {
    const file = { path: "test.txt", name: "test.txt", size: 100, mime_type: "text/plain", created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
    const fetch = mockFetch(200, file);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files["test.txt"].getMetadata();

    expect(result.path).toBe("test.txt");
    expect(result.size).toBe(100);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/bucket-1/files/test.txt");
  });

  it('files["test.txt"].download() returns Response', async () => {
    const fetch = mockFetch(200, "file content", "application/octet-stream");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files["test.txt"].download();

    expect(result).toBeInstanceOf(Response);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/bucket-1/files/test.txt/content");
  });

  it('files["test.txt"].delete() deletes file', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files["test.txt"].delete();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("DELETE");
    expect(new URL(req.url).pathname).toBe("/api/buckets/bucket-1/files/test.txt");
  });

  it('files["test.txt"].getMetadata() deserializes sha256', async () => {
    const file = { path: "test.txt", name: "test.txt", size: 100, mime_type: "text/plain", sha256: "deadbeef", created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
    const fetch = mockFetch(200, file);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files["test.txt"].getMetadata();

    expect(result.sha256).toBe("deadbeef");
  });

  it('files["test.txt"].patch() sends PATCH with Content-Range', async () => {
    const file = { path: "test.txt", name: "test.txt", size: 100, mime_type: "text/plain", created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
    const fetch = mockFetch(200, file);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files["test.txt"].patch(new Uint8Array([1, 2, 3]), 0, 10, 100);

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("PATCH");
    expect(req.headers.get("Content-Range")).toBe("bytes 0-10/100");
  });

  it('files["test.txt"].append() sends PATCH with X-Append header', async () => {
    const file = { path: "test.txt", name: "test.txt", size: 200, mime_type: "text/plain", created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
    const fetch = mockFetch(200, file);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    await files["test.txt"].append(new Uint8Array([4, 5, 6]));

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("PATCH");
    expect(req.headers.get("X-Append")).toBe("true");
  });

  it('files["test.txt"].patch() throws CarbonFilesError on error', async () => {
    const fetch = mockFetch(404, { error: "File not found" });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    try {
      await files["test.txt"].patch(new Uint8Array([1]), 0, 0, 1);
      expect.unreachable("Should have thrown");
    } catch (e) {
      expect(e).toBeInstanceOf(CarbonFilesError);
      expect((e as CarbonFilesError).status).toBe(404);
    }
  });

  it('files["test.txt"].verify() returns VerifyResponse', async () => {
    const response = { path: "test.txt", stored_hash: "aaa", computed_hash: "aaa", valid: true };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    const result = await files["test.txt"].verify();

    expect(result.path).toBe("test.txt");
    expect(result.stored_hash).toBe("aaa");
    expect(result.computed_hash).toBe("aaa");
    expect(result.valid).toBe(true);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/bucket-1/files/test.txt/verify");
  });

  it('files["test.txt"].verify() throws on 404', async () => {
    const fetch = mockFetch(404, { error: "File not found" });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const files = createFileOperations(transport, "bucket-1");

    try {
      await files["test.txt"].verify();
      expect.unreachable("Should have thrown");
    } catch (e) {
      expect(e).toBeInstanceOf(CarbonFilesError);
      expect((e as CarbonFilesError).status).toBe(404);
    }
  });
});
