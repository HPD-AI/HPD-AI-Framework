export type ExternalEffectState = "Prepared" | "Dispatched" | "PossibleDispatch" | "Succeeded" | "Failed" | "Cancelled";

export interface PaymentsApiEvidence {
  readonly operationId: string;
  readonly state: ExternalEffectState;
  readonly externalReference: string | null;
  readonly wireVersion: typeof PaymentsApiWireVersion;
}

export interface PaymentsBrowserClientOptions {
  readonly baseUrl: string | URL;
  readonly fetch?: typeof globalThis.fetch;
}

export interface PaymentsRequestOptions { readonly signal?: AbortSignal; }
export interface PaymentsHealth { readonly status: "ready"; readonly version: typeof PaymentsApiWireVersion; }
export interface PaymentsManifest { readonly version: typeof PaymentsApiWireVersion; readonly authorityLogic: false; }

export declare class PaymentsBrowserProtocolError extends Error {
  readonly code: string;
}

export declare class PaymentsBrowserClient {
  constructor(options: PaymentsBrowserClientOptions);
  health(options?: PaymentsRequestOptions): Promise<PaymentsHealth>;
  manifest(options?: PaymentsRequestOptions): Promise<PaymentsManifest>;
}

export declare function decodeEvidence(value: unknown): Readonly<PaymentsApiEvidence>;
export declare const PaymentsApiWireVersion: "hpd.payments.api.v1";
export declare const PaymentsApiRoutes: Readonly<{
  health: "/hpd/payments/v1/health";
  manifest: "/hpd/payments/v1/manifest";
}>;
