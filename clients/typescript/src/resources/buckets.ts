import type { HttpTransport, RequestOptions } from "../transport.js";
import type {
  Bucket,
  BucketDetailResponse,
  CreateBucketRequest,
  PaginatedResponse,
  PaginationOptions,
  UpdateBucketRequest,
  SyncDeployRequest,
  SyncDeployResponse,
} from "../types.js";
import { type FileOperationsWithIndexer, createFileOperations } from "./files.js";
import { UploadTokenOperations } from "./upload-tokens.js";

export class BucketOperations {
  constructor(private readonly transport: HttpTransport) {}

  async create(request: CreateBucketRequest, options?: RequestOptions): Promise<Bucket> {
    return this.transport.post<Bucket>("/api/buckets", request, undefined, options);
  }

  async list(
    params?: PaginationOptions & { includeExpired?: boolean },
    options?: RequestOptions,
  ): Promise<PaginatedResponse<Bucket>> {
    const query: Record<string, string | undefined> = {};
    if (params?.limit !== undefined) query["limit"] = String(params.limit);
    if (params?.offset !== undefined) query["offset"] = String(params.offset);
    if (params?.sort) query["sort"] = params.sort;
    if (params?.order) query["order"] = params.order;
    if (params?.includeExpired) query["include_expired"] = "true";
    return this.transport.get<PaginatedResponse<Bucket>>("/api/buckets", query, options);
  }
}

export class BucketResource {
  readonly files: FileOperationsWithIndexer;
  readonly tokens: UploadTokenOperations;

  constructor(
    private readonly transport: HttpTransport,
    private readonly bucketId: string,
  ) {
    this.files = createFileOperations(transport, bucketId);
    this.tokens = new UploadTokenOperations(transport, bucketId);
  }

  private get basePath(): string {
    return `/api/buckets/${encodeURIComponent(this.bucketId)}`;
  }

  async get(options?: RequestOptions): Promise<BucketDetailResponse> {
    return this.transport.get<BucketDetailResponse>(this.basePath, undefined, options);
  }

  async update(request: UpdateBucketRequest, options?: RequestOptions): Promise<Bucket> {
    return this.transport.patch<Bucket>(this.basePath, request, undefined, options);
  }

  async delete(options?: RequestOptions): Promise<void> {
    return this.transport.delete(this.basePath, options);
  }

  async getSummary(options?: RequestOptions): Promise<string> {
    return this.transport.getPlainText(`${this.basePath}/summary`, options);
  }

  async downloadZip(options?: RequestOptions): Promise<Response> {
    return this.transport.getResponse(`${this.basePath}/zip`, undefined, options);
  }

  async syncDeploy(request: SyncDeployRequest, token?: string, options?: RequestOptions): Promise<SyncDeployResponse> {
    return this.transport.post<SyncDeployResponse>(`${this.basePath}/deploy/sync`, request,
      token ? { token } : undefined, options);
  }
}

export type BucketOperationsWithIndexer = BucketOperations & Record<string, BucketResource>;

export function createBucketOperations(transport: HttpTransport): BucketOperationsWithIndexer {
  const ops = new BucketOperations(transport);
  return new Proxy(ops, {
    get(target, prop, receiver) {
      if (typeof prop === "string" && !(prop in target) && prop !== "then" && prop !== "toJSON") {
        return new BucketResource(transport, prop);
      }
      return Reflect.get(target, prop, receiver);
    },
  }) as BucketOperationsWithIndexer;
}
