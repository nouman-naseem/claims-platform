import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ClaimSummary, MARKETS, Paged, STATUSES } from '../core/models';
import { errMsg, label } from '../core/util';
import { PagerComponent, StatusBadgeComponent } from '../shared/shared.components';

@Component({
  selector: 'app-claim-list',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, CurrencyPipe, StatusBadgeComponent, PagerComponent],
  template: `
    <div class="container">
      <div class="row spread">
        <h1>{{ auth.hasRole('Claimant') ? 'My claims' : 'Claims queue' }}</h1>
        @if (auth.hasRole('Claimant')) { <a routerLink="/claims/new"><button>Report an incident</button></a> }
      </div>

      <form class="card grid cols-4" [formGroup]="filters" (ngSubmit)="apply()">
        <div class="field"><label for="s">Search</label><input id="s" formControlName="search" placeholder="Claim no. or claimant"></div>
        <div class="field"><label for="st">Status</label>
          <select id="st" formControlName="status"><option value="">All</option>@for (s of statuses; track s) { <option [value]="s">{{ label(s) }}</option> }</select></div>
        <div class="field"><label for="t">Type</label>
          <select id="t" formControlName="type"><option value="">All</option><option>Motor</option><option>Property</option></select></div>
        @if (!auth.hasRole('Claimant')) {
          <div class="field"><label for="m">Market</label>
            <select id="m" formControlName="market"><option value="">All</option>@for (m of markets; track m) { <option [value]="m">{{ label(m) }}</option> }</select></div>
          <div class="field row">
            <label><input type="checkbox" formControlName="unassigned"> Unassigned</label>
            @if (auth.hasRole('ClaimsOfficer')) { <label><input type="checkbox" formControlName="assignedToMe"> Assigned to me</label> }
            <label><input type="checkbox" formControlName="overdue"> Overdue</label>
          </div>
        }
        <div class="row"><button type="submit">Apply</button><button type="button" class="secondary" (click)="reset()">Reset</button></div>
      </form>

      @if (error) { <div class="alert error">{{ error }}</div> }

      <div class="card table-wrap">
        <table>
          <thead><tr><th>Claim</th><th>Type</th><th>Market</th><th>Status</th><th>Claimed</th><th>Submitted</th>
            @if (!auth.hasRole('Claimant')) { <th>Claimant</th> }<th>Officer</th></tr></thead>
          <tbody>
            @for (c of data?.items ?? []; track c.id) {
              <tr>
                <td><a [routerLink]="['/claims', c.id]">{{ c.claimNumber }}</a></td>
                <td>{{ c.type }}</td>
                <td>{{ label(c.market) }}</td>
                <td><app-status-badge [status]="c.status" />@if (c.isOverdue) { <span class="badge overdue">Overdue</span> }</td>
                <td>{{ c.claimedAmount | currency: c.currency : 'symbol-narrow' : '1.0-2' }}</td>
                <td>{{ c.submittedAt | date: 'mediumDate' }}</td>
                @if (!auth.hasRole('Claimant')) { <td>{{ c.claimantName }}</td> }
                <td>{{ c.assignedOfficerName ?? '—' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="8" class="muted">{{ loading ? 'Loading…' : 'No claims match your filters.' }}</td></tr>
            }
          </tbody>
        </table>
        @if (data) { <app-pager [page]="data.page" [pages]="data.totalPages" [total]="data.totalCount" (pageChange)="go($event)" /> }
      </div>
    </div>`,
})
export class ClaimListComponent implements OnInit {
  private api = inject(ApiService);
  private fb = inject(FormBuilder);
  auth = inject(AuthService);

  label = label;
  statuses = STATUSES;
  markets = MARKETS;
  data?: Paged<ClaimSummary>;
  page = 1;
  loading = false;
  error = '';

  filters = this.fb.nonNullable.group({
    search: '', status: '', type: '', market: '', unassigned: false, assignedToMe: false, overdue: false,
  });

  ngOnInit() { this.load(); }

  apply() { this.page = 1; this.load(); }
  reset() { this.filters.reset(); this.apply(); }
  go(page: number) { this.page = page; this.load(); }

  private load() {
    this.loading = true; this.error = '';
    this.api.listClaims({ page: this.page, pageSize: 10, ...this.filters.getRawValue() }).subscribe({
      next: (d) => { this.data = d; this.loading = false; },
      error: (e) => { this.error = errMsg(e); this.loading = false; },
    });
  }
}
