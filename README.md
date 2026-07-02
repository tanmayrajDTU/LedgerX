# 💼 WealthPulse (LedgerX) — Wealth Management Platform

WealthPulse is a premium, real-time distributed portfolio tracking and wealth management system customized for the **Indian Stock Market (INR / ₹)**. It aggregates stocks, ETFs, Mutual Funds, Fixed Deposits, and Savings Accounts to compile a consolidated net worth, analyze asset concentration, calculate annualized volatility, and track compound returns.

---

## 🏗️ System Architecture

The platform is designed around a **decoupled, microservices-oriented architecture** to handle heavy real-time market data requests and CPU-intensive analytics background tasks:

```mermaid
graph TD
    Client[React Frontend / SPA] -->|HTTP / REST| API[ASP.NET Web API]
    API -->|Read/Write| DB[(PostgreSQL Database)]
    API -->|Cache / Config| Redis[(Redis Cache)]
    API -->|Queue Jobs| RMQ[[RabbitMQ Message Broker]]
    
    subgraph Background Workers Host
        MDW[MarketData Worker]
        AW[Analytics Worker]
        ALW[Alert Worker]
        STW[SchedulerTrigger Worker]
    end

    RMQ -->|Consume: job.marketdata| MDW
    RMQ -->|Consume: job.analytics| AW
    RMQ -->|Consume: job.alert| ALW
    
    MDW -->|Query Live Price| YF[Yahoo Finance API]
    MDW -->|Cache Price| Redis
    MDW -->|Save Price Date| DB
    
    AW -->|Calculate Risk Metric| DB
    ALW -->|Check Risk Rules| DB
```

---

## 🛠️ Prerequisites

Before setting up the project, make sure you have the following installed:
*   **Operating System:** Windows 10/11 (with WSL2 enabled)
*   **Containers:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) (configured to use WSL2 backend)
*   **Backend:** [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
*   **Frontend:** [Node.js (v18 or higher)](https://nodejs.org/) & `npm`

---

## 🚀 Local Development Setup

You can run the entire platform using Docker, or run the services individually for an active debugging workflow.

### Option A: Run the Entire Stack via Docker Compose (Simplest)
This boots all backend services, database migrations, message brokers, caching nodes, and monitoring tools.

1.  **Clear cached volumes (Required on first boot to seed Indian stock market data):**
    ```powershell
    docker-compose down -v
    ```
2.  **Build and launch the containers:**
    ```powershell
    docker-compose up --build
    ```
3.  **Access the applications:**
    *   **Frontend UI:** `http://localhost:5173`
    *   **ASP.NET Web API:** `http://localhost:5000` (Swagger UI at `/swagger`)
    *   **RabbitMQ Management Console:** `http://localhost:15672` (Credentials: `guest` / `guest`)
    *   **Grafana Dashboard:** `http://localhost:3000` (Credentials: `admin` / `admin`)

---

### Option B: Run Services Individually (For Development & Debugging)
To debug code changes in real time, run the backing databases in Docker and start the C# and React projects locally.

#### 1. Spin up Core Infrastructure Containers
Ensure Docker Desktop is running, then start only the backing services:
```powershell
docker-compose up postgres redis rabbitmq -d
```

#### 2. Run Database Migrations & Seed Data
Navigate to the source directory and apply the Entity Framework Core migrations to seed Indian equity assets:
```powershell
cd src
dotnet ef database update --project LedgerX.Infrastructure --startup-project LedgerX.API
```

#### 3. Run the Web API
Start the ASP.NET Core API server:
```powershell
dotnet run --project LedgerX.API
```

#### 4. Run the Background Workers
Start the RabbitMQ consumer workers (MarketData, Analytics, Alerts, and Schedulers):
```powershell
dotnet run --project LedgerX.Workers
```

#### 5. Run the React Frontend
Open a new terminal session, navigate to the UI project, install dependencies, and boot the Vite development server:
```powershell
cd src/ledgerx-ui
npm install
npm run dev
```

---

## 📊 Live vs. Mock Market Data

By default, the platform runs in **Mock/Simulation mode** for convenience, producing stable simulated prices based on asset types:
*   **Stocks:** ₹50 – ₹1,500
*   **ETFs:** ₹80 – ₹400
*   **Mutual Funds:** ₹10 – ₹150

### How to Toggle Real-Time Yahoo Finance Data:
1.  Log in to the **Frontend UI** using default credentials:
    *   **Username:** `user`
    *   **Password:** `User@123`
2.  Click **Admin System Console** in the sidebar.
3.  Toggle **Live Yahoo Finance Data** to **ON** and click **Save Configuration**.
4.  The background workers will immediately start pulling real-time market data from the Yahoo Finance API (e.g. ₹63.50 for `GOLDBEES.NS`).

---

## 📦 Production Release Deployment Guide

To deploy WealthPulse to a production environment, follow these steps:

### 1. Build Production Images
Compile and package the microservices into optimized Docker images:
```powershell
# Build API Image
docker build -t ledgerx-api:latest -f src/LedgerX.API/Dockerfile .

# Build Workers Image
docker build -t ledgerx-workers:latest -f src/LedgerX.Workers/Dockerfile .

# Build React Static Assets (Production SPA Bundle)
cd src/ledgerx-ui
npm run build
```

### 2. Production Architecture Design
For high availability, resilience, and security:

```
                            [ DNS (Cloudflare) ]
                                     |
                                     v
                        [ SSL Termination & Nginx ]
                                     |
                +--------------------+--------------------+
                |                                         |
                v                                         v
     [ API Service Replica 1 ]                 [ API Service Replica 2 ]
                |                                         |
                +--------------------+--------------------+
                                     |
        +----------------------------+----------------------------+
        |                            |                            |
        v                            v                            v
 [ Redis Cluster ]          [ RabbitMQ Cluster ]       [ PostgreSQL Primary ]
 (Cache/Session)             (Mirrored Queues)                    |
                                                                  v
                                                        [ PostgreSQL Standby ]
```

### 3. Step-by-Step Production Setup Plan

#### Step A: Configure Production Databases
1.  **PostgreSQL:** Set up a managed database instance (e.g. AWS RDS PostgreSQL or Azure SQL Database for PostgreSQL) with Multi-AZ replication enabled. Enable regular automated snapshots.
2.  **Redis:** Spin up a managed Redis Cache instance (e.g., AWS ElastiCache) configured for high availability (primary-replica) with clustering enabled.

#### Step B: Secure Production Environment Secrets
Do not check connection strings or keys into source control. Use an environment configuration mechanism:
*   **AWS Parameter Store / Secrets Manager** or **Azure Key Vault** to inject secrets.
*   Required Environment Variables:
    ```bash
    ConnectionStrings__DefaultConnection="Host=prod-pg.db.net;Database=ledgerx;Username=dbadmin;Password=YourSecurePassword"
    ConnectionStrings__Redis="prod-redis.cache.windows.net:6379,password=YourRedisPassword,ssl=True"
    ConnectionStrings__RabbitMQ="amqp://rabbituser:SecureRabbitPassword@prod-rmq-broker:5672/"
    Jwt__Key="Use-A-Min-256-Bit-Cryptographic-Key-Generated-Via-OpenSSL-For-Production"
    MarketData__UseLiveData="true"
    ```

#### Step C: Set Up RabbitMQ Cluster
Deploy RabbitMQ in cluster mode with queue mirroring enabled (`ha-mode: all`). Ensure standard access controls are configured (delete the default `guest` account and add a dedicated service account).

#### Step D: Deploy Frontend Assets
The React frontend is a Single Page Application (SPA).
1.  Build the files using `npm run build`.
2.  Upload the compiled `dist/` directory to an object storage bucket (e.g., AWS S3, Azure Blob Storage, or Google Cloud Storage).
3.  Front the storage bucket with a Content Delivery Network (CDN) like **Cloudflare** or **Amazon CloudFront** for global edge-caching and SSL termination.

#### Step E: Container Orchestration (Kubernetes / ECS)
Deploy the API and Worker containers using a container management system (Kubernetes or AWS ECS):
1.  **Replicas:** Run at least 2 replicas of `LedgerX.API` behind a load balancer to ensure zero-downtime rolling updates.
2.  **Autoscaling:** Configure Horizontal Pod Autoscaling (HPA) to scale API pods based on CPU/Memory usage.
3.  **Background Workers:** Run independent replicas of `LedgerX.Workers`. Scale workers based on RabbitMQ queue depth metrics (e.g. using KEDA).

---

## 📈 Monitoring & Alerts

In production, keep track of system health using the built-in integrations:
*   **Metrics:** Prometheus scrapes worker performance metrics on port `5001`.
*   **Dashboards:** Import the custom dashboard into Grafana to trace database query times, message broker queue sizes, and background worker task throughput.
*   **Logging:** Configure Serilog to ship logs directly to an aggregation service (e.g. Datadog, Elasticsearch/Kibana, or AWS CloudWatch) using structured JSON format.
