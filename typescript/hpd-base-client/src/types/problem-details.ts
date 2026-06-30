/** RFC 7807 ProblemDetails plus HPD extension members. */
export interface HpdProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
}
