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
    return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString();
  }
}
