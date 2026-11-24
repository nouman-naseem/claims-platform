import { CurrencyPipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { CountBy, Dashboard } from '../core/models';
import { errMsg, label } from '../core/util';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CurrencyPipe, DecimalPipe, NgTemplateOutlet, RouterLink],
  template: `
    <div class="container">
      <h1>Management dashboard</h1>
      @if (error) { <div class="alert error">{{ error }}</div> }
      @if (data; as d) {
        <div class="grid cols-4">
          <div class="card"><div class="muted">Total claims</div><div class="stat">{{ d.totalClaims }}</div></div>
          <div class="card"><div class="muted">Outstanding</div><div class="stat">{{ d.outstandingClaims }}</div></div>
          <div class="card"><div class="muted">Pending decisions</div><div class="stat">{{ d.pendingDecisions }}</div>
            <a [routerLink]="['/claims']">Unassigned: {{ d.unassignedClaims }}</a></div>
          <div class="card"><div class="muted">Overdue (&gt; {{ d.slaDays }} days)</div><div class="stat" style="color:var(--danger)">{{ d.overdueClaims }}</div></div>
          <div class="card"><div class="muted">Est. outstanding exposure</div><div class="stat">{{ d.estimatedExposureUsd | currency: 'USD' : 'symbol' : '1.0-0' }}</div>
            <span class="muted">Indicative FX roll-up</span></div>
        </div>

        <div class="grid cols-2">
          <div class="card"><h2>By status</h2>@for (r of d.byStatus; track r.key) { <ng-container [ngTemplateOutlet]="bar" [ngTemplateOutletContext]="{ r: r, max: max(d.byStatus) }" /> }</div>
          <div class="card"><h2>By type</h2>@for (r of d.byType; track r.key) { <ng-container [ngTemplateOutlet]="bar" [ngTemplateOutletContext]="{ r: r, max: max(d.byType) }" /> }</div>
          <div class="card"><h2>By market</h2>@for (r of d.byMarket; track r.key) { <ng-container [ngTemplateOutlet]="bar" [ngTemplateOutletContext]="{ r: r, max: max(d.byMarket) }" /> }</div>
          <div class="card table-wrap"><h2>Exposure by market</h2>
            <table><thead><tr><th>Market</th><th>Local</th><th>USD</th></tr></thead><tbody>
              @for (e of d.exposureByMarket; track e.market) {
                <tr><td>{{ label(e.market) }}</td><td>{{ e.exposureLocal | currency: e.currency : 'symbol-narrow' : '1.0-0' }}</td>
                  <td>{{ e.exposureUsd | currency: 'USD' : 'symbol' : '1.0-0' }}</td></tr>
              }</tbody></table></div>
        </div>

        <div class="card table-wrap"><h2>Team workload &amp; performance</h2>
          <table><thead><tr><th>Officer</th><th>Market</th><th>Open</th><th>Overdue</th><th>Decided</th><th>Approved</th><th>Avg days to decision</th></tr></thead><tbody>
            @for (w of d.teamWorkload; track w.officerId) {
              <tr><td>{{ w.name }}</td><td>{{ label(w.market) }}</td><td>{{ w.openClaims }}</td><td>{{ w.overdueClaims }}</td>
                <td>{{ w.decidedClaims }}</td><td>{{ w.approvedClaims }}</td><td>{{ w.avgDaysToDecision | number: '1.0-1' }}</td></tr>
            }</tbody></table></div>
      } @else if (!error) { <p class="muted">Loading…</p> }
    </div>

    <ng-template #bar let-r="r" let-max="max">
      <div class="row spread" style="margin-top:.4rem"><span>{{ label(r.key) }}</span><strong>{{ r.count }}</strong></div>
      <div class="bar"><span [style.width.%]="max ? (r.count / max) * 100 : 0"></span></div>
    </ng-template>`,
})
export class DashboardComponent implements OnInit {
  private api = inject(ApiService);
  data?: Dashboard;
  error = '';
  label = label;

  max(rows: CountBy[]) { return Math.max(0, ...rows.map((r) => r.count)); }

  ngOnInit() {
    this.api.dashboard().subscribe({ next: (d) => (this.data = d), error: (e) => (this.error = errMsg(e)) });
  }
}
