import { describe, it, expect, vi } from "vitest";
import { CarbonFilesClient } from "../src/client.js";

describe("CarbonFilesClient", () => {
  it("string constructor initializes all operation groups", () => {
    const client = new CarbonFilesClient("https://example.com", "my-key");
    expect(client.health).toBeDefined();
    expect(client.buckets).toBeDefined();
    expect(client.keys).toBeDefined();
    expect(client.stats).toBeDefined();
    expect(client.shortUrls).toBeDefined();
    expect(client.dashboard).toBeDefined();
  });

  it("options constructor accepts custom fetch", () => {
    const customFetch = vi.fn();
    const client = new CarbonFilesClient({ baseUrl: "https://example.com", fetch: customFetch as any });
    expect(client).toBeDefined();
  });
});
