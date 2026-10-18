// --- Entities ---

export interface Bucket {
  id: string;
  name: string;
  owner: string;
  description?: string;
  created_at: string;
  expires_at?: string;
  last_used_at?: string;
  file_count: number;
  total_size: number;
}

export interface BucketFile {
  path: string;
  name: string;
  size: number;
  mime_type: string;
  short_code?: string;
  short_url?: string;
  sha256?: string;
  created_at: string;
  updated_at: string;
}

export interface UploadedFile {
  path: string;
  name: string;
  size: number;
  mime_type: string;
  short_code?: string;
  short_url?: string;
  sha256?: string;
  deduplicated: boolean;
  created_at: string;
  updated_at: string;
}

export interface DirectoryEntry {
  path: string;
  file_count: number;
  total_size: number;
}

export interface BucketChanges {
  name?: string;
  description?: string;
  expires_at?: string;
}

export interface OwnerStats {
  owner: string;
  bucket_count: number;
  file_count: number;
  total_size: number;
}

// --- Requests ---

export interface CreateBucketRequest {
  name: string;
  description?: string;
  expires_in?: string;
}

export interface UpdateBucketRequest {
  name?: string;
  description?: string;
  expires_in?: string;
}

export interface CreateApiKeyRequest {
  name: string;
}

export interface CreateUploadTokenRequest {
  expires_in?: string;
  max_uploads?: number;
}

export interface CreateDashboardTokenRequest {
  expires_in?: string;
}

// --- Responses ---

export interface PaginatedResponse<T> {
  items: T[];
  total: number;
  limit: number;
  offset: number;
}

export interface BucketDetailResponse {
  id: string;
  name: string;
  owner: string;
  description?: string;
  created_at: string;
  expires_at?: string;
  last_used_at?: string;
  file_count: number;
  total_size: number;
  unique_content_count: number;
  unique_content_size: number;
  files: BucketFile[];
  has_more_files: boolean;
}

export interface DirectoryListingResponse {
  files: BucketFile[];
  folders: string[];
  total_files: number;
  total_folders: number;
  limit: number;
  offset: number;
}

export interface FileTreeResponse {
  prefix?: string;
  delimiter: string;
  directories: DirectoryEntry[];
  files: BucketFile[];
  total_files: number;
  total_directories: number;
  cursor?: string;
}

export interface UploadResponse {
  uploaded: UploadedFile[];
}

export interface SyncDeployRequest {
  files: { path: string; sha256: string }[];
}

export interface SyncDeployResponse {
  kept: number;
  deleted: number;
}

export interface VerifyResponse {
  path: string;
  stored_hash: string;
  computed_hash: string;
  valid: boolean;
}

export interface HealthResponse {
  status: string;
  uptime_seconds: number;
  db: string;
}

export interface StatsResponse {
  total_buckets: number;
  total_files: number;
  total_size: number;
  total_keys: number;
  total_downloads: number;
  storage_by_owner: OwnerStats[];
}

export interface ApiKeyResponse {
  key: string;
  prefix: string;
  name: string;
  created_at: string;
}

export interface ApiKeyListItem {
  prefix: string;
  name: string;
  created_at: string;
  last_used_at?: string;
  bucket_count: number;
  file_count: number;
  total_size: number;
}

export interface ApiKeyUsageResponse {
  prefix: string;
  name: string;
  created_at: string;
  last_used_at?: string;
  bucket_count: number;
  file_count: number;
  total_size: number;
  total_downloads: number;
  buckets: Bucket[];
}

export interface UploadTokenResponse {
  token: string;
  bucket_id: string;
  expires_at: string;
  max_uploads?: number;
  uploads_used: number;
}

export interface DashboardTokenResponse {
  token: string;
  expires_at: string;
}

export interface DashboardTokenInfo {
  scope: string;
  expires_at: string;
}

export interface ErrorResponse {
  error: string;
  hint?: string;
}

// --- Options ---

export interface PaginationOptions {
  limit?: number;
  offset?: number;
  sort?: string;
  order?: string;
}

export interface UploadProgress {
  bytesSent: number;
  totalBytes?: number;
  percentage?: number;
}

export interface CarbonFilesClientOptions {
  baseUrl: string;
  apiKey?: string;
  fetch?: typeof globalThis.fetch;
}
