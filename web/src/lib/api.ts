const API = 'http://localhost:5000';
const DEV_TOKEN = 'DEV_TOKEN';

export async function getState(vehicleId: string) {
  const r = await fetch(`${API}/api/state/${vehicleId}`, {
    headers: { 'Authorization': `Bearer ${DEV_TOKEN}` }
  });
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}

export async function postCommand(cmd: any) {
  const r = await fetch(`${API}/api/commands`, {
    method: 'POST',
    headers: { 'Authorization': `Bearer ${DEV_TOKEN}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(cmd)
  });
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}

export function openTelemetryWS(vehicleId: string, onMsg: (t:any)=>void) {
  const ws = new WebSocket(`ws://localhost:5000/ws/telemetry/${vehicleId}`);
  ws.onmessage = (ev) => {
    try {
      const j = JSON.parse(ev.data);
      if (j.type === 'telemetry') onMsg(j.data);
    } catch {}
  };
  return ws;
}
