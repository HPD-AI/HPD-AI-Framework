export type FileBucketId = string;
export type FileObjectId = string;
export type FileObjectKey = string;
export type FileObjectRevision = string;
export type FileObjectChecksum = string;
export type FileProviderRef = string;

export interface FileObjectRef {
  bucketId: FileBucketId;
  objectId: FileObjectId;
  revision?: FileObjectRevision;
  contentType?: string;
  sizeBytes?: number;
  checksum?: FileObjectChecksum;
  name?: string;
  metadata?: Record<string, string>;
}

export interface FileObjectMetadata {
  bucketId: FileBucketId;
  objectId: FileObjectId;
  key?: FileObjectKey;
  name?: string;
  contentType?: string;
  sizeBytes?: number;
  checksum?: FileObjectChecksum;
  revision?: FileObjectRevision;
  createdAt?: string;
  updatedAt?: string;
  ownerSubjectId?: string;
  tenantId?: string;
  publicMetadata?: Record<string, string>;
}

export interface FileObjectUploadResult {
  metadata: FileObjectMetadata;
  created?: boolean;
}

export interface FileObjectListResult {
  items: FileObjectMetadata[];
  nextCursor?: string;
}

export type FileBucketVisibility = "private" | "publicRead" | "adminOnly" | "custom";

export interface FileBucketDescriptor {
  bucketId: string;
  displayName?: string;
  enabled?: boolean;
  visibility?: FileBucketVisibility;
  maxObjectBytes?: number;
  allowedContentTypes?: string[];
  allowedExtensions?: string[];
  requireChecksum?: boolean;
  allowOverwrite?: boolean;
  defaultCachePolicy?: string;
  policyRefs?: string[];
  providerRef?: string;
  capabilities?: FileBucketCapabilities;
  publicSafeMetadata?: Record<string, string>;
  adminConfigSummary?: FileBucketAdminConfigSummary;
  healthRef?: string;
  diagnosticRefs?: string[];
  descriptorVisibility?: "public" | "admin" | "internal" | "system";
}

export interface FileBucketCapabilities {
  upload?: boolean;
  download?: boolean;
  metadata?: boolean;
  delete?: boolean;
  list?: boolean;
}

export interface FileBucketAdminConfigSummary {
  providerRef?: string;
  storageClassSummary?: string;
  capabilityFlags?: string[];
  nonSecretMetadata?: Record<string, string>;
  diagnosticRefs?: string[];
}

export interface FileObjectHeaders {
  contentType?: string;
  contentLength?: number;
  etag?: string;
  lastModified?: string;
  cacheControl?: string;
  correlationId?: string;
}
