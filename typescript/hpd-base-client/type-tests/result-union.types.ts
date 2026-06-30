import type { BaseResult } from "../src/index.js";

declare const result: BaseResult<{ id: string }>;

if (result.ok) {
  result.value.id;
  // @ts-expect-error successful results do not have error data.
  result.error;
} else {
  result.error.code;
  // @ts-expect-error failed results do not have values.
  result.value;
}
