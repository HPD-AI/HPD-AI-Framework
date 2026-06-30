import { createBaseClient } from "@hpd/base-client";
import { createBaseFilesClient, type FileObjectUploadResult } from "../src/index.js";

const base = createBaseClient({ baseUrl: "/base" });
const files = createBaseFilesClient(base);
const bucket = files.bucket("avatars");
const file = new File(["hello"], "hello.txt", { type: "text/plain" });
const blob = new Blob(["hello"], { type: "text/plain" });

const uploaded: FileObjectUploadResult = await bucket.upload(file, { key: "users/u1/hello.txt" });
uploaded.metadata.objectId satisfies string;

await bucket.upload(blob, { key: "users/u1/blob.txt", contentType: "text/plain" });
const response: Response = await bucket.download(uploaded.metadata.objectId);
response.body satisfies ReadableStream<Uint8Array> | null;
const downloadedBlob: Blob = await bucket.downloadBlob(uploaded.metadata.objectId);
downloadedBlob.size satisfies number;
const buffer: ArrayBuffer = await bucket.downloadArrayBuffer(uploaded.metadata.objectId);
buffer.byteLength satisfies number;
const deleted: void = await bucket.delete(uploaded.metadata.objectId);
deleted satisfies void;
