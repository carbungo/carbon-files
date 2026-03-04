import { describe, it, expect } from "vitest";
import { HttpTransport } from "../../src/transport.js";
import { HealthOperations } from "../../src/resources/health.js";
import { CarbonFilesError } from "../../src/errors.js";
import { mockFetch } from "../helpers.js";

describe("HealthOperations", () => {
  it("check() returns HealthResponse", async () => {
    const fetch = mockFetch(200, { status: "healthy", uptime_seconds: 42, db: "ok" });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const health = new HealthOperations(transport);

    const result = await health.check();

    expect(result.status).toBe("healthy");
    expect(result.uptime_seconds).toBe(42);
    expect(result.db).toBe("ok");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(new URL(req.url).pathname).toBe("/healthz");
  });

  it("check() throws CarbonFilesError on error", async () => {
    const fetch = mockFetch(503, { error: "Service unavailable", hint: "DB down" });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const health = new HealthOperations(transport);

    try {
      await health.check();
      expect.unreachable("Should have thrown");
    } catch (e) {
      expect(e).toBeInstanceOf(CarbonFilesError);
      const err = e as CarbonFilesError;
      expect(err.status).toBe(503);
      expect(err.error).toBe("Service unavailable");
    }
  });
});
