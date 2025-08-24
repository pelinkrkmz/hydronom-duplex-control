# Hydronom — Çift Yönlü Kontrol & Telemetri (MVP)

Bu repo, ödevdeki **MVP kabul kriterlerini** karşılayan çalışır bir iskelet sunar.
Bileşenler:
- **api/** (.NET 8 Minimal API + WebSocket yayın + Swagger + JSONL log)
- **web/** (React + TypeScript + Vite + Tailwind) — canlı telemetri WS, komut gönderimi
- **feeder/** (Python sensör simülatörü) — 5–10 Hz telemetri POST eder

Önerilen akış:
1. **API**'yi çalıştırın (localhost:5000)
2. **Feeder**'ı başlatın (telemetri akmaya başlar)
3. **Web** arayüzünü çalıştırın (localhost:5173) ve canlı veriyi görün, komut gönderin

> Geliştirme **dev token**: `DEV_TOKEN` (Bearer ile gönderin). Web istemci bunu otomatik ekler.

---

## 1) API (C# .NET 8)

### Kurulum
```bash
cd api
dotnet restore
dotnet run
```
- Swagger: http://localhost:5000/swagger
- WS (Telemetri yayın): `ws://localhost:5000/ws/telemetry/hydronom-boat-01`

### Notlar
- CORS: `http://localhost:5173` whitelist
- JSONL log dizini: `./logs/telemetry-YYYYMMDD.jsonl`
- Basit rate limit yeri ayrıldı (opsiyonel)

---

## 2) Web (React + TS + Vite + Tailwind)
```bash
cd web
npm i
npm run dev
```
- Aç: http://localhost:5173
- Sayfalar: Dashboard, Manual Control, Mission Planner, Logs

---

## 3) Feeder (Python 3.10+)
```bash
cd feeder
python -m venv .venv && . .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python feeder.py --vehicle hydronom-boat-01 --hz 5 --leak-after 30
```
Argümanlar:
- `--hz`: saniyedeki gönderim (5–10 arası)
- `--leak-after`: saniye sonra `leak=true` fault tetikleme
- `--low-batt-after`: saniye sonra batarya düşüş senaryosu

---

## Hızlı Test
- Dashboard'da hız/heading/batarya anlık güncellenir.
- Manual Control sayfasından `SET_MODE: MANUAL` ve `SET_THRUSTERS` gönderin.
- Mission Planner'da basit 2–3 waypoint oluşturup `START_MISSION` deneyin.
- 30sn sonra leak fault (uyarı + kırmızı badge) görünür.

---

## Güvenlik
- Sadece geliştirme için: `Authorization: Bearer DEV_TOKEN` beklenir.
- Web istemci otomatik gönderir.

---

## Lisans
Eğitim amaçlı örnek.
