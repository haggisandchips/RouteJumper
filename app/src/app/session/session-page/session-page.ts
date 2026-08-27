import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { Firestore } from '../../core/firestore';
import { SessionHeader } from '../session-header/session-header';
import { SessionEventFeed } from '../session-event-feed/session-event-feed';

@Component({
  selector: 'app-session-page',
  imports: [SessionHeader, SessionEventFeed],
  templateUrl: './session-page.html',
  styleUrl: './session-page.scss',
})
export class SessionPage {
  private readonly route = inject(ActivatedRoute);
  private readonly firestore = inject(Firestore);

  private readonly sessionId$ = this.route.paramMap.pipe(
    map((params) => params.get('sessionId')!),
  );

  private readonly sessionId = toSignal(this.sessionId$, { initialValue: '' });

  session = toSignal(this.sessionId$.pipe(switchMap((id) => this.firestore.watchSession(id))), {
    initialValue: null,
  });

  events = toSignal(this.sessionId$.pipe(switchMap((id) => this.firestore.watchEvents(id))), {
    initialValue: [],
  });

  onDeleteEvent(eventId: string): void {
    // Best-effort, same as every other companion write - a failed delete just leaves the event
    // in place (Firestore's own offline-write rollback means a rejected delete flashes the row
    // away and straight back, via the same live watchEvents subscription that shows new events).
    // Logged rather than silently swallowed so a rules/permission problem is actually diagnosable
    // from the browser console.
    this.firestore
      .deleteEvent(this.sessionId(), eventId)
      .catch((err) => console.error('Failed to delete companion event', err));
  }
}
