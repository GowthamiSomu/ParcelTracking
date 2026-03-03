# 📦 Parcel Tracking System

A production-grade, event-driven parcel tracking system built with **.NET 8** and deployable locally via **Docker** or on **Kubernetes**. It ingests parcel scan events, enforces business rules, records a full event history, sends notifications, and exposes a REST API for tracking lookups.

---

## Table of Contents

1. [What Does It Do?](#1-what-does-it-do)
2. [How It Works (Technical Overview)](#2-how-it-works-technical-overview)
3. [Parcel Lifecycle](#3-parcel-lifecycle)
4. [Business Rules](#4-business-rules)
5. [REST API Reference](#5-rest-api-reference)
6. [Project Structure](#6-project-structure)
7. [Prerequisites – Install Required Tools](#7-prerequisites--install-required-tools)
8. [First-Time Setup](#8-first-time-setup)
9. [Running the Application](#9-running-the-application)
10. [Stopping the Application](#10-stopping-the-application)
11. [Using the API](#11-using-the-api)
12. [Browsing the Database](#12-browsing-the-database)
13. [Running Tests](#13-running-tests)
14. [Kubernetes Deployment](#14-kubernetes-deployment)
15. [Architecture & Security](#15-architecture--security)
16. [Troubleshooting](#16-troubleshooting)

---

## 1. What Does It Do?

The Parcel Tracking System allows a courier business to:

- **Register a parcel** when it is collected from a sender
- **Track it** through sorting hubs and delivery stages in real time
- **Automatically classify** parcels as Standard or Large and calculate charges
- **Validate** every scan event against strict business rules
- **Notify** the sender by email and the receiver by SMS at every stage change
- **Query** the current status and full event history of any parcel via a REST API
- **Handle 5,000 scan events per second** with guaranteed per-parcel ordering

---

## 2. How It Works (Technical Overview)

The system is composed of two independently running services backed by three infrastructure components:

```
┌──────────────────────────────────────────────────────────┐
│                  Event Producer (external)               │
│     Publishes ScanEvent JSON → Azure Service Bus Queue   │
└─────────────────────────┬────────────────────────────────┘
                          │
              ┌───────────▼───────────┐
              │   Ingestion Service   │  (.NET Worker Service)
              │  ─────────────────── │
              │  1. Idempotency check │◄─── Redis
              │  2. Rule A validation │
              │  3. Rule B sizing     │
              │  4. Rule C transition │
              │  5. Persist to DB     │◄─── SQL Server
              │  6. Send notification │
              └───────────────────────┘

              ┌───────────────────────┐
              │      REST API         │  (ASP.NET Core)
              │  ─────────────────── │
              │  GET /api/parcels/{id}│◄─── SQL Server
              │  GET /.../events      │
              └───────────────────────┘
```

### Technology Stack

| Component | Technology |
|---|---|
| Language & Runtime | C# 12 / .NET 8 |
| REST API | ASP.NET Core Web API |
| Event Ingestion | .NET Worker Service + Azure Service Bus SDK |
| Message Broker | Azure Service Bus (local emulator for demo) |
| Database | SQL Server 2022 + Entity Framework Core 8 |
| Idempotency Store | Redis 7 |
| Containerisation | Docker + Docker Compose |
| Orchestration | Kubernetes (optional) |

---

## 3. Parcel Lifecycle

Each parcel moves through the following statuses in strict order. The system **rejects** any attempt to skip or reverse a step.

```
   [New Parcel]
        │
        ▼
   COLLECTED          ← Parcel picked up from sender
        │
        ▼
   SOURCE-SORT         ← Arrives at origin sorting hub
        │
        ▼
   DESTINATION-SORT    ← Arrives at destination sorting hub
        │
        ▼
   DELIVERY_CENTRE     ← Arrives at local delivery depot
        │
        ▼
   READY_FOR_DELIVERY  ← Out for delivery
        │
   ┌────┴────┐
   ▼         ▼
DELIVERED  FAILED_TO_DELIVER
               │
          ┌────┴────┐
          ▼         ▼
  READY_FOR_DELIVERY  RETURNED
```

Any scan event that attempts an invalid transition is automatically rejected, an **anomaly event** is recorded, and the message is routed to a Dead-Letter Queue for investigation.

---

## 4. Business Rules

### Rule A – Collection Validation
Applies to every `COLLECTED` event. Validates:
- `trackingId` is alphanumeric (dashes allowed), 8–30 characters, globally unique
- `fromAddress` and `toAddress` are fully populated
- Dimensions (length, width, height) are positive and ≤ 300 cm
- Weight is positive and ≤ 70 kg
- `baseCharge` is a positive value

**Failure:** Event rejected with a structured error; parcel is not created; message sent to Dead-Letter Queue.

### Rule B – Size Classification & Pricing
Applies automatically on `COLLECTED`:
- If any dimension exceeds **50 cm** → `sizeClass = LARGE`
- Otherwise → `sizeClass = STANDARD`
- `LARGE` parcels incur a **20% surcharge** on the base charge
- All monetary values stored as fixed-point decimal (never floating-point)

### Rule C – Status Transition Validation
Applies to every scan event:
- Validates the requested transition against the state machine above
- **Failure:** Event rejected; anomaly event emitted with full context; original message sent to Dead-Letter Queue

### Idempotency
Every scan event carries a unique `eventId`. If the same event arrives twice (e.g. network retry), the second copy is silently acknowledged and discarded — parcel state is never double-updated. Idempotency keys are stored in Redis with a 7-day TTL.

### Notifications
- **Sender** is notified by email on every status change (always)
- **Receiver** is notified by SMS on every status change, only if `notificationOptIn = true`
- For the demo, notifications are written to structured log output. The `INotificationService` interface is designed for easy swap to SendGrid, Twilio, or Azure Communication Services.

---

## 5. REST API Reference

**Base URL:** `http://localhost:5058`

### Get Parcel Details

```
GET /api/parcels/{trackingId}
```

Returns the current status, size classification, charges, and address information for a parcel.

**Example response:**
```json
{
  "trackingId": "PARCEL-001",
  "currentStatus": "DELIVERY_CENTRE",
  "sizeClass": "STANDARD",
  "charges": {
    "base": 8.50,
    "surcharge": 0.00,
    "total": 8.50
  },
  "from": {
    "line1": "1 Sender Street",
    "city": "Manchester",
    "postcode": "M1 1AA",
    "country": "UK"
  },
  "to": {
    "line1": "99 Receiver Road",
    "city": "London",
    "postcode": "EC1 2BB",
    "country": "UK"
  },
  "sender": {
    "name": "Alice Smith",
    "contactNumber": "07700000001",
    "email": "alice@example.com"
  },
  "receiver": {
    "name": "Bob Jones",
    "notificationOptIn": true
  }
}
```

### Get Parcel Event History

```
GET /api/parcels/{trackingId}/events?limit=100
```

Returns all scan events in **reverse chronological order** (latest first). `limit` defaults to 100, maximum 500.

**Example response:**
```json
{
  "trackingId": "PARCEL-001",
  "events": [
    {
      "eventId": "a1b2c3d4-...",
      "eventType": "DELIVERY_CENTRE",
      "eventTimeUtc": "2026-03-04T09:00:00.000Z",
      "locationId": "DEPOT-LONDON-SW1",
      "hubType": "DELIVERY_CENTRE",
      "actorId": "SCANNER-007",
      "metadata": {}
    }
  ]
}
```

### Health Checks

| Endpoint | Purpose |
|---|---|
| `GET /healthz/live` | Liveness – confirms the process is running |
| `GET /healthz/ready` | Readiness – confirms SQL Server and Redis are reachable |

### Error Responses

All errors use a consistent shape:
```json
{
  "error": {
    "code": "PARCEL_NOT_FOUND",
    "message": "No parcel found with trackingId 'XYZ'."
  }
}
```

Common error codes:

| Code | HTTP Status | Meaning |
|---|---|---|
| `PARCEL_NOT_FOUND` | 404 | No parcel with that tracking ID exists |
| `INVALID_TRACKING_ID` | 400 | Tracking ID contains invalid characters or wrong length |
| `INVALID_LIMIT` | 400 | Event history limit is out of range |

---

## 6. Project Structure

```
ParcelTracking/
├── src/
│   ├── ParcelTracking.Domain/        # Enums, domain models, state machine
│   ├── ParcelTracking.Rules/         # Business rules A, B, C
│   ├── ParcelTracking.Notifications/ # INotificationService interface + stub
│   ├── ParcelTracking.Infrastructure/# EF Core, repositories, Redis, Service Bus
│   ├── ParcelTracking.Ingestion/     # Worker service – Service Bus consumer
│   └── ParcelTracking.Api/           # REST API (ASP.NET Core)
├── tests/
│   ├── ParcelTracking.UnitTests/     # 53 unit tests (rules, state machine, etc.)
│   ├── ParcelTracking.IntegrationTests/
│   └── ParcelTracking.LoadTests/
├── deploy/
│   ├── k8s/                          # Kubernetes manifests
│   └── servicebus-config.json        # Service Bus emulator queue configuration
├── docker-compose.yml                # Full local demo stack
├── .env.example                      # Template for secrets (copy to .env)
└── ParcelTracking.sln
```

---

## 7. Prerequisites – Install Required Tools

> **No technical experience is required to follow these steps.** Each tool has an installer — just download and run it.

### Step 1 – Install Docker Desktop

Docker runs all the application components (database, message broker, API, etc.) inside isolated containers on your machine. You do not need to install SQL Server, Redis, or .NET separately.

1. Go to **[https://www.docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop)**
2. Click **"Download for Windows"** (or Mac)
3. Run the downloaded installer (`Docker Desktop Installer.exe`)
4. Follow the on-screen prompts. Accept the default settings.
5. Restart your computer when prompted
6. After restart, Docker Desktop will launch automatically. You will see the Docker whale icon in the system tray (bottom-right of your screen)
7. Wait until the icon stops animating and shows **"Docker Desktop is running"**

> **Minimum requirements:** Windows 10/11 (64-bit), 8 GB RAM, hardware virtualisation enabled in BIOS (most modern PCs have this by default).

### Step 2 – Install Git (to download the project)

1. Go to **[https://git-scm.com/download/win](https://git-scm.com/download/win)**
2. The download starts automatically. Run the installer.
3. Click **Next** on every screen to accept all defaults.
4. Click **Finish**.

### Step 3 – Verify installations

1. Press `Windows + R`, type `cmd`, press Enter
2. In the black window that opens, type each command below and press Enter:

```
docker --version
git --version
```

You should see version numbers printed for each. If you see **"command not found"**, restart your computer and try again.

---

## 8. First-Time Setup

### Step 1 – Download the project

Open a terminal (press `Windows + R`, type `cmd`, press Enter) and run:

```
git clone https://github.com/your-org/ParcelTracking.git
cd ParcelTracking
```

> If you already have the project folder, skip this step and navigate to it:
> ```
> cd C:\path\to\ParcelTracking
> ```

### Step 2 – Create your secrets file

The application needs a password for the database. A template file is provided:

**On Windows (Command Prompt):**
```
copy .env.example .env
```

**On Windows (PowerShell):**
```powershell
Copy-Item .env.example .env
```

Now open the `.env` file in Notepad:
```
notepad .env
```

Replace `REPLACE_WITH_STRONG_PASSWORD` in **all three places** with a password of your choice.
The password must be at least 8 characters and include uppercase letters, lowercase letters, and numbers (e.g. `MyPass2026!`).

**Example `.env` after editing:**
```
SA_PASSWORD=MyPass2026!
SQLSERVER_CONNECTION=Server=sqlserver,1433;Database=ParcelTracking;User Id=sa;Password=MyPass2026!;TrustServerCertificate=True;
REDIS_CONNECTION=redis:6379
SERVICEBUS_CONNECTION=Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Save and close Notepad.

### Step 3 – Build and start the application

In your terminal (make sure you are in the `ParcelTracking` folder), run:

```
docker compose up --build -d
```

This will:
- Download all required components (SQL Server, Redis, Service Bus emulator) — **this may take 5–15 minutes on first run** depending on your internet speed
- Build the application code
- Start all 7 containers

You will see output like:
```
✔ Container parcel-sqlserver  Healthy
✔ Container parcel-redis      Running
✔ Container parcel-sqledge    Healthy
✔ Container parcel-servicebus Running
✔ Container parcel-adminer    Running
✔ Container parcel-ingestion  Running
✔ Container parcel-api        Running
```

The application is ready when all containers show **Healthy** or **Running**.

---

## 9. Running the Application

After the first-time setup, starting the application on subsequent occasions only requires one command:

```
docker compose up -d
```

Wait about 20–30 seconds for all components to start, then open your browser and go to:

| What | URL |
|---|---|
| **Swagger UI (API Explorer)** | http://localhost:5058/swagger |
| **Database Browser (Adminer)** | http://localhost:8080 |

---

## 10. Stopping the Application

### Stop (keep your data)

This stops the containers but preserves all data in the database:

```
docker compose down
```

### Stop and wipe all data

This stops everything and deletes all data (useful for a clean reset):

```
docker compose down -v
```

---

## 11. Using the API

### Option A – Swagger UI (Recommended for non-technical users)

1. Open **[http://localhost:5058/swagger](http://localhost:5058/swagger)** in your browser
2. You will see all available API endpoints listed
3. Click on any endpoint to expand it
4. Click **"Try it out"** to enable the input fields
5. Enter a `trackingId` (e.g. `PARCEL-001`) and click **"Execute"**
6. The response will appear below

### Option B – Send a Scan Event (Publish to the queue)

The ingestion service listens on the **Azure Service Bus** queue. To create a parcel, publish a `COLLECTED` scan event.

**Minimum payload for a `COLLECTED` event:**

```json
{
  "eventId": "evt-00000001-0000-0000-0000-000000000001",
  "trackingId": "PARCEL-001",
  "eventType": "COLLECTED",
  "eventTimeUtc": "2026-03-04T08:00:00Z",
  "locationId": "DEPOT-MANCHESTER-M1",
  "actorId": "DRIVER-101",
  "metadata": {},
  "fromAddress": {
    "line1": "1 Sender Street",
    "city": "Manchester",
    "postcode": "M1 1AA",
    "country": "UK"
  },
  "toAddress": {
    "line1": "99 Receiver Road",
    "city": "London",
    "postcode": "EC1 2BB",
    "country": "UK"
  },
  "sender": {
    "name": "Alice Smith",
    "contactNumber": "07700000001",
    "email": "alice@example.com"
  },
  "receiver": {
    "name": "Bob Jones",
    "contactNumber": "07700000002",
    "email": "bob@example.com",
    "notificationOptIn": true
  },
  "dimensions": {
    "lengthCm": 30,
    "widthCm": 20,
    "heightCm": 15,
    "weightKg": 2.5
  },
  "baseCharge": 8.50
}
```

**To progress the parcel, publish further events with the next status:**

```json
{
  "eventId": "evt-00000001-0000-0000-0000-000000000002",
  "trackingId": "PARCEL-001",
  "eventType": "SOURCE_SORT",
  "eventTimeUtc": "2026-03-04T10:00:00Z",
  "locationId": "HUB-MANCHESTER-M60",
  "actorId": "SCANNER-042",
  "metadata": {}
}
```

---

## 12. Browsing the Database

Adminer provides a web interface to inspect the database directly — no SQL knowledge needed.

1. Open **[http://localhost:8080](http://localhost:8080)** in your browser
2. Fill in the login form:

| Field | Value |
|---|---|
| **System** | MS SQL (SQL Server) |
| **Server** | `sqlserver` |
| **Username** | `sa` |
| **Password** | The `SA_PASSWORD` value from your `.env` file |
| **Database** | `ParcelTracking` |

3. Click **Login**
4. Click on **Parcels** or **ScanEvents** tables to view data

---

## 13. Running Tests

> **Requires .NET 8 SDK.** Download from **[https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)** (choose the **SDK** installer for your OS).

### Unit Tests (53 tests — no running containers needed)

```
dotnet test tests/ParcelTracking.UnitTests
```

Unit tests cover:
- All three business rules (A, B, C) with valid and invalid inputs
- Every state machine transition (valid and invalid)
- Idempotency logic
- Notification trigger conditions (sender always, receiver only if opted in)
- Anomaly event emission on invalid transitions

### Integration Tests

Requires all containers running (`docker compose up -d`):

```
dotnet test tests/ParcelTracking.IntegrationTests
```

### Load / Performance Tests

Validates sustained 5,000 events/second throughput:

```
dotnet test tests/ParcelTracking.LoadTests
```

---

## 14. Kubernetes Deployment

Kubernetes manifests are provided in `deploy/k8s/` for deploying to Docker Desktop Kubernetes or any Kubernetes cluster.

### Enable Kubernetes in Docker Desktop

1. Open **Docker Desktop**
2. Click the **gear icon** (Settings) in the top right
3. Click **Kubernetes** in the left menu
4. Check **"Enable Kubernetes"**
5. Click **"Apply & Restart"**
6. Wait until the Kubernetes icon in the bottom-left of Docker Desktop turns **green**

### Create Kubernetes Secrets

Secrets must never be stored in the manifest files. Run the provided script to create them securely:

```powershell
.\deploy\k8s\create-secrets.ps1
```

This reads your `.env` file and pipes the values directly into Kubernetes — credentials are never written to any file.

### Deploy All Components

```
kubectl apply -f deploy/k8s/
```

This deploys in order: namespace → secrets → configmap → SQL Server → Redis → Service Bus → Adminer → Ingestion → API.

### Check Deployment Status

```
kubectl get pods -n parcel-tracking
```

All pods should show `Running` status.

### Access the API

```
kubectl get svc -n parcel-tracking
```

The API is exposed on **port 5058** via a LoadBalancer service — accessible at `http://localhost:5058/swagger`.

### Remove Deployment

```
kubectl delete -f deploy/k8s/
```

---

## 15. Architecture & Security

### Architecture Principles

| Principle | Implementation |
|---|---|
| **Event-driven** | All state changes driven by scan events via Azure Service Bus |
| **Idempotency** | Every event has a unique `eventId`; duplicates detected via Redis SET NX and silently dropped |
| **Per-parcel ordering** | Service Bus sessions partitioned by `trackingId` guarantee in-order delivery |
| **Backpressure & resilience** | Exponential backoff retries; Dead-Letter Queue for poison/invalid messages; circuit breakers |
| **CQRS-lite** | Write path (ingestion + rules) completely separate from read path (REST API) |
| **Dependency injection** | Rules engine, notification service, and repositories are all interface-driven for easy testing and swapping |

### Security Controls

| Control | Detail |
|---|---|
| **No secrets in source code** | All credentials in `.env` (gitignored); Kubernetes secrets created via script |
| **Input validation** | `trackingId` validated against regex before any database call |
| **Security headers** | `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy`, `Permissions-Policy`, `Content-Security-Policy` |
| **Rate limiting** | 100 requests/minute per IP; 429 returned on breach |
| **No stack trace leakage** | Global exception handler returns RFC 7807 ProblemDetails with trace ID only |
| **PII protection** | Receiver phone/email not exposed on unauthenticated API endpoints |
| **Health checks** | `/healthz/live` and `/healthz/ready` endpoints for container orchestration |
| **Non-root container** | Docker images run as a non-root `appuser` account |

---

## 16. Troubleshooting

### Containers won't start

**Check Docker is running:**
Look for the Docker whale icon in the system tray. If it is not there, open Docker Desktop from the Start menu and wait for it to fully start.

**View container logs:**
```
docker compose logs api
docker compose logs ingestion
```

### Port already in use

If you see an error like `port 5058 is already allocated`, another application is using that port. Either stop the other application or edit `docker-compose.yml` and change `5058:8080` to another port (e.g. `5059:8080`).

### Swagger page is blank

Try opening the API URL directly in a browser (not the VS Code Simple Browser):
```
http://localhost:5058/swagger/index.html
```

### Database connection errors

Ensure your `.env` file password matches the `SA_PASSWORD` value exactly in all three lines. Then restart:
```
docker compose down -v
docker compose up --build -d
```

### `docker compose` command not found

Ensure Docker Desktop is fully installed and running. Older Docker versions use `docker-compose` (with a hyphen) instead of `docker compose`.

### Reset everything (clean slate)

```
docker compose down -v
docker compose up --build -d
```

---

## Quick Reference Card

```
# Start the app
docker compose up -d

# Stop the app (keep data)
docker compose down

# View logs
docker compose logs api --follow
docker compose logs ingestion --follow

# Rebuild after code changes
docker compose build --no-cache api
docker compose up -d api

# Run unit tests
dotnet test tests/ParcelTracking.UnitTests

# Open API explorer
http://localhost:5058/swagger

# Open database browser
http://localhost:8080
```
