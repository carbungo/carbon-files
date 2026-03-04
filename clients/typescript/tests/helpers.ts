import { vi } from "vitest";

export function mockFetch(status: number, body: unknown, contentType = "application/json") {
  return vi.fn().mockResolvedValue(
    new Response(
      typeof body === "string" ? body : JSON.stringify(body),
      { status, headers: { "Content-Type": contentType } },
    ),
  );
}
