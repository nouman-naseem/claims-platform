export type Role = 'Claimant' | 'ClaimsOfficer' | 'Manager';
export type ClaimStatus = 'Submitted' | 'UnderReview' | 'InformationRequested' | 'Approved' | 'Rejected';
export type ClaimType = 'Motor' | 'Property';
export const MARKETS = ['Singapore', 'HongKong', 'Australia', 'Japan', 'Malaysia', 'Thailand'] as const;
export type Market = (typeof MARKETS)[number];
export const STATUSES: ClaimStatus[] = ['Submitted', 'UnderReview', 'InformationRequested', 'Approved', 'Rejected'];

export interface User { id: string; email: string; fullName: string; role: Role; market: Market; }
export interface AuthResponse { token: string; expiresAt: string; user: User; }

export interface Policy {
  id: string; policyNumber: string; type: ClaimType; market: Market; currency: string;
  coverageLimit: number; startDate: string; endDate: string; description: string;
}
export interface Officer { id: string; fullName: string; market: Market; openClaims: number; }

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface ClaimSummary {
  id: string; claimNumber: string; type: ClaimType; market: Market; status: ClaimStatus; currency: string;
  claimedAmount: number; incidentDate: string; submittedAt: string; updatedAt: string;
  claimantName: string; assignedOfficerName: string | null; isOverdue: boolean;
}

export interface ClaimDocument { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedBy: string; uploadedAt: string; }
export interface InformationRequest {
  id: string; message: string; requestedBy: string; requestedAt: string; dueDate: string | null;
  response: string | null; respondedAt: string | null; isOpen: boolean;
}
export interface Assessment { id: string; officer: string; estimatedLoss: number; recommendedPayout: number; notes: string; assessedAt: string; }
export interface Assignment { id: string; officerId: string; officer: string; assignedBy: string; assignedAt: string; unassignedAt: string | null; }
export interface Decision { outcome: 'Approved' | 'Rejected'; approvedAmount: number | null; currency: string; reason: string; decidedBy: string; decidedAt: string; }
export interface HistoryEvent { type: string; actor: string; description: string; occurredAt: string; }

export interface ClaimDetail {
  id: string; claimNumber: string; type: ClaimType; market: Market; status: ClaimStatus; currency: string;
  claimedAmount: number; estimatedExposure: number | null; incidentDate: string; incidentLocation: string; description: string;
  submittedAt: string; updatedAt: string; isOverdue: boolean; assignedOfficerId: string | null; assignedOfficerName: string | null;
  policy: Policy; claimant: { id: string; fullName: string; email: string; phone: string; market: Market };
  documents: ClaimDocument[]; informationRequests: InformationRequest[]; assessments: Assessment[];
  assignments: Assignment[]; decision: Decision | null; history: HistoryEvent[];
}

export interface CountBy { key: string; count: number; }
export interface Dashboard {
  totalClaims: number; outstandingClaims: number; pendingDecisions: number; unassignedClaims: number; overdueClaims: number;
  estimatedExposureUsd: number; slaDays: number;
  byStatus: CountBy[]; byType: CountBy[]; byMarket: CountBy[];
  exposureByMarket: { market: string; currency: string; exposureLocal: number; exposureUsd: number }[];
  teamWorkload: { officerId: string; name: string; market: string; openClaims: number; overdueClaims: number; decidedClaims: number; approvedClaims: number; avgDaysToDecision: number }[];
}

export interface ClaimFilters {
  page: number; pageSize: number; search?: string; status?: string; type?: string; market?: string;
  unassigned?: boolean; assignedToMe?: boolean; overdue?: boolean;
}
