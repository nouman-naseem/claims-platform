import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ClaimDetail, ClaimDocument, Officer } from '../core/models';
import { errMsg, label } from '../core/util';
import { StatusBadgeComponent } from '../shared/shared.components';

@Component({
  selector: 'app-claim-detail',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, CurrencyPipe, DecimalPipe, StatusBadgeComponent],
  template: `
    <div class="container">
      <a routerLink="/claims">&larr; Back to claims</a>
      @if (error) { <div class="alert error" style="margin-top:1rem">{{ error }}</div> }
      @if (notice) { <div class="alert info" style="margin-top:1rem">{{ notice }}</div> }

      @if (claim; as c) {
        <div class="row spread" style="margin:1rem 0">
          <h1 style="margin:0">{{ c.claimNumber }}</h1>
          <div><app-status-badge [status]="c.status" />@if (c.isOverdue) { <span class="badge overdue">Overdue</span> }</div>
        </div>

        <div class="grid cols-2">
          <div class="card">
            <h2>Incident</h2>
            <dl class="kv">
              <dt>Type</dt><dd>{{ c.type }} · {{ label(c.market) }}</dd>
              <dt>Incident date</dt><dd>{{ c.incidentDate | date: 'mediumDate' }}</dd>
              <dt>Location</dt><dd>{{ c.incidentLocation }}</dd>
              <dt>Claimed</dt><dd>{{ c.claimedAmount | currency: c.currency : 'symbol-narrow' : '1.0-2' }}</dd>
              @if (c.estimatedExposure !== null) { <dt>Est. exposure</dt><dd>{{ c.estimatedExposure | currency: c.currency : 'symbol-narrow' : '1.0-2' }}</dd> }
              <dt>Description</dt><dd>{{ c.description }}</dd>
            </dl>
          </div>
          <div class="card">
            <h2>Policy &amp; parties</h2>
            <dl class="kv">
              <dt>Policy</dt><dd>{{ c.policy.policyNumber }} ({{ c.policy.description }})</dd>
              <dt>Coverage limit</dt><dd>{{ c.policy.coverageLimit | currency: c.currency : 'symbol-narrow' : '1.0-0' }}</dd>
              <dt>Claimant</dt><dd>{{ c.claimant.fullName }}<br><span class="muted">{{ c.claimant.email }} · {{ c.claimant.phone }}</span></dd>
              <dt>Officer</dt><dd>{{ c.assignedOfficerName ?? 'Not yet assigned' }}</dd>
              <dt>Submitted</dt><dd>{{ c.submittedAt | date: 'medium' }}</dd>
            </dl>
          </div>
        </div>

        @if (c.decision; as d) {
          <div class="card" [style.border-color]="d.outcome === 'Approved' ? 'var(--ok)' : 'var(--danger)'">
            <h2>Decision: {{ d.outcome }}</h2>
            @if (d.outcome === 'Approved') {
              <p><strong>Settlement: {{ d.approvedAmount | currency: d.currency : 'symbol-narrow' : '1.0-2' }}</strong></p>
            }
            <p>{{ d.reason || 'No additional notes.' }}</p>
            <span class="muted">Decided by {{ d.decidedBy }} on {{ d.decidedAt | date: 'medium' }}</span>
          </div>
        }

        <!-- Information requests -->
        @if (c.informationRequests.length) {
          <div class="card">
            <h2>Additional information</h2>
            @for (r of c.informationRequests; track r.id) {
              <div style="margin-bottom:1rem">
                <strong>{{ r.requestedBy }}</strong> <span class="muted">{{ r.requestedAt | date: 'medium' }}@if (r.dueDate) { · due {{ r.dueDate | date: 'mediumDate' }} }</span>
                <p style="margin:.25rem 0">{{ r.message }}</p>
                @if (r.response) {
                  <p style="margin:.25rem 0;padding-left:1rem;border-left:3px solid var(--border)"><span class="muted">Response {{ r.respondedAt | date: 'medium' }}:</span><br>{{ r.response }}</p>
                } @else if (isClaimant) {
                  <form [formGroup]="responseForm" (ngSubmit)="respond(r.id)">
                    <div class="field"><label for="resp">Your response</label><textarea id="resp" formControlName="response"></textarea></div>
                    <button [disabled]="responseForm.invalid || busy">Send response</button>
                  </form>
                  <p class="muted">Attach any supporting files in the Documents section below.</p>
                } @else { <p class="muted">Awaiting the claimant's response.</p> }
              </div>
            }
          </div>
        }

        <!-- Documents -->
        <div class="card">
          <h2>Documents</h2>
          @for (d of c.documents; track d.id) {
            <div class="row spread"><span>{{ d.fileName }} <span class="muted">· {{ d.sizeBytes / 1024 | number: '1.0-0' }} KB · {{ d.uploadedBy }}</span></span>
              <button class="secondary" (click)="download(d)">Download</button></div>
          } @empty { <p class="muted">No documents uploaded.</p> }
          @if (canUpload) {
            <div class="field" style="margin-top:.75rem"><label for="up">Add a document (PDF, JPG, PNG, DOCX · max 10 MB)</label>
              <input id="up" type="file" accept=".pdf,.jpg,.jpeg,.png,.docx" (change)="upload($event)" [disabled]="busy"></div>
          }
        </div>

        <!-- Staff: assignment -->
        @if (canAssign) {
          <div class="card">
            <h2>Assignment</h2>
            @if (isOfficer) {
              @if (!isMine) { <button (click)="assign(null)" [disabled]="busy">Assign to me</button> } @else { <p class="muted">Assigned to you.</p> }
            } @else {
              <form class="row" [formGroup]="assignForm" (ngSubmit)="assign(assignForm.controls.officerId.value)">
                <select formControlName="officerId" style="max-width:320px">
                  <option value="" disabled>Select an officer</option>
                  @for (o of officers; track o.id) { <option [value]="o.id">{{ o.fullName }} · {{ label(o.market) }} · {{ o.openClaims }} open</option> }
                </select>
                <button [disabled]="assignForm.invalid || busy">{{ c.assignedOfficerId ? 'Reassign' : 'Assign' }}</button>
              </form>
            }
            @if (c.assignments.length) {
              <ul class="timeline" style="margin-top:.75rem">
                @for (a of c.assignments; track a.id) { <li>{{ a.officer }} <span class="muted">assigned by {{ a.assignedBy }} · {{ a.assignedAt | date: 'medium' }}@if (a.unassignedAt) { · until {{ a.unassignedAt | date: 'medium' }} }</span></li> }
              </ul>
            }
          </div>
        }

        <!-- Staff: assessments -->
        @if (!isClaimant && c.assessments.length) {
          <div class="card">
            <h2>Assessments <span class="muted">(internal)</span></h2>
            @for (a of c.assessments; track a.id) {
              <p><strong>{{ a.officer }}</strong> · {{ a.assessedAt | date: 'medium' }}<br>
                Estimated loss {{ a.estimatedLoss | currency: c.currency : 'symbol-narrow' : '1.0-2' }} · Recommended payout {{ a.recommendedPayout | currency: c.currency : 'symbol-narrow' : '1.0-2' }}<br>{{ a.notes }}</p>
            }
          </div>
        }

        <!-- Officer actions -->
        @if (canAct) {
          <div class="grid cols-2">
            <div class="card">
              <h2>Record assessment</h2>
              <form [formGroup]="assessForm" (ngSubmit)="assess()">
                <div class="field"><label for="el">Estimated loss ({{ c.currency }})</label><input id="el" type="number" min="0" step="0.01" formControlName="estimatedLoss"></div>
                <div class="field"><label for="rp">Recommended payout ({{ c.currency }})</label><input id="rp" type="number" min="0" step="0.01" formControlName="recommendedPayout"></div>
                <div class="field"><label for="an">Internal notes</label><textarea id="an" formControlName="notes"></textarea></div>
                <button [disabled]="assessForm.invalid || busy">Save assessment</button>
              </form>
            </div>
            <div class="card">
              <h2>Request additional information</h2>
              <form [formGroup]="infoForm" (ngSubmit)="requestInfo()">
                <div class="field"><label for="im">Message to claimant</label><textarea id="im" formControlName="message"></textarea></div>
                <div class="field"><label for="idue">Respond by (optional)</label><input id="idue" type="date" formControlName="dueDate"></div>
                <button class="secondary" [disabled]="infoForm.invalid || busy">Send request</button>
              </form>
            </div>
            <div class="card">
              <h2>Approve settlement</h2>
              @if (!c.assessments.length) { <p class="muted">Record an assessment before deciding.</p> }
              <form [formGroup]="approveForm" (ngSubmit)="approve()">
                <div class="field"><label for="aa">Approved amount ({{ c.currency }}, max {{ c.policy.coverageLimit | currency: c.currency : 'symbol-narrow' : '1.0-0' }})</label>
                  <input id="aa" type="number" min="0.01" step="0.01" formControlName="approvedAmount"></div>
                <div class="field"><label for="ann">Notes to claimant</label><textarea id="ann" formControlName="notes"></textarea></div>
                <button [disabled]="approveForm.invalid || busy || !c.assessments.length">Approve</button>
              </form>
            </div>
            <div class="card">
              <h2>Reject claim</h2>
              <form [formGroup]="rejectForm" (ngSubmit)="reject()">
                <div class="field"><label for="rr">Reason (shown to claimant)</label><textarea id="rr" formControlName="reason"></textarea></div>
                <button class="danger" [disabled]="rejectForm.invalid || busy || !c.assessments.length">Reject</button>
              </form>
            </div>
          </div>
        }

        <!-- History -->
        <div class="card">
          <h2>Processing history</h2>
          <ul class="timeline">
            @for (h of c.history; track $index) {
              <li><strong>{{ label(h.type) }}</strong> · {{ h.actor }}<br><span class="muted">{{ h.description }} · {{ h.occurredAt | date: 'medium' }}</span></li>
            }
          </ul>
        </div>
      } @else if (!error) { <p class="muted" style="margin-top:1rem">Loading…</p> }
    </div>`,
})
export class ClaimDetailComponent implements OnInit {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);
  private fb = inject(FormBuilder);
  auth = inject(AuthService);

  label = label;
  claim?: ClaimDetail;
  officers: Officer[] = [];
  error = '';
  notice = '';
  busy = false;
  private id = this.route.snapshot.paramMap.get('id')!;

  responseForm = this.fb.nonNullable.group({ response: ['', [Validators.required, Validators.maxLength(4000)]] });
  assignForm = this.fb.nonNullable.group({ officerId: ['', Validators.required] });
  assessForm = this.fb.nonNullable.group({
    estimatedLoss: [null as number | null, [Validators.required, Validators.min(0)]],
    recommendedPayout: [null as number | null, [Validators.required, Validators.min(0)]],
    notes: ['', [Validators.required, Validators.maxLength(4000)]],
  });
  infoForm = this.fb.nonNullable.group({ message: ['', [Validators.required, Validators.maxLength(2000)]], dueDate: '' });
  approveForm = this.fb.nonNullable.group({
    approvedAmount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    notes: '',
  });
  rejectForm = this.fb.nonNullable.group({ reason: ['', [Validators.required, Validators.maxLength(4000)]] });

  get isClaimant() { return this.auth.hasRole('Claimant'); }
  get isOfficer() { return this.auth.hasRole('ClaimsOfficer'); }
  get isManager() { return this.auth.hasRole('Manager'); }
  get isFinal() { return this.claim?.status === 'Approved' || this.claim?.status === 'Rejected'; }
  get isMine() { return !!this.claim && this.claim.assignedOfficerId === this.auth.user()?.id; }
  get canAssign() { return !this.isClaimant && !this.isFinal; }
  get canAct() { return this.isOfficer && this.isMine && this.claim?.status === 'UnderReview'; }
  get canUpload() { return !this.isFinal && (this.isClaimant || (this.isOfficer && this.isMine)); }

  ngOnInit() {
    const failed = (history.state?.uploadFailed ?? []) as string[];
    if (failed.length) this.notice = `Your claim was submitted, but these files could not be uploaded: ${failed.join(', ')}. You can retry below.`;
    this.load();
    if (this.isManager) this.api.officers().subscribe({ next: (o) => (this.officers = o), error: (e) => (this.error = errMsg(e)) });
  }

  private load() {
    this.api.getClaim(this.id).subscribe({ next: (c) => this.apply(c), error: (e) => (this.error = errMsg(e)) });
  }

  private apply(c: ClaimDetail) {
    this.claim = c;
    const last = c.assessments[c.assessments.length - 1];
    if (last && this.approveForm.controls.approvedAmount.value === null) this.approveForm.controls.approvedAmount.setValue(last.recommendedPayout);
  }

  private run(call: Observable<ClaimDetail>, after?: () => void) {
    this.busy = true; this.error = ''; this.notice = '';
    call.subscribe({
      next: (c) => { this.apply(c); after?.(); this.busy = false; },
      error: (e) => { this.error = errMsg(e); this.busy = false; },
    });
  }

  assign(officerId: string | null) { this.run(this.api.assign(this.id, officerId)); }
  respond(requestId: string) { this.run(this.api.provideInfo(this.id, requestId, this.responseForm.controls.response.value), () => this.responseForm.reset()); }
  assess() { this.run(this.api.assess(this.id, this.assessForm.getRawValue() as any), () => this.assessForm.reset()); }
  requestInfo() {
    const v = this.infoForm.getRawValue();
    this.run(this.api.requestInfo(this.id, { message: v.message, dueDate: v.dueDate ? new Date(v.dueDate).toISOString() : null }), () => this.infoForm.reset());
  }
  approve() { const v = this.approveForm.getRawValue(); this.run(this.api.approve(this.id, { approvedAmount: Number(v.approvedAmount), notes: v.notes || null })); }
  reject() { this.run(this.api.reject(this.id, this.rejectForm.controls.reason.value)); }

  upload(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.busy = true; this.error = ''; this.notice = '';
    this.api.upload(this.id, file).subscribe({
      next: () => { input.value = ''; this.busy = false; this.load(); },
      error: (e) => { this.error = errMsg(e); this.busy = false; input.value = ''; },
    });
  }

  download(doc: ClaimDocument) {
    this.api.download(this.id, doc.id).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = doc.fileName; a.click();
        URL.revokeObjectURL(url);
      },
      error: (e) => (this.error = errMsg(e)),
    });
  }
}
