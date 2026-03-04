import { HttpTransport } from "./transport.js";
import type { CarbonFilesClientOptions } from "./types.js";
import { HealthOperations } from "./resources/health.js";
import { StatsOperations } from "./resources/stats.js";
import { type BucketOperationsWithIndexer, createBucketOperations } from "./resources/buckets.js";
import { type ApiKeyOperationsWithIndexer, createApiKeyOperations } from "./resources/keys.js";
import { type ShortUrlOperationsWithIndexer, createShortUrlOperations } from "./resources/short-urls.js";
import { DashboardOperations } from "./resources/dashboard.js";

export class CarbonFilesClient {
  readonly health: HealthOperations;
  readonly buckets: BucketOperationsWithIndexer;
  readonly keys: ApiKeyOperationsWithIndexer;
  readonly stats: StatsOperations;
  readonly shortUrls: ShortUrlOperationsWithIndexer;
  readonly dashboard: DashboardOperations;

  constructor(baseUrl: string, apiKey?: string);
  constructor(options: CarbonFilesClientOptions);
  constructor(baseUrlOrOptions: string | CarbonFilesClientOptions, apiKey?: string) {
    const options: CarbonFilesClientOptions =
      typeof baseUrlOrOptions === "string"
        ? { baseUrl: baseUrlOrOptions, apiKey }
        : baseUrlOrOptions;

    const transport = new HttpTransport(options.baseUrl, options.apiKey, options.fetch);

    this.health = new HealthOperations(transport);
    this.buckets = createBucketOperations(transport);
    this.keys = createApiKeyOperations(transport);
    this.stats = new StatsOperations(transport);
    this.shortUrls = createShortUrlOperations(transport);
    this.dashboard = new DashboardOperations(transport);
  }
}
