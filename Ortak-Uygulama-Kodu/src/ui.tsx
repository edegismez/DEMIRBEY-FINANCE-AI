import type { ReactNode } from 'react';

export const Kpi = ({ label, value, sub, color }: { label: string; value: string; sub?: string; color?: string }) => (
  <div className="card kpi"><div className="l">{label}</div><div className="v" style={{ color }}>{value}</div>{sub && <div className="s">{sub}</div>}</div>
);

export const Tag = ({ color, children }: { color: string; children: ReactNode }) => (
  <span className="tag" style={{ color }}>{children}</span>
);

export const State = ({ error }: { error: string | null }) => (
  <div className="card state">{error ? <>⚠ {error}<div className="muted">Sunucu çalışıyor mu? (Sunucu/src/Demirbey.Api → dotnet run)</div></> : 'Yükleniyor…'}</div>
);
