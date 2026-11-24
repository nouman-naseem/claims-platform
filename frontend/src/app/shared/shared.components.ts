import { Component, EventEmitter, Input, Output } from '@angular/core';
import { label } from '../core/util';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  template: `<span class="badge" [class]="status">{{ text }}</span>`,
})
export class StatusBadgeComponent {
  @Input({ required: true }) status = '';
  get text() { return label(this.status); }
}

@Component({
  selector: 'app-pager',
  standalone: true,
  template: `
    <div class="pager">
      <span class="muted">{{ total }} result(s) · page {{ page }} of {{ pages || 1 }}</span>
      <button class="secondary" [disabled]="page <= 1" (click)="pageChange.emit(page - 1)">Previous</button>
      <button class="secondary" [disabled]="page >= pages" (click)="pageChange.emit(page + 1)">Next</button>
    </div>`,
})
export class PagerComponent {
  @Input() page = 1;
  @Input() pages = 1;
  @Input() total = 0;
  @Output() pageChange = new EventEmitter<number>();
}
