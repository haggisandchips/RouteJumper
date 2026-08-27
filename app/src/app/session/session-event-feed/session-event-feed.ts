import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { SessionEvent, SessionEventKind } from '../../core/models/session-event.model';

const ICONS: Record<SessionEventKind, string> = {
  plotted: 'route',
  arrived: 'flag_circle',
  refueled: 'local_gas_station',
  panic: 'warning',
};

@Component({
  selector: 'app-session-event-feed',
  imports: [MatIconModule],
  templateUrl: './session-event-feed.html',
  styleUrl: './session-event-feed.scss',
})
export class SessionEventFeed {
  events = input.required<SessionEvent[]>();

  iconFor(kind: SessionEventKind): string {
    return ICONS[kind];
  }

  timeLabel(clientUtc: string): string {
    const date = new Date(clientUtc);
    if (Number.isNaN(date.getTime())) {
      return '';
    }
    // A route can run for days, so the date matters too - a named month
    // (rather than a locale-ambiguous numeric one) avoids any US/UK mix-up.
    const datePart = date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
    return `${datePart}, ${date.toLocaleTimeString()}`;
  }
}
