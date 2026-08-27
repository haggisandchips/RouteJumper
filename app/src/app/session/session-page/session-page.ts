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

  session = toSignal(this.sessionId$.pipe(switchMap((id) => this.firestore.watchSession(id))), {
    initialValue: null,
  });

  events = toSignal(this.sessionId$.pipe(switchMap((id) => this.firestore.watchEvents(id))), {
    initialValue: [],
  });
}
