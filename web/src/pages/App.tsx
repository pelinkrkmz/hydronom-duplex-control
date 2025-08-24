import React, { useEffect, useRef, useState } from 'react'
import { getState, postCommand, openTelemetryWS } from '../lib/api'

type MissionMode = 'MANUAL' | 'AUTONOMOUS' | 'EMERGENCY';
type Telemetry = any;

const VEHICLE = 'hydronom-boat-01';

export default function App() {
  const [telemetry, setTelemetry] = useState<Telemetry | null>(null);
  const [left, setLeft] = useState(1450);
  const [right, setRight] = useState(1450);
  const wsRef = useRef<WebSocket | null>(null);
  const [log, setLog] = useState<string[]>([]);

  useEffect(() => {
    getState(VEHICLE).catch(()=>{});
    wsRef.current = openTelemetryWS(VEHICLE, (t) => {
      setTelemetry(t);
      if (t?.leak || (t?.battery?.voltage ?? 100) < 12 || (t?.temp_c ?? 0) > 50) {
        setLog((L)=>[`${new Date().toLocaleTimeString()} ALERT: ${t.leak?'LEAK ':''}${(t.battery?.voltage ?? 100) < 12?'LOW_VOLT ':''}${(t.temp_c ?? 0) > 50?'HIGH_TEMP':''}`, ...L].slice(0,200))
      }
    });
    return () => wsRef.current?.close();
  }, []);

  function sendMode(mode: MissionMode) {
    postCommand({ vehicle_id: VEHICLE, command: 'SET_MODE', payload: { mode }})
      .then(()=>setLog(L=>[`SET_MODE ${mode}`, ...L].slice(0,200)))
      .catch(e=>alert(e));
  }
  function sendThrusters() {
    postCommand({ vehicle_id: VEHICLE, command: 'SET_THRUSTERS', payload: { left_pwm:left, right_pwm:right }})
      .then(()=>setLog(L=>[`SET_THRUSTERS L:${left} R:${right}`, ...L].slice(0,200)))
      .catch(e=>alert(e));
  }

  return (
    <div className="p-6 space-y-4 max-w-6xl mx-auto">
      <h1 className="text-2xl font-bold">Hydronom Control</h1>

      <div className="grid2">
        <div className="card space-y-2">
          <h2 className="font-semibold">Dashboard</h2>
          <div className="grid grid-cols-2 gap-2 text-sm">
            <Stat label="Speed (m/s)" value={telemetry?.pose?.speed_mps?.toFixed(2) ?? '--'} />
            <Stat label="Heading (°)" value={telemetry?.pose?.heading_deg?.toFixed(1) ?? '--'} />
            <Stat label="Battery (V)" value={telemetry?.battery?.voltage?.toFixed(2) ?? '--'} danger={telemetry?.battery?.voltage < 12} />
            <Stat label="SoC (%)" value={telemetry?.battery?.soc_pct?.toFixed(1) ?? '--'} danger={telemetry?.battery?.soc_pct < 20} />
            <Stat label="Temp (°C)" value={telemetry?.temp_c?.toFixed(1) ?? '--'} danger={telemetry?.temp_c > 50} />
            <Stat label="Leak" value={telemetry?.leak ? 'YES' : 'NO'} danger={telemetry?.leak} />
          </div>
        </div>

        <div className="card space-y-2">
          <h2 className="font-semibold">Manual Control</h2>
          <div className="flex items-center gap-2">
            <button className="btn" onClick={()=>sendMode('MANUAL')}>SET_MODE: MANUAL</button>
            <button className="btn" onClick={()=>sendMode('AUTONOMOUS')}>AUTONOMOUS</button>
            <button className="btn" onClick={()=>sendMode('EMERGENCY')}>EMERGENCY</button>
          </div>
          <div className="mt-3 grid grid-cols-2 gap-3">
            <div>
              <div className="text-sm mb-1">Left PWM: {left}</div>
              <input type="range" min={1300} max={1600} value={left} onChange={e=>setLeft(parseInt(e.target.value))} className="w-full" />
            </div>
            <div>
              <div className="text-sm mb-1">Right PWM: {right}</div>
              <input type="range" min={1300} max={1600} value={right} onChange={e=>setRight(parseInt(e.target.value))} className="w-full" />
            </div>
          </div>
          <button className="btn mt-2" onClick={sendThrusters}>Send Thrusters</button>
        </div>

        <div className="card space-y-2">
          <h2 className="font-semibold">Mission Planner</h2>
          <p className="text-sm opacity-80">MVP: örnek 2 waypoint ve temel START/PAUSE/ABORT akışı için Swagger'dan çağrı yapabilirsiniz.</p>
          <a className="underline" href="http://localhost:5000/swagger" target="_blank">Open Swagger</a>
        </div>

        <div className="card">
          <h2 className="font-semibold mb-2">Logs & Alerts</h2>
          <ul className="text-xs space-y-1 max-h-48 overflow-auto">
            {log.map((l,i)=>(<li key={i} className="font-mono">{l}</li>))}
          </ul>
        </div>
      </div>
    </div>
  )
}

function Stat({label, value, danger=false}:{label:string, value:any, danger?:boolean}){
  return (
    <div className={"p-3 rounded-xl border " + (danger?"border-red-500 bg-red-500/10":"border-gray-700")}>
      <div className="text-xs opacity-70">{label}</div>
      <div className="text-lg">{value}</div>
    </div>
  )
}
