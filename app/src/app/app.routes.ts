import { Routes } from '@angular/router';
import { Landing } from './landing/landing';
import { NotFound } from './not-found/not-found';
import { SessionPage } from './session/session-page/session-page';

export const routes: Routes = [
  { path: '', component: Landing, pathMatch: 'full' },
  { path: 'session/:sessionId', component: SessionPage },
  { path: '**', component: NotFound },
];
