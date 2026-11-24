import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../core/api.service';
import { Policy } from '../core/models';
import { errMsg } from '../core/util';

@Component({
  selector: 'app-claim-new',
  standalone: true,
  imports: [ReactiveFormsModule, CurrencyPipe],
  template: `
    <div class="container">
      <h1>Report an incident</h1>
      @if (error) { <div class="alert error">{{ error }}</div> }
      @if (policies.length === 0 && loaded) {
        <div class="alert info">No policies are linked to your account yet, so there is nothing to claim against.</div>
      }
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <div class="field"><label for="pol">Policy</label>
          <select id="pol" formControlName="policyId">
            <option value="" disabled>Select a policy</option>
            @for (p of policies; track p.id) { <option [value]="p.id">{{ p.policyNumber }} · {{ p.type }} · {{ p.description }}</option> }
          </select>
          @if (selected; as p) {
            <span class="muted">Coverage limit {{ p.coverageLimit | currency: p.currency : 'symbol-narrow' : '1.0-0' }}, valid {{ p.startDate.slice(0, 10) }} to {{ p.endDate.slice(0, 10) }}.</span>
          }
        </div>
        <div class="grid cols-2">
          <div class="field"><label for="d">Incident date</label><input id="d" type="date" formControlName="incidentDate" [max]="today"></div>
          <div class="field"><label for="a">Estimated amount{{ selected ? ' (' + selected.currency + ')' : '' }}</label><input id="a" type="number" min="1" step="0.01" formControlName="claimedAmount"></div>
        </div>
        <div class="field"><label for="l">Incident location</label><input id="l" formControlName="incidentLocation"></div>
        <div class="field"><label for="desc">What happened?</label><textarea id="desc" formControlName="description"></textarea>
          @if (form.controls.description.touched && form.controls.description.invalid) { <span class="err">Please describe the incident (at least 10 characters).</span> }</div>
        <div class="field"><label for="f">Supporting documents (PDF, JPG, PNG, DOCX · max 10 MB each)</label>
          <input id="f" type="file" multiple accept=".pdf,.jpg,.jpeg,.png,.docx" (change)="pick($event)"></div>
        <button type="submit" [disabled]="form.invalid || busy">{{ busy ? 'Submitting…' : 'Submit claim' }}</button>
      </form>
    </div>`,
})
export class ClaimNewComponent implements OnInit {
  private api = inject(ApiService);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  policies: Policy[] = [];
  loaded = false;
  files: File[] = [];
  busy = false;
  error = '';
  today = new Date().toISOString().slice(0, 10);

  form = this.fb.nonNullable.group({
    policyId: ['', Validators.required],
    incidentDate: [this.today, Validators.required],
    incidentLocation: ['', [Validators.required, Validators.maxLength(300)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(4000)]],
    claimedAmount: [null as number | null, [Validators.required, Validators.min(0.01)]],
  });

  get selected(): Policy | undefined { return this.policies.find((p) => p.id === this.form.controls.policyId.value); }

  ngOnInit() {
    this.api.myPolicies().subscribe({
      next: (p) => { this.policies = p; this.loaded = true; if (p.length === 1) this.form.controls.policyId.setValue(p[0].id); },
      error: (e) => { this.error = errMsg(e); this.loaded = true; },
    });
  }

  pick(event: Event) { this.files = Array.from((event.target as HTMLInputElement).files ?? []); }

  async submit() {
    this.busy = true; this.error = '';
    const v = this.form.getRawValue();
    try {
      const claim = await firstValueFrom(this.api.submitClaim({ ...v, claimedAmount: Number(v.claimedAmount) }));
      const failed: string[] = [];
      for (const file of this.files) {
        try { await firstValueFrom(this.api.upload(claim.id, file)); } catch { failed.push(file.name); }
      }
      // Claim exists either way; failed uploads can be retried from the claim page.
      await this.router.navigate(['/claims', claim.id], { state: { uploadFailed: failed } });
    } catch (e) {
      this.error = errMsg(e);
      this.busy = false;
    }
  }
}
