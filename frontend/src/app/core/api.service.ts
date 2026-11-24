import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ClaimDetail, ClaimDocument, ClaimFilters, ClaimSummary, Dashboard, Officer, Paged, Policy } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  listClaims(f: ClaimFilters): Observable<Paged<ClaimSummary>> {
    let params = new HttpParams().set('page', f.page).set('pageSize', f.pageSize);
    for (const [k, v] of Object.entries(f)) {
      if (['page', 'pageSize'].includes(k) || v === undefined || v === null || v === '' || v === false) continue;
      params = params.set(k, String(v));
    }
    return this.http.get<Paged<ClaimSummary>>('/api/claims', { params });
  }

  getClaim(id: string): Observable<ClaimDetail> { return this.http.get<ClaimDetail>(`/api/claims/${id}`); }
  myPolicies(): Observable<Policy[]> { return this.http.get<Policy[]>('/api/policies/mine'); }
  officers(): Observable<Officer[]> { return this.http.get<Officer[]>('/api/officers'); }
  dashboard(): Observable<Dashboard> { return this.http.get<Dashboard>('/api/dashboard'); }

  submitClaim(body: { policyId: string; incidentDate: string; incidentLocation: string; description: string; claimedAmount: number }) {
    return this.http.post<ClaimDetail>('/api/claims', body);
  }
  assign(id: string, officerId: string | null) { return this.http.post<ClaimDetail>(`/api/claims/${id}/assign`, { officerId }); }
  requestInfo(id: string, body: { message: string; dueDate: string | null }) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests`, body);
  }
  provideInfo(id: string, requestId: string, response: string) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/information-requests/${requestId}/response`, { response });
  }
  assess(id: string, body: { estimatedLoss: number; recommendedPayout: number; notes: string }) {
    return this.http.post<ClaimDetail>(`/api/claims/${id}/assessments`, body);
  }
  approve(id: string, body: { approvedAmount: number; notes: string | null }) { return this.http.post<ClaimDetail>(`/api/claims/${id}/approve`, body); }
  reject(id: string, reason: string) { return this.http.post<ClaimDetail>(`/api/claims/${id}/reject`, { reason }); }

  upload(id: string, file: File): Observable<ClaimDocument> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ClaimDocument>(`/api/claims/${id}/documents`, form);
  }
  download(id: string, documentId: string): Observable<Blob> {
    return this.http.get(`/api/claims/${id}/documents/${documentId}`, { responseType: 'blob' });
  }
}
