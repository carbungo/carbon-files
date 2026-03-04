import { describe, it, expect } from "vitest";
import { HttpTransport } from "../../src/transport.js";
import { createApiKeyOperations } from "../../src/resources/keys.js";
import { StatsOperations } from "../../src/resources/stats.js";
import { createShortUrlOperations } from "../../src/resources/short-urls.js";
import { DashboardOperations } from "../../src/resources/dashboard.js";
import { UploadTokenOperations } from "../../src/resources/upload-tokens.js";
import { mockFetch } from "../helpers.js";

describe("ApiKeyOperations", () => {
  it("keys.create() POSTs to /api/keys", async () => {
    const response = { key: "cf4_full_key", prefix: "cf4_abc", name: "test", created_at: "2026-01-01T00:00:00Z" };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const keys = createApiKeyOperations(transport);

    const result = await keys.create({ name: "test" });

    expect(result.key).toBe("cf4_full_key");
    expect(result.prefix).toBe("cf4_abc");
    expect(result.name).toBe("test");
    expect(result.created_at).toBe("2026-01-01T00:00:00Z");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("POST");
    expect(new URL(req.url).pathname).toBe("/api/keys");
    const body = await req.json();
    expect(body).toEqual({ name: "test" });
  });

  it("keys.list() GETs paginated list", async () => {
    const response = { items: [{ prefix: "cf4_abc", name: "test", created_at: "2026-01-01T00:00:00Z", bucket_count: 1, file_count: 5, total_size: 1024 }], total: 1, limit: 20, offset: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const keys = createApiKeyOperations(transport);

    const result = await keys.list({ limit: 20, offset: 0 });

    expect(result.items).toHaveLength(1);
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.pathname).toBe("/api/keys");
    expect(url.searchParams.get("limit")).toBe("20");
  });

  it('keys["cf4_abc"].revoke() DELETEs', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const keys = createApiKeyOperations(transport);

    await keys["cf4_abc"].revoke();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("DELETE");
    expect(new URL(req.url).pathname).toBe("/api/keys/cf4_abc");
  });

  it('keys["cf4_abc"].getUsage() returns ApiKeyUsageResponse', async () => {
    const response = {
      prefix: "cf4_abc", name: "test", created_at: "2026-01-01T00:00:00Z",
      bucket_count: 2, file_count: 10, total_size: 2048, total_downloads: 5,
      buckets: [{ id: "b1", name: "Bucket 1", owner: "cf4_abc", created_at: "2026-01-01T00:00:00Z", file_count: 5, total_size: 1024 }],
    };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const keys = createApiKeyOperations(transport);

    const result = await keys["cf4_abc"].getUsage();

    expect(result.prefix).toBe("cf4_abc");
    expect(result.total_downloads).toBe(5);
    expect(result.buckets).toHaveLength(1);
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/keys/cf4_abc/usage");
  });

  it('keys["cf4/special"].revoke() escapes prefix', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const keys = createApiKeyOperations(transport);

    await keys["cf4/special"].revoke();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/keys/cf4%2Fspecial");
  });
});

describe("StatsOperations", () => {
  it("stats.get() returns StatsResponse", async () => {
    const response = {
      total_buckets: 5, total_files: 100, total_size: 50000, total_keys: 3, total_downloads: 42,
      storage_by_owner: [{ owner: "key1", bucket_count: 2, file_count: 50, total_size: 25000 }],
    };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const stats = new StatsOperations(transport);

    const result = await stats.get();

    expect(result.total_buckets).toBe(5);
    expect(result.total_files).toBe(100);
    expect(result.total_downloads).toBe(42);
    expect(result.storage_by_owner).toHaveLength(1);
    expect(result.storage_by_owner[0]!.owner).toBe("key1");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/stats");
  });
});

describe("ShortUrlOperations", () => {
  it('shortUrls["abc123"].delete() DELETEs', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const shortUrls = createShortUrlOperations(transport);

    await shortUrls["abc123"].delete();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("DELETE");
    expect(new URL(req.url).pathname).toBe("/api/short/abc123");
  });

  it('shortUrls["a/b"].delete() escapes code', async () => {
    const fetch = mockFetch(200, "");
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const shortUrls = createShortUrlOperations(transport);

    await shortUrls["a/b"].delete();

    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/short/a%2Fb");
  });
});

describe("DashboardOperations", () => {
  it("dashboard.createToken() POSTs to /api/tokens/dashboard", async () => {
    const response = { token: "jwt.token.here", expires_at: "2026-01-02T00:00:00Z" };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const dashboard = new DashboardOperations(transport);

    const result = await dashboard.createToken({ expires_in: "24h" });

    expect(result.token).toBe("jwt.token.here");
    expect(result.expires_at).toBe("2026-01-02T00:00:00Z");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("POST");
    expect(new URL(req.url).pathname).toBe("/api/tokens/dashboard");
    const body = await req.json();
    expect(body).toEqual({ expires_in: "24h" });
  });

  it("dashboard.createToken() with no request posts empty body", async () => {
    const response = { token: "jwt.token.here", expires_at: "2026-01-02T00:00:00Z" };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const dashboard = new DashboardOperations(transport);

    await dashboard.createToken();

    const req = fetch.mock.calls[0]![0] as Request;
    const body = await req.json();
    expect(body).toEqual({});
  });

  it("dashboard.getCurrentUser() returns DashboardTokenInfo", async () => {
    const response = { scope: "admin", expires_at: "2026-01-02T00:00:00Z" };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "admin-key", fetch);
    const dashboard = new DashboardOperations(transport);

    const result = await dashboard.getCurrentUser();

    expect(result.scope).toBe("admin");
    expect(result.expires_at).toBe("2026-01-02T00:00:00Z");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/tokens/dashboard/me");
  });
});

describe("UploadTokenOperations", () => {
  it("tokens.create() POSTs to /api/buckets/{id}/tokens", async () => {
    const response = { token: "cfu_abc123", bucket_id: "bucket-1", expires_at: "2026-01-02T00:00:00Z", uploads_used: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const tokens = new UploadTokenOperations(transport, "bucket-1");

    const result = await tokens.create({ expires_in: "1h", max_uploads: 10 });

    expect(result.token).toBe("cfu_abc123");
    expect(result.bucket_id).toBe("bucket-1");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("POST");
    expect(new URL(req.url).pathname).toBe("/api/buckets/bucket-1/tokens");
    const body = await req.json();
    expect(body).toEqual({ expires_in: "1h", max_uploads: 10 });
  });

  it("tokens.create() escapes bucket ID", async () => {
    const response = { token: "cfu_abc", bucket_id: "b/1", expires_at: "2026-01-02T00:00:00Z", uploads_used: 0 };
    const fetch = mockFetch(200, response);
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const tokens = new UploadTokenOperations(transport, "b/1");

    await tokens.create({ expires_in: "1h" });

    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/api/buckets/b%2F1/tokens");
  });
});
