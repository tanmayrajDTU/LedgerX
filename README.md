# 💼 LedgerX — Wealth Management Platform

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

## 📂 Codebase Directory Structure

The project is structured according to clean architecture guidelines to isolate business logic from database, caching, messaging, and API concerns:

```text
WealthPulse/
├── src/
│   ├── LedgerX.Core/                     # Pure domain logic (no external dependencies)
│   │   ├── Entities/                     # Database domain models
│   │   │   ├── User.cs                   # User account info and role mappings
│   │   │   ├── Holding.cs                # Portfolios holding entries (Stock, Mutual Fund, FD, etc.)
│   │   │   ├── Transaction.cs            # Ledger audit log entries (Buys, Sells, Deposits)
│   │   │   ├── Alert.cs                  # Risk monitors (Price limits, Drift, Concentration)
│   │   │   ├── Job.cs                    # Background tasks scheduled for RabbitMQ execution
│   │   │   └── JobExecution.cs           # Tracks worker execution logs, times, and errors
│   │   └── Interfaces/                   # Abstractions implemented by Infrastructure
│   │       ├── ICacheService.cs          # Redis cache access protocols
│   │       ├── IMessageBroker.cs         # RabbitMQ publishing client
│   │       ├── IMarketDataProvider.cs    # Pluggable price checker (Mock vs. Yahoo Live)
│   │       └── IJobTracker.cs            # Helper to manage worker execution logs
│   │
│   ├── LedgerX.Infrastructure/           # Core implementation layer
│   │   ├── Data/                         # EF Core database context
│   │   │   ├── LedgerXDbContext.cs       # PostgreSQL DB schema configuration
│   │   │   └── DbSeeder.cs               # Seeds DB with Indian stock market assets
│   │   ├── Caching/                      # Distributed caching layer
│   │   │   └── RedisCacheService.cs      # Redis client wrapper
│   │   ├── Messaging/                    # Distributed messaging implementation
│   │   │   └── RabbitMQMessageBroker.cs  # Creates Exchanges, DLQs, and queues
│   │   └── Services/                     # Business services
│   │       ├── MarketDataProvider.cs     # Queries Yahoo Finance API or simulated feeds
│   │       ├── TransactionService.cs     # Processes ledger trades & validates whole-shares
│   │       └── PipelineJobScheduler.cs   # Spawns nightly background update tasks
│   │
│   ├── LedgerX.API/                      # Web API presentation layer
│   │   ├── Controllers/                  # API Endpoint controllers
│   │   │   ├── AuthController.cs         # Handles JWT token logins and user validation
│   │   │   ├── SearchController.cs       # Autocomplete proxy queries Yahoo search endpoint
│   │   │   ├── HoldingsController.cs     # Handles portfolio registrations and price seeding
│   │   │   ├── TransactionsController.cs # Submits buy/sell transaction items
│   │   │   └── AdminController.cs        # Updates settings in Redis and monitors worker queues
│   │   └── Program.cs                    # Web application startup configuration
│   │
│   └── LedgerX.Workers/                  # Background worker hosted services
│       ├── Workers/                      # Consumer implementations
│       │   ├── MarketDataWorker.cs       # Pulls and caches current stock market prices
│       │   ├── AnalyticsWorker.cs        # Recalculates volatility & concentration risks
│       │   ├── AlertWorker.cs            # Evaluates watch rules & flags warning logs
│       │   ├── SchedulerService.cs       # Trigger worker cron pipelines (5:00 PM IST)
│       │   └── SchedulerTriggerWorker.cs # Processes manual admin scheduler triggers
│       └── Program.cs                    # Configures DI and Prometheus metrics endpoints
│
└── src/ledgerx-ui/                       # React frontend SPA (Vite + TypeScript)
    └── src/
        ├── pages/                        # Interface views
        │   ├── Login.tsx                 # Secure authentication screen
        │   ├── Dashboard.tsx             # Net Worth charts, Volatility, and upcoming FDs
        │   ├── Holdings.tsx              # Portfolio table & search autocomplete form drawer
        │   ├── Transactions.tsx          # Ledger logging view and trade forms
        │   ├── Alerts.tsx                # Risk parameters config and warnings panel
        │   └── Admin.tsx                 # Live database stats & distributed switchboard
        └── services/
            └── api.ts                    # Axios client instance with JWT token interceptors
```

---

## 🗄️ Database Schema Layout

The relational database schema is configured in PostgreSQL under the following layout:

```mermaid
erDiagram
    Users {
        Guid Id PK
        string Username
        string Email
        string PasswordHash
        string Role
    }
    Holdings {
        Guid Id PK
        Guid UserId FK
        string AssetType
        string SymbolOrName
        decimal QuantityOrUnits
        decimal BuyPriceOrNAV
        decimal Principal
        decimal InterestRate
        DateTime StartDate
        DateTime MaturityDate
        decimal Balance
    }
    Transactions {
        Guid Id PK
        Guid HoldingId FK
        Guid UserId FK
        string TransactionType
        decimal Amount
        decimal QuantityOrUnits
        decimal PriceOrNAV
        DateTime TransactionDate
    }
    PriceHistory {
        Guid Id PK
        string SymbolOrName
        string AssetType
        decimal Price
        DateTime PriceDate
    }
    Alerts {
        Guid Id PK
        Guid UserId FK
        Guid HoldingId FK
        string AlertType
        decimal ThresholdValue
        decimal CurrentValue
        string Message
        bool IsTriggered
        DateTime TriggeredAt
    }

    Users ||--o{ Holdings : owns
    Holdings ||--o{ Transactions : audits
    Users ||--o{ Transactions : triggers
    Users ||--o{ Alerts : configures
    Holdings ||--o{ Alerts : monitors
```

---

## 🔌 API Endpoint Reference

The ASP.NET Core API exposes the following endpoints (protected by JWT token auth):

| Method | Endpoint | Description | Auth Required |
|:---|:---|:---|:---|
| **POST** | `/api/auth/login` | Returns JWT token for credentials. | No |
| **GET** | `/api/search?q={query}` | Autocomplete search proxying Yahoo Finance. | Yes |
| **GET** | `/api/holdings` | Retrieves user's portfolios holdings (cached). | Yes |
| **POST** | `/api/holdings` | Registers a new asset holding (clears cache). | Yes |
| **GET** | `/api/transactions` | Retrieves historical transaction audit logs. | Yes |
| **POST** | `/api/transactions` | Records a new trade or FD ledger entry. | Yes |
| **GET** | `/api/alerts` | Retrieves risk configurations and warnings. | Yes |
| **POST** | `/api/alerts` | Registers a new price limit or concentration alert. | Yes |
| **POST** | `/api/alerts/{id}/dismiss` | Clears a triggered risk alert. | Yes |
| **GET** | `/api/admin/config` | Retrieves the live data & fault configurations. | Yes (Admin) |
| **POST** | `/api/admin/config` | Updates cache configurations in Redis. | Yes (Admin) |
| **GET** | `/api/admin/jobs` | Retrieves task statuses and executions log. | Yes (Admin) |
| **POST** | `/api/admin/scheduler/trigger` | Manually triggers the nightly pipeline jobs. | Yes (Admin) |

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
