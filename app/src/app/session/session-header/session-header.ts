import { Component, computed, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { Session } from '../../core/models/session.model';

@Component({
  selector: 'app-session-header',
  imports: [MatChipsModule, MatIconModule],
  templateUrl: './session-header.html',
  styleUrl: './session-header.scss',
})
export class SessionHeader {
  session = input.required<Session | null>();

  statusLabel = computed(() => {
    switch (this.session()?.status) {
      case 'completed':
        return 'Completed';
      case 'panicked':
        return 'Auto Pilot stopped';
      default:
        return 'Live';
    }
  });
}
