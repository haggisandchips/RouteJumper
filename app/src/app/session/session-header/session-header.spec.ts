import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SessionHeader } from './session-header';

describe('SessionHeader', () => {
  let component: SessionHeader;
  let fixture: ComponentFixture<SessionHeader>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SessionHeader],
    }).compileComponents();

    fixture = TestBed.createComponent(SessionHeader);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('session', null);
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows a loading state with no session yet', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Loading route');
  });

  it('renders the start -> end systems and a status label once a session arrives', async () => {
    fixture.componentRef.setInput('session', {
      startSystem: 'Sol',
      endSystem: 'Colonia',
      createdUtc: new Date().toISOString(),
      status: 'active',
    });
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sol');
    expect(text).toContain('Colonia');
    expect(text).toContain('Live');
  });
});
