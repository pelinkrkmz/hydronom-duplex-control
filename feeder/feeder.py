import time, json, random, argparse
from datetime import datetime, timezone
import requests

API = "http://localhost:5000/api/telemetry"
DEV_TOKEN = "DEV_TOKEN"

def make_sample(vehicle_id="hydronom-boat-01", as_sub=False, waypoint_index=0, leak=False, low_batt=False):
    now = datetime.now(timezone.utc).isoformat()
    soc = random.uniform(70, 100)
    if low_batt: soc = random.uniform(10, 19)
    t = {
        "timestamp": now,
        "vehicle": {"id": vehicle_id, "type": "sub" if as_sub else "boat"},
        "pose": {"lat": 41.025 + random.uniform(-1e-4,1e-4),
                 "lon": 28.85 + random.uniform(-1e-4,1e-4),
                 "heading_deg": random.uniform(0,360),
                 "speed_mps": random.uniform(0,2)},
        "depth_m": 1.5 if as_sub else 0.0,
        "imu": {"roll_deg": round(random.uniform(-2,2),2),
                "pitch_deg": round(random.uniform(-2,2),2),
                "yaw_deg": round(random.uniform(0,360),2)},
        "thrusters": {"left_pwm": random.randint(1400,1500),
                      "right_pwm": random.randint(1400,1500)},
        "rudder_deg": round(random.uniform(-10,10),1),
        "ballast": {"level_pct": random.randint(0,100) if as_sub else 0},
        "battery": {"voltage": round(random.uniform(11.5, 16.8),2), "soc_pct": round(soc,2)},
        "leak": bool(leak),
        "temp_c": round(random.uniform(18, 55),1),
        "mission": {"mode": "AUTONOMOUS", "task_id":"task-001", "waypoint_index": waypoint_index}
    }
    return t

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--vehicle", default="hydronom-boat-01")
    p.add_argument("--hz", type=int, default=5)
    p.add_argument("--as-sub", action="store_true")
    p.add_argument("--leak-after", type=int, default=0)
    p.add_argument("--low-batt-after", type=int, default=0)
    args = p.parse_args()

    period = 1.0 / max(1, min(args.hz, 10))
    start = time.time()
    i = 0
    while True:
        elapsed = time.time() - start
        leak = args.leak_after and elapsed >= args.leak_after
        low_batt = args.low_batt_after and elapsed >= args.low_batt_after

        payload = make_sample(args.vehicle, args.as_sub, waypoint_index=i%3, leak=leak, low_batt=low_batt)
        try:
            r = requests.post(API, json=payload, timeout=3, headers={"Authorization": "Bearer "+DEV_TOKEN})
            # print(r.status_code)
        except Exception as e:
            print("POST error:", e)
        time.sleep(period)
        i += 1

if __name__ == "__main__":
    main()
