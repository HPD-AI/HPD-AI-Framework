import { createBaseClient } from "@hpd/base-client";
import { createBaseFilesClient } from "../src/index.js";

const files = createBaseFilesClient(createBaseClient({ baseUrl: "/base" }));
const bucket = files.bucket("avatars");

// @ts-expect-error upload requires a file key.
await bucket.upload(new Blob(["hello"]), {});
// @ts-expect-error signed URLs are deferred.
await bucket.signedUrl("obj-1");
// @ts-expect-error resumable uploads are deferred.
await bucket.createResumableUpload(new Blob(["hello"]), { key: "hello.txt" });
// @ts-expect-error bucket CRUD is deferred.
await files.createBucket({ bucketId: "avatars" });
// @ts-expect-error auth lifecycle belongs to the base client/application.
await files.login();
