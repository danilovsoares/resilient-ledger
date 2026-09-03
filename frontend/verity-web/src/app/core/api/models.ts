export enum TransactionType {
  Credit = 1,
  Debit = 2,
}

export interface Transaction {
  id: string;
  type: TransactionType;
  amount: number;
  occurredAt: string;
  businessDate: string;
  description: string | null;
  idempotencyKey: string;
  createdAt: string;
  reversalOfTransactionId: string | null;
  reversedByTransactionId: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface RegisterTransactionRequest {
  type: TransactionType;
  amount: number;
  occurredAt: string;
  description: string | null;
}

export interface DailyBalance {
  businessDate: string;
  totalCredits: number;
  totalDebits: number;
  balance: number;
  updatedAt: string | null;
}

export interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  username: string;
  displayName: string;
}
